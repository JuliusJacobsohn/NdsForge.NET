using System.Security.Cryptography;

namespace NdsForge;

/// <summary>
/// Overlays typed DSi metadata, layout-owned regions, and explicitly requested authentication fields onto the
/// extended header. Reserved template bytes survive, while stale size, digest, HMAC, and signature claims do not.
/// </summary>
internal static class NdsDsiHeaderWriter
{
    /// <summary>
    /// Writes bytes <c>0x180</c>-<c>0xFFF</c> after all DSi program and total-image regions are final, then either
    /// clears or recomputes integrity fields according to the recipe's named policy.
    /// </summary>
    /// <param name="header">Complete mutable reserved header area of at least 0x1000 bytes.</param>
    /// <param name="builder">Validated DSi recipe supplying Programs and typed metadata.</param>
    /// <param name="layout">Final common and DSi physical regions.</param>
    /// <param name="content">Exact payload bytes used for HMAC-SHA1 input.</param>
    /// <param name="digestResult">Generated hierarchy bytes and master HMAC, or <see langword="null"/> when disabled.</param>
    public static void Write(
        Span<byte> header,
        NdsImageBuilder builder,
        NdsImageBuildLayout layout,
        NdsImageBuildContent content,
        NdsDsiDigestBuildResult? digestResult)
    {
        NdsDsiBuildMetadata metadata = builder.DsiMetadata!;
        metadata.ExtensionTemplate.Span.CopyTo(header[0x180..0x1000]);
        metadata.MemoryBankSettings.Span.CopyTo(header[0x180..0x1B0]);
        NdsBinary.WriteUInt32(header, 0x1B0, metadata.RegionFlags);
        NdsBinary.WriteUInt32(header, 0x1B4, metadata.AccessControl);
        NdsBinary.WriteUInt32(header, 0x1B8, metadata.ScfgExtMask);
        header[0x1BF] = metadata.ApplicationFlags;
        WriteProgram(header, 0x1C0, layout.Arm9i!.Value, builder.Arm9i!);
        WriteProgram(header, 0x1D0, layout.Arm7i!.Value, builder.Arm7i!);
        NdsBinary.WriteUInt32(header, 0x1D4, metadata.Arm7DeviceListAddress);

        WriteDigestMetadata(header, layout, metadata.Digests, digestResult);
        NdsBinary.WriteUInt32(header, 0x208, checked((uint)(builder.Banner?.RawData.Length ?? 0)));
        NdsBinary.WriteUInt32(header, 0x20C, 0x00010000);
        NdsBinary.WriteUInt32(header, 0x210, checked((uint)layout.PhysicalSize));
        WriteRegion(header, 0x220, metadata.ModcryptArea1);
        WriteRegion(header, 0x228, metadata.ModcryptArea2);
        NdsBinary.WriteUInt32(header, 0x230, (uint)metadata.TitleId);
        NdsBinary.WriteUInt32(header, 0x234, (uint)(metadata.TitleId >> 32));
        NdsBinary.WriteUInt32(header, 0x238, metadata.PublicSaveSize);
        NdsBinary.WriteUInt32(header, 0x23C, metadata.PrivateSaveSize);
        metadata.AgeRatings.Span.CopyTo(header[0x2F0..0x300]);
        ClearAuthenticationFields(header);
        if (digestResult is not null)
        {
            digestResult.MasterHmac.CopyTo(header[0x328..0x33C]);
        }

        WriteHmacs(header, builder, content, metadata.Integrity);
    }

    /// <summary>Writes a coherent all-zero or fully populated digest hierarchy descriptor.</summary>
    /// <param name="header">Mutable extended header.</param>
    /// <param name="layout">Covered content and generated table regions.</param>
    /// <param name="options">Configured granularity, or <see langword="null"/> when digests are absent.</param>
    /// <param name="result">Generated table bytes used to cross-check planned lengths.</param>
    private static void WriteDigestMetadata(
        Span<byte> header,
        NdsImageBuildLayout layout,
        NdsDsiDigestOptions? options,
        NdsDsiDigestBuildResult? result)
    {
        header[0x1E0..0x208].Clear();
        if (options is null || result is null)
        {
            return;
        }

        if (layout.SectorHashTable.Length != result.SectorHashes.Length ||
            layout.BlockHashTable.Length != result.BlockHashes.Length)
        {
            throw new InvalidDataException("Generated DSi digest bytes disagree with their planned Regions.");
        }

        WriteRegion(header, 0x1E0, layout.NtrDigest);
        WriteRegion(header, 0x1E8, layout.TwlDigest);
        WriteRegion(header, 0x1F0, layout.SectorHashTable);
        WriteRegion(header, 0x1F8, layout.BlockHashTable);
        NdsBinary.WriteUInt32(header, 0x200, checked((uint)options.SectorSize));
        NdsBinary.WriteUInt32(header, 0x204, checked((uint)options.BlockSectorCount));
    }

