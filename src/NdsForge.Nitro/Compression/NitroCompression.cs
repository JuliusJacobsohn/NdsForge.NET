using System.Buffers.Binary;

namespace NdsForge.Nitro.Compression;

/// <summary>Inspects shared Nintendo compression headers and dispatches supported codecs by their exact type byte.</summary>
public static class NitroCompression
{
    /// <summary>Matches the largest managed byte array supported by the target runtime.</summary>
    public const int DefaultMaximumDecodedLength = 0x7FFFFFC7;

    /// <summary>Reads a common four- or eight-byte envelope without trusting or decoding its payload.</summary>
    /// <param name="data">Bytes beginning at the compression type.</param>
    /// <param name="info">Receives the recognized codec, output length, and payload offset.</param>
    /// <returns><see langword="true"/> for a supported type with a positive, addressable decoded size.</returns>
    public static bool TryInspect(ReadOnlySpan<byte> data, out NitroCompressionInfo info)
    {
        info = default;
        if (data.Length < 4 || !TryGetType(data[0], out NitroCompressionType type))
        {
            return false;
        }

        int decodedLength = data[1] | (data[2] << 8) | (data[3] << 16);
        int headerLength = 4;
        if (decodedLength == 0)
        {
            if (data.Length < 8)
            {
                return false;
            }

            uint extendedLength = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            if (extendedLength == 0 || extendedLength > int.MaxValue)
            {
                return false;
            }

            decodedLength = (int)extendedLength;
            headerLength = 8;
        }

        info = new(type, decodedLength, headerLength);
        return true;
    }

    /// <summary>Decodes one recognized envelope through its format-specific bounded implementation.</summary>
    /// <param name="data">Complete compressed bytes.</param>
    /// <param name="maximumDecodedLength">Largest output accepted from untrusted metadata.</param>
    /// <returns>Exactly the number of bytes declared by the common header.</returns>
    /// <exception cref="InvalidDataException">The header, token stream, or tree is invalid.</exception>
    public static byte[] Decompress(
        ReadOnlySpan<byte> data,
        int maximumDecodedLength = DefaultMaximumDecodedLength)
    {
        if (!TryInspect(data, out NitroCompressionInfo info))
        {
            throw new InvalidDataException("The input does not begin with a supported Nitro compression envelope.");
        }

        return info.Type switch
        {
            NitroCompressionType.Lz10 => Lz10Codec.Decompress(data, maximumDecodedLength),
            NitroCompressionType.Lz11 => Lz11Codec.Decompress(data, maximumDecodedLength),
            NitroCompressionType.RunLength => RleCodec.Decompress(data, maximumDecodedLength),
            NitroCompressionType.Huffman4 or NitroCompressionType.Huffman8 =>
                HuffmanCodec.Decompress(data, maximumDecodedLength),
            _ => throw new InvalidDataException("The compression type is not supported."),
        };
    }

    /// <summary>Parses one exact expected type and enforces the caller's allocation ceiling.</summary>
    internal static NitroCompressionInfo ReadHeader(
        ReadOnlySpan<byte> data,
        NitroCompressionType expectedType,
        int maximumDecodedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDecodedLength);
        if (!TryInspect(data, out NitroCompressionInfo info) || info.Type != expectedType)
        {
            throw new InvalidDataException($"The input is not a {expectedType} stream.");
        }

        if (info.DecodedLength > maximumDecodedLength)
        {
            throw new InvalidDataException(
                $"The decoded length {info.DecodedLength} exceeds the configured limit {maximumDecodedLength}.");
        }

        return info;
    }

    /// <summary>Creates the shortest common header able to represent one addressable input array.</summary>
    internal static byte[] CreateHeader(NitroCompressionType type, int decodedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decodedLength);
        if (decodedLength <= 0x00FFFFFF)
        {
            return
            [
                (byte)type,
                (byte)decodedLength,
                (byte)(decodedLength >> 8),
                (byte)(decodedLength >> 16),
            ];
        }

        byte[] header = new byte[8];
        header[0] = (byte)type;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)decodedLength);
        return header;
    }

    /// <summary>Restricts recognition to exact variants implemented by the public dispatcher.</summary>
    private static bool TryGetType(byte value, out NitroCompressionType type)
    {
        type = (NitroCompressionType)value;
        return type is NitroCompressionType.Lz10 or
            NitroCompressionType.Lz11 or
            NitroCompressionType.Huffman4 or
            NitroCompressionType.Huffman8 or
            NitroCompressionType.RunLength;
    }
}
