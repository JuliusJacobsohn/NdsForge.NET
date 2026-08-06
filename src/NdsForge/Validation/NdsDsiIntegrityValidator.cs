using System.Security.Cryptography;

namespace NdsForge;

/// <summary>Validates DSi-specific bounds and authentication fields without assigning trust to unprovided keys.</summary>
internal static class NdsDsiIntegrityValidator
{
    /// <summary>Appends deterministic DSi findings after common DS validation has completed.</summary>
    /// <param name="image">Live image used for bounded component hashing.</param>
    /// <param name="diagnostics">Shared ordered accumulator.</param>
    /// <param name="options">Optional HMAC trust material and development-marker policy.</param>
    public static void Validate(
        NdsImage image,
        List<NdsDiagnostic> diagnostics,
        NdsValidationOptions options)
    {
        NdsDsiHeader? dsi = image.Header.Dsi;
        if (dsi is null)
        {
            return;
        }

        if (dsi.TotalImageSize > image.Length || dsi.TotalImageSize < image.Header.UsedImageSize)
        {
            diagnostics.Add(new(
                "NDS1301",
                NdsDiagnosticSeverity.Error,
                $"The DSi total image size 0x{dsi.TotalImageSize:X} is inconsistent with the physical or common content size."));
        }

        ValidateOptionalRegion(image, diagnostics, "NDS1302", "first modcrypt area", dsi.ModcryptArea1);
        ValidateOptionalRegion(image, diagnostics, "NDS1303", "second modcrypt area", dsi.ModcryptArea2);
        ValidateDigestMetadata(image, diagnostics, dsi);
        if (!options.DsiHmacKey.IsEmpty)
        {
            ValidateHmacs(image, diagnostics, dsi, options.DsiHmacKey.Span);
        }

        if (options.ValidateDsiDevelopmentSignature)
        {
            ValidateDevelopmentSignature(image, diagnostics, dsi);
        }
    }

    /// <summary>Checks a nonempty optional region against physical image bounds.</summary>
    /// <param name="image">Image supplying the physical length.</param>
    /// <param name="diagnostics">Accumulator receiving a stable bounds error.</param>
    /// <param name="code">Stable diagnostic code for this field.</param>
    /// <param name="name">Human-readable field identity.</param>
    /// <param name="region">Optional half-open interval.</param>
    private static void ValidateOptionalRegion(
        NdsImage image,
        List<NdsDiagnostic> diagnostics,
        string code,
        string name,
        NdsRegion region)
    {
        if (!region.IsEmpty && (region.Offset < 0 || region.Length < 0 || region.Offset > image.Length - region.Length))
        {
            diagnostics.Add(new(
                code,
                NdsDiagnosticSeverity.Error,
                $"The DSi {name} at 0x{region.Offset:X}+0x{region.Length:X} is outside the image.",
                region));
        }
    }

    /// <summary>Requires a coherent all-or-nothing digest hierarchy before any future content hashes are trusted.</summary>
    /// <param name="image">Image supplying bounds for both digest tables and covered ranges.</param>
    /// <param name="diagnostics">Accumulator receiving metadata and bounds findings.</param>
    /// <param name="dsi">Parsed digest regions and granularity values.</param>
    private static void ValidateDigestMetadata(
        NdsImage image,
        List<NdsDiagnostic> diagnostics,
        NdsDsiHeader dsi)
    {
        bool hasDigestMetadata = !dsi.NtrDigest.IsEmpty || !dsi.TwlDigest.IsEmpty ||
            !dsi.SectorHashTable.IsEmpty || !dsi.BlockHashTable.IsEmpty;
        if (!hasDigestMetadata)
        {
            return;
        }

        if (dsi.DigestSectorSize == 0 ||
            (dsi.DigestSectorSize & (dsi.DigestSectorSize - 1)) != 0 ||
            dsi.DigestBlockSectorCount == 0 ||
            dsi.SectorHashTable.IsEmpty ||
            dsi.BlockHashTable.IsEmpty)
        {
            diagnostics.Add(new(
                "NDS1304",
                NdsDiagnosticSeverity.Error,
                "The DSi digest hierarchy requires power-of-two sectors, a nonzero block sector count, and both hash tables."));
        }

        ValidateOptionalRegion(image, diagnostics, "NDS1305", "sector hash table", dsi.SectorHashTable);
        ValidateOptionalRegion(image, diagnostics, "NDS1306", "block hash table", dsi.BlockHashTable);
        ValidateOptionalRegion(image, diagnostics, "NDS1307", "NTR digest content", dsi.NtrDigest);
        ValidateOptionalRegion(image, diagnostics, "NDS1308", "TWL digest content", dsi.TwlDigest);
    }