    /// <summary>
    /// Finalizes the optional development marker after common logo and header CRCs have reached their stored
    /// values, because those fields lie inside the marker's 0xE00-byte SHA-1 input.
    /// </summary>
    /// <param name="header">Complete header with every field except the signature area finalized.</param>
    /// <param name="integrity">Clear, development-marker, or application-supplied RSA signing policy.</param>
    public static void FinalizeSignature(Span<byte> header, NdsDsiIntegrityOptions integrity)
    {
        if (integrity.SignatureMode == NdsDsiSignatureMode.RsaSha1)
        {
            INdsDsiSignatureProvider provider = integrity.SignatureProvider ??
                throw new InvalidDataException("RSA signature mode has no signing provider.");
            provider.SignHeader(header[..0xE00], header[0xF80..0x1000]);
            return;
        }

        WriteDevelopmentSignature(header, integrity.SignatureMode);
    }

    /// <summary>Encodes a DSi Program's ROM offset, single runtime address, and exact byte length.</summary>
    /// <param name="header">Mutable extended header.</param>
    /// <param name="offset">Tuple start: <c>0x1C0</c> for ARM9i or <c>0x1D0</c> for ARM7i.</param>
    /// <param name="region">Final physical payload interval.</param>
    /// <param name="program">Definition whose validated load and entry addresses are identical.</param>
    private static void WriteProgram(Span<byte> header, int offset, NdsRegion region, NdsProgramDefinition program)
    {
        NdsBinary.WriteUInt32(header, offset, checked((uint)region.Offset));
        NdsBinary.WriteUInt32(header, offset + 8, program.LoadAddress);
        NdsBinary.WriteUInt32(header, offset + 12, checked((uint)region.Length));
    }

    /// <summary>Writes an optional modcrypt offset/length pair without inferring encryption state from nonzero values.</summary>
    /// <param name="header">Mutable extended header.</param>
    /// <param name="offset">Start of the two adjacent little-endian words.</param>
    /// <param name="region">Caller-declared interval validated later against the completed image.</param>
    private static void WriteRegion(Span<byte> header, int offset, NdsRegion region)
    {
        NdsBinary.WriteUInt32(header, offset, checked((uint)region.Offset));
        NdsBinary.WriteUInt32(header, offset + 4, checked((uint)region.Length));
    }

    /// <summary>
    /// Removes authentication bytes inherited from a template before any selected hashes are calculated. Digest
    /// master and alternate ARM9 HMACs stay clear because this builder does not yet emit their dependent structures.
    /// </summary>
    /// <param name="header">Mutable extended header containing possibly stale template fields.</param>
    private static void ClearAuthenticationFields(Span<byte> header)
    {
        header[0x300..0x378].Clear();
        header[0x3A0..0x3B4].Clear();
        header[0xF80..0x1000].Clear();
    }

    /// <summary>Computes component HMAC-SHA1 values only when the caller selected a concrete key policy.</summary>
    /// <param name="header">Mutable extended header receiving five 20-byte digests.</param>
    /// <param name="builder">Recipe supplying optional Banner bytes.</param>
    /// <param name="content">Frozen common and DSi Program bytes.</param>
    /// <param name="integrity">Policy containing copied key material or explicit unauthenticated behavior.</param>
    private static void WriteHmacs(
        Span<byte> header,
        NdsImageBuilder builder,
        NdsImageBuildContent content,
        NdsDsiIntegrityOptions integrity)
    {
        if (!integrity.ComputesHmacSha1)
        {
            return;
        }

        WriteHmac(header[0x300..0x314], integrity.HmacKey.Span, content.Arm9Data.Span);
        WriteHmac(header[0x314..0x328], integrity.HmacKey.Span, content.Arm7Data.Span);
        ReadOnlySpan<byte> bannerData = builder.Banner is null ? [] : builder.Banner.RawData.Span;
        WriteHmac(header[0x33C..0x350], integrity.HmacKey.Span, bannerData);
        WriteHmac(header[0x350..0x364], integrity.HmacKey.Span, content.Arm9iData.Span);
        WriteHmac(header[0x364..0x378], integrity.HmacKey.Span, content.Arm7iData.Span);
    }

    /// <summary>Runs HMAC-SHA1 over one exact component and copies its fixed 20-byte result into the header.</summary>
    /// <param name="destination">Exactly one HMAC field.</param>
    /// <param name="key">Explicit caller or named-compatibility key.</param>
    /// <param name="data">Exact component bytes, excluding layout padding.</param>
    private static void WriteHmac(Span<byte> destination, ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
#pragma warning disable CA5350 // The DSi header format mandates HMAC-SHA1; this API does not recommend SHA-1 for new protocols.
        HMACSHA1.HashData(key, data, destination);
#pragma warning restore CA5350
    }

    /// <summary>Writes the explicitly non-RSA no$gba marker after every signed header byte is final.</summary>
    /// <param name="header">Mutable extended header whose first 0xE00 bytes are hashed.</param>
    /// <param name="mode">Clear-field or development-marker policy.</param>
    private static void WriteDevelopmentSignature(Span<byte> header, NdsDsiSignatureMode mode)
    {
        if (mode != NdsDsiSignatureMode.NoGbaDevelopmentMarker)
        {
            return;
        }

        Span<byte> signature = header[0xF80..0x1000];
        signature.Fill(0xFF);
        signature[0] = 0;
        signature[1] = 1;
        signature[0x6B] = 0;
#pragma warning disable CA5350 // The no$gba development marker is defined as SHA-1 and is explicitly documented as non-authenticating.
        SHA1.HashData(header[..0xE00], signature[0x6C..0x80]);
#pragma warning restore CA5350
    }
}
