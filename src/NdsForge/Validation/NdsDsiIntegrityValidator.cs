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

        if (dsi.TotalImageSize != 0 &&
            (dsi.TotalImageSize > image.Length || dsi.TotalImageSize < image.Header.UsedImageSize))
        {
            diagnostics.Add(new(
                "NDS1301",
                NdsDiagnosticSeverity.Error,
                $"The DSi total image size 0x{dsi.TotalImageSize:X} is inconsistent with the physical or common content size."));
        }

        ValidateOptionalRegion(image, diagnostics, "NDS1302", "first modcrypt area", dsi.ModcryptArea1);
        ValidateOptionalRegion(image, diagnostics, "NDS1303", "second modcrypt area", dsi.ModcryptArea2);
        bool digestMetadataValid = ValidateDigestMetadata(image, diagnostics, dsi, options);
        if (!options.DsiHmacKey.IsEmpty)
        {
            ValidateHmacs(image, diagnostics, dsi, options.DsiHmacKey.Span);
            if (digestMetadataValid && !dsi.SectorHashTable.IsEmpty)
            {
                ValidateDigestHierarchy(image, diagnostics, dsi, options, options.DsiHmacKey.Span);
            }
        }

        if (options.ValidateDsiDevelopmentSignature)
        {
            ValidateDevelopmentSignature(image, diagnostics, dsi);
        }

        if (options.DsiRsaPublicKey is not null && !dsi.VerifyRsaSignature(options.DsiRsaPublicKey))
        {
            diagnostics.Add(new(
                "NDS1321",
                NdsDiagnosticSeverity.Error,
                "The DSi header RSA-SHA1 signature does not match the caller-trusted public key.",
                new(0xF80, 128)));
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
    /// <param name="options">Resource bounds for table materialization.</param>
    /// <returns><see langword="true"/> when keyed content verification can proceed safely.</returns>
    private static bool ValidateDigestMetadata(
        NdsImage image,
        List<NdsDiagnostic> diagnostics,
        NdsDsiHeader dsi,
        NdsValidationOptions options)
    {
        bool hasDigestMetadata = !dsi.NtrDigest.IsEmpty || !dsi.TwlDigest.IsEmpty ||
            !dsi.SectorHashTable.IsEmpty || !dsi.BlockHashTable.IsEmpty;
        if (!hasDigestMetadata)
        {
            return true;
        }

        bool valid = true;
        if (dsi.DigestSectorSize is < 0x200 or > 16 * 1024 * 1024 ||
            (dsi.DigestSectorSize & (dsi.DigestSectorSize - 1)) != 0 ||
            dsi.DigestBlockSectorCount is 0 or > 65_536 ||
            dsi.SectorHashTable.IsEmpty ||
            dsi.BlockHashTable.IsEmpty)
        {
            diagnostics.Add(new(
                "NDS1304",
                NdsDiagnosticSeverity.Error,
                "The DSi digest hierarchy requires power-of-two sectors, a nonzero block sector count, and both hash tables."));
            valid = false;
        }

        ValidateOptionalRegion(image, diagnostics, "NDS1305", "sector hash table", dsi.SectorHashTable);
        ValidateOptionalRegion(image, diagnostics, "NDS1306", "block hash table", dsi.BlockHashTable);
        ValidateOptionalRegion(image, diagnostics, "NDS1307", "NTR digest content", dsi.NtrDigest);
        ValidateOptionalRegion(image, diagnostics, "NDS1308", "TWL digest content", dsi.TwlDigest);
        if (dsi.SectorHashTable.Length > options.MaxDsiDigestTableBytes ||
            dsi.BlockHashTable.Length > options.MaxDsiDigestTableBytes)
        {
            diagnostics.Add(new(
                "NDS1309",
                NdsDiagnosticSeverity.Error,
                "A DSi digest table exceeds the configured validation allocation limit."));
            valid = false;
        }

        if (!valid || !IsWithin(image, dsi.NtrDigest) || !IsWithin(image, dsi.TwlDigest) ||
            !IsWithin(image, dsi.SectorHashTable) || !IsWithin(image, dsi.BlockHashTable))
        {
            return false;
        }

        long sectorCount;
        long expectedSectorBytes;
        long blockCount;
        long expectedBlockBytes;
        try
        {
            sectorCount = checked(
                DivideRoundUp(dsi.NtrDigest.Length, dsi.DigestSectorSize) +
                DivideRoundUp(dsi.TwlDigest.Length, dsi.DigestSectorSize));
            expectedSectorBytes = checked(sectorCount * 20);
            blockCount = DivideRoundUp(sectorCount, dsi.DigestBlockSectorCount);
            expectedBlockBytes = checked(blockCount * 20);
        }
        catch (OverflowException)
        {
            diagnostics.Add(new(
                "NDS1316",
                NdsDiagnosticSeverity.Error,
                "The DSi digest hierarchy overflows supported sector or table counts."));
            return false;
        }

        if (dsi.SectorHashTable.Length != expectedSectorBytes || dsi.BlockHashTable.Length != expectedBlockBytes)
        {
            diagnostics.Add(new(
                "NDS1316",
                NdsDiagnosticSeverity.Error,
                $"DSi digest table lengths do not match {sectorCount} content sectors and {blockCount} hash blocks."));
            return false;
        }

        return true;
    }

    /// <summary>Recomputes sector, block, and master HMACs after structural metadata has passed all bounds checks.</summary>
    /// <param name="image">Image supplying covered content and stored tables.</param>
    /// <param name="diagnostics">Accumulator receiving bounded mismatch findings.</param>
    /// <param name="dsi">Validated hierarchy metadata and stored master HMAC.</param>
    /// <param name="options">Mismatch and allocation limits.</param>
    /// <param name="key">Caller-supplied HMAC key.</param>
    private static void ValidateDigestHierarchy(
        NdsImage image,
        List<NdsDiagnostic> diagnostics,
        NdsDsiHeader dsi,
        NdsValidationOptions options,
        ReadOnlySpan<byte> key)
    {
        byte[] sectorHashes = ReadRegion(image, dsi.SectorHashTable);
        byte[] blockHashes = ReadRegion(image, dsi.BlockHashTable);
        int failureCount = 0;
        int sectorIndex = 0;
        ValidateRegionSectors(
            image,
            diagnostics,
            dsi.NtrDigest,
            dsi.DigestSectorSize,
            sectorHashes,
            ref sectorIndex,
            ref failureCount,
            options.MaxDsiDigestFailures,
            key);
        ValidateRegionSectors(
            image,
            diagnostics,
            dsi.TwlDigest,
            dsi.DigestSectorSize,
            sectorHashes,
            ref sectorIndex,
            ref failureCount,
            options.MaxDsiDigestFailures,
            key);

        int blockInputSize = checked((int)dsi.DigestBlockSectorCount * 20);
        for (int blockIndex = 0; blockIndex * blockInputSize < sectorHashes.Length; blockIndex++)
        {
            int inputOffset = checked(blockIndex * blockInputSize);
            ReadOnlySpan<byte> input = sectorHashes.AsSpan(
                inputOffset,
                Math.Min(blockInputSize, sectorHashes.Length - inputOffset));
#pragma warning disable CA5350 // DSi block-table verification is defined as HMAC-SHA1.
            byte[] calculated = HMACSHA1.HashData(key, input);
#pragma warning restore CA5350
            if (!CryptographicOperations.FixedTimeEquals(blockHashes.AsSpan(blockIndex * 20, 20), calculated) &&
                failureCount++ < options.MaxDsiDigestFailures)
            {
                diagnostics.Add(new(
                    "NDS1318",
                    NdsDiagnosticSeverity.Error,
                    $"DSi digest block {blockIndex} does not authenticate its sector-hash group.",
                    new(dsi.BlockHashTable.Offset + (blockIndex * 20L), 20)));
            }
        }

#pragma warning disable CA5350 // The DSi digest master field is defined as HMAC-SHA1 over the block table.
        byte[] master = HMACSHA1.HashData(key, blockHashes);
#pragma warning restore CA5350
        if (!CryptographicOperations.FixedTimeEquals(dsi.DigestMasterHmac.Span, master))
        {
            diagnostics.Add(new(
                "NDS1319",
                NdsDiagnosticSeverity.Error,
                "The DSi digest master HMAC does not authenticate the block hash table.",
                new(0x328, 20)));
        }

        if (failureCount > options.MaxDsiDigestFailures)
        {
            diagnostics.Add(new(
                "NDS1320",
                NdsDiagnosticSeverity.Warning,
                $"Additional DSi digest mismatches were suppressed after {options.MaxDsiDigestFailures} findings."));
        }
    }

    /// <summary>Authenticates each sector in one covered range against consecutive stored first-level entries.</summary>
    /// <param name="image">Image supplying content bytes.</param>
    /// <param name="diagnostics">Accumulator receiving bounded sector mismatches.</param>
    /// <param name="region">NTR or TWL covered interval.</param>
    /// <param name="sectorSize">Maximum bytes per HMAC input.</param>
    /// <param name="storedHashes">Complete first-level table.</param>
    /// <param name="sectorIndex">Shared NTR-then-TWL table index.</param>
    /// <param name="failureCount">Shared mismatch counter.</param>
    /// <param name="failureLimit">Maximum detailed findings.</param>
    /// <param name="key">Caller-supplied HMAC key.</param>
    private static void ValidateRegionSectors(
        NdsImage image,
        List<NdsDiagnostic> diagnostics,
        NdsRegion region,
        uint sectorSize,
        byte[] storedHashes,
        ref int sectorIndex,
        ref int failureCount,
        int failureLimit,
        ReadOnlySpan<byte> key)
    {
        long offset = 0;
        while (offset < region.Length)
        {
            long length = Math.Min(sectorSize, region.Length - offset);
            var sector = new NdsRegion(region.Offset + offset, length);
            byte[] calculated = CalculateRegionHmac(image, sector, key);
            if (!CryptographicOperations.FixedTimeEquals(storedHashes.AsSpan(sectorIndex * 20, 20), calculated) &&
                failureCount++ < failureLimit)
            {
                diagnostics.Add(new(
                    "NDS1317",
                    NdsDiagnosticSeverity.Error,
                    $"DSi digest sector {sectorIndex} does not match its covered image bytes.",
                    sector));
            }

            sectorIndex++;
            offset += length;
        }
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

        byte[] calculated = CalculateRegionHmac(image, region, key);
        if (!CryptographicOperations.FixedTimeEquals(stored, calculated))
        {
            diagnostics.Add(new(
                code,
                NdsDiagnosticSeverity.Error,
                $"The DSi {name} HMAC-SHA1 does not match the supplied key.",
                region));
        }
    }

    /// <summary>Streams a bounded image interval through the format-mandated HMAC-SHA1 algorithm.</summary>
    /// <param name="image">Image opening the bounded source stream.</param>
    /// <param name="region">Exact HMAC input interval.</param>
    /// <param name="key">Caller-supplied key.</param>
    /// <returns>Exactly twenty calculated digest bytes.</returns>
    private static byte[] CalculateRegionHmac(NdsImage image, NdsRegion region, ReadOnlySpan<byte> key)
    {
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

        return hash.GetHashAndReset();
    }

    /// <summary>Materializes a previously bounded digest table under the caller's allocation limit.</summary>
    /// <param name="image">Image supplying table bytes.</param>
    /// <param name="region">Validated table interval whose length fits a managed array.</param>
    /// <returns>An exact independently owned table copy.</returns>
    private static byte[] ReadRegion(NdsImage image, NdsRegion region)
    {
        byte[] data = new byte[checked((int)region.Length)];
        using Stream stream = image.OpenRead(region);
        stream.ReadExactly(data);
        return data;
    }

    /// <summary>Checks a region with subtraction-based arithmetic that cannot overflow at its exclusive end.</summary>
    /// <param name="image">Image supplying the physical length.</param>
    /// <param name="region">Candidate interval.</param>
    /// <returns><see langword="true"/> when every byte is inside the image.</returns>
    private static bool IsWithin(NdsImage image, NdsRegion region) =>
        region.Offset >= 0 && region.Length >= 0 && region.Offset <= image.Length - region.Length;

    /// <summary>Rounds a count upward into fixed positive partitions using checked integer arithmetic.</summary>
    /// <param name="value">Non-negative byte or entry count.</param>
    /// <param name="divisor">Positive partition size from validated metadata.</param>
    /// <returns>Number of partitions required.</returns>
    private static long DivideRoundUp(long value, uint divisor) => checked((value + divisor - 1) / divisor);

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