    /// <summary>Recomputes all five component HMAC fields for which the DSi header exposes exact regions.</summary>
    /// <param name="image">Image used for bounded reads.</param>
    /// <param name="diagnostics">Accumulator receiving mismatches.</param>
    /// <param name="dsi">Stored HMAC bytes and Banner size.</param>
    /// <param name="key">Caller-supplied key whose provenance remains outside the library.</param>
    private static void ValidateHmacs(
        NdsImage image,
        List<NdsDiagnostic> diagnostics,
        NdsDsiHeader dsi,
        ReadOnlySpan<byte> key)
    {
        ValidateHmac(image, diagnostics, "NDS1310", "ARM9", image.Header.Arm9.Data, dsi.Arm9Hmac.Span, key);
        ValidateHmac(image, diagnostics, "NDS1311", "ARM7", image.Header.Arm7.Data, dsi.Arm7Hmac.Span, key);
        var banner = new NdsRegion(image.Header.BannerOffset, dsi.BannerSize);
        ValidateHmac(image, diagnostics, "NDS1312", "Banner", banner, dsi.BannerHmac.Span, key);
        ValidateHmac(image, diagnostics, "NDS1313", "ARM9i", image.Header.Arm9i!.Data, dsi.Arm9iHmac.Span, key);
        ValidateHmac(image, diagnostics, "NDS1314", "ARM7i", image.Header.Arm7i!.Data, dsi.Arm7iHmac.Span, key);
    }

    /// <summary>Streams one component through the mandated HMAC-SHA1 algorithm and reports a constant-time mismatch.</summary>
    /// <param name="image">Image opening a bounded component stream.</param>
    /// <param name="diagnostics">Accumulator receiving the mismatch.</param>
    /// <param name="code">Stable component-specific diagnostic code.</param>
    /// <param name="name">Human-readable component identity.</param>
    /// <param name="region">Exact bytes covered by the stored field.</param>
    /// <param name="stored">Twenty stored digest bytes.</param>
    /// <param name="key">Explicit validation key.</param>
    private static void ValidateHmac(
        NdsImage image,
        List<NdsDiagnostic> diagnostics,
        string code,
        string name,
        NdsRegion region,
        ReadOnlySpan<byte> stored,
        ReadOnlySpan<byte> key)
    {
        if (region.Offset < 0 || region.Length < 0 || region.Offset > image.Length - region.Length)
        {
            return;
        }

#pragma warning disable CA5350 // DSi authentication fields are specified as HMAC-SHA1; callers explicitly opt into key validation.
        using IncrementalHash hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, key);
#pragma warning restore CA5350
        using Stream stream = image.OpenRead(region);
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        byte[] calculated = hash.GetHashAndReset();
        if (!CryptographicOperations.FixedTimeEquals(stored, calculated))
        {
            diagnostics.Add(new(
                code,
                NdsDiagnosticSeverity.Error,
                $"The DSi {name} HMAC-SHA1 does not match the supplied key.",
                region));
        }
    }

    /// <summary>Recognizes and verifies the explicitly non-RSA no$gba marker without judging unknown retail signatures.</summary>
    /// <param name="image">Image supplying finalized header bytes.</param>
    /// <param name="diagnostics">Accumulator receiving a marker mismatch.</param>
    /// <param name="dsi">Stored 128-byte signature field.</param>
    private static void ValidateDevelopmentSignature(
        NdsImage image,
        List<NdsDiagnostic> diagnostics,
        NdsDsiHeader dsi)
    {
        ReadOnlySpan<byte> signature = dsi.RsaSignature.Span;
        if (signature[0] != 0 || signature[1] != 1 || signature[0x6B] != 0)
        {
            return;
        }

#pragma warning disable CA5350 // The recognized development marker is defined as SHA-1 and is not treated as a secure signature.
        byte[] calculated = SHA1.HashData(image.Header.RawData.Span[..0xE00]);
#pragma warning restore CA5350
        if (!CryptographicOperations.FixedTimeEquals(signature[0x6C..0x80], calculated))
        {
            diagnostics.Add(new(
                "NDS1315",
                NdsDiagnosticSeverity.Error,
                "The no$gba DSi development marker does not match the finalized extended header.",
                new(0xFEC, 20)));
        }
    }
}
