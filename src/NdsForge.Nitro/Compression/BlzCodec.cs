using NdsForge.Shared;

namespace NdsForge.Nitro.Compression;

/// <summary>Inspects, expands, and creates Nintendo bottom-up LZ streams used by programs and overlays.</summary>
public static class BlzCodec
{
    /// <summary>Caps allocation by default while allowing every addressable byte array on the target runtime.</summary>
    public const int DefaultMaximumDecodedLength = BlzEngine.DefaultMaximumDecodedLength;

    /// <summary>Reads the fixed trailing header and validates all size relationships without processing tokens.</summary>
    /// <param name="data">Complete stored stream, including a possible verbatim prefix.</param>
    /// <param name="info">Receives decoded sizes when the trailing header is structurally coherent.</param>
    /// <returns><see langword="true"/> only for a size-safe BLZ envelope with a nonempty compressed body.</returns>
    public static bool TryInspect(ReadOnlySpan<byte> data, out BlzInfo info)
    {
        bool valid = BlzEngine.TryInspect(data, out BlzEngineInfo engineInfo);
        info = valid
            ? new(
                engineInfo.EncodedLength,
                engineInfo.DecodedLength,
                engineInfo.UncompressedPrefixLength,
                engineInfo.CompressedRegionLength,
                engineInfo.HeaderLength,
                engineInfo.AdditionalLength)
            : default;
        return valid;
    }

    /// <summary>Expands a complete BLZ stream while rejecting truncated tokens, invalid lookbacks, and allocation bombs.</summary>
    /// <param name="data">Stored bytes whose trailing header identifies the compressed suffix.</param>
    /// <param name="maximumDecodedLength">Largest output accepted from untrusted metadata.</param>
    /// <returns>A newly allocated byte array containing the verbatim prefix followed by expanded bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumDecodedLength"/> is negative.</exception>
    /// <exception cref="InvalidDataException">The envelope or backward token stream is invalid.</exception>
    public static byte[] Decompress(
        ReadOnlySpan<byte> data,
        int maximumDecodedLength = DefaultMaximumDecodedLength)
    {
        return BlzEngine.Decompress(data, maximumDecodedLength);
    }

    /// <summary>Attempts deterministic greedy compression of a suffix while leaving a caller-selected prefix verbatim.</summary>
    /// <param name="data">Uncompressed bytes to encode.</param>
    /// <param name="encoded">Receives a BLZ stream, or an empty array when the encoded form would not be smaller.</param>
    /// <param name="uncompressedPrefixLength">Minimum leading bytes excluded from match encoding, commonly 0x4000 for ARM9.</param>
    /// <returns><see langword="true"/> when a smaller, self-describing BLZ representation was produced.</returns>
    public static bool TryCompress(
        ReadOnlySpan<byte> data,
        out byte[] encoded,
        int uncompressedPrefixLength = 0)
    {
        return BlzEngine.TryCompress(data, out encoded, uncompressedPrefixLength);
    }
}
