namespace NdsForge.Nitro.Compression;

/// <summary>Describes a structurally valid bottom-up LZ stream without expanding its payload.</summary>
public readonly record struct BlzInfo
{
    /// <summary>Creates a validated projection of one trailing BLZ envelope.</summary>
    /// <param name="encodedLength">Total stored bytes, including any uncompressed prefix and the trailing header.</param>
    /// <param name="decodedLength">Total bytes produced after expanding the compressed suffix.</param>
    /// <param name="uncompressedPrefixLength">Leading bytes copied verbatim before backward decoding begins.</param>
    /// <param name="compressedRegionLength">Stored suffix length, including padding and the trailing header.</param>
    /// <param name="headerLength">Trailing header plus zero to three alignment bytes.</param>
    /// <param name="additionalLength">Number of bytes by which decoded output exceeds stored input.</param>
    public BlzInfo(
        int encodedLength,
        int decodedLength,
        int uncompressedPrefixLength,
        int compressedRegionLength,
        byte headerLength,
        int additionalLength)
    {
        EncodedLength = encodedLength;
        DecodedLength = decodedLength;
        UncompressedPrefixLength = uncompressedPrefixLength;
        CompressedRegionLength = compressedRegionLength;
        HeaderLength = headerLength;
        AdditionalLength = additionalLength;
    }

    /// <summary>Gets total stored bytes, including any uncompressed prefix and the trailing header.</summary>
    public int EncodedLength { get; }

    /// <summary>Gets total bytes produced after expanding the compressed suffix.</summary>
    public int DecodedLength { get; }

    /// <summary>Gets leading bytes copied verbatim before backward decoding begins.</summary>
    public int UncompressedPrefixLength { get; }

    /// <summary>Gets the stored suffix length, including padding and the trailing header.</summary>
    public int CompressedRegionLength { get; }

    /// <summary>Gets the trailing header plus zero to three alignment bytes.</summary>
    public byte HeaderLength { get; }

    /// <summary>Gets the number of bytes by which decoded output exceeds stored input.</summary>
    public int AdditionalLength { get; }
}
