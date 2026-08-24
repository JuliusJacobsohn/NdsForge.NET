using System.Buffers.Binary;

namespace NdsForge.Nitro.Compression;

/// <summary>Inspects, expands, and creates Nintendo bottom-up LZ streams used by programs and overlays.</summary>
public static class BlzCodec
{
    /// <summary>Caps allocation by default while allowing every addressable byte array on the target runtime.</summary>
    public const int DefaultMaximumDecodedLength = 0x7FFFFFC7;

    /// <summary>Reads the fixed trailing header and validates all size relationships without processing tokens.</summary>
    /// <param name="data">Complete stored stream, including a possible verbatim prefix.</param>
    /// <param name="info">Receives decoded sizes when the trailing header is structurally coherent.</param>
    /// <returns><see langword="true"/> only for a size-safe BLZ envelope with a nonempty compressed body.</returns>
    public static bool TryInspect(ReadOnlySpan<byte> data, out BlzInfo info)
    {
        info = default;
        if (data.Length < 8)
        {
            return false;
        }

        uint packedLength = BinaryPrimitives.ReadUInt32LittleEndian(data[^8..]);
        byte headerLength = (byte)(packedLength >> 24);
        int compressedLength = (int)(packedLength & 0x00FFFFFF);
        uint additionalLength = BinaryPrimitives.ReadUInt32LittleEndian(data[^4..]);
        if (headerLength is < 8 or > 11 ||
            compressedLength <= headerLength ||
            compressedLength > data.Length ||
            additionalLength == 0 ||
            additionalLength > int.MaxValue - data.Length)
        {
            return false;
        }

        int prefixLength = data.Length - compressedLength;
        int bodyLength = compressedLength - headerLength;
        if (bodyLength <= 0 || prefixLength > data.Length - headerLength)
        {
            return false;
        }

        info = new(
            data.Length,
            checked(data.Length + (int)additionalLength),
            prefixLength,
            compressedLength,
            headerLength,
            (int)additionalLength);
        return true;
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
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDecodedLength);
        if (!TryInspect(data, out BlzInfo info))
        {
            throw new InvalidDataException("The input does not contain a structurally valid BLZ envelope.");
        }

        if (info.DecodedLength > maximumDecodedLength)
        {
            throw new InvalidDataException(
                $"The BLZ output length {info.DecodedLength} exceeds the configured limit {maximumDecodedLength}.");
        }

        byte[] output = new byte[info.DecodedLength];
        data.CopyTo(output);
        int source = info.EncodedLength - info.HeaderLength;
        int destination = info.DecodedLength;
        while (destination > info.UncompressedPrefixLength)
        {
            if (source <= info.UncompressedPrefixLength)
            {
                throw new InvalidDataException("The BLZ token stream ended before producing its declared output.");
            }

            byte flags = output[--source];
            for (int mask = 0x80; mask != 0 && destination > info.UncompressedPrefixLength; mask >>= 1)
            {
                if ((flags & mask) == 0)
                {
                    if (source <= info.UncompressedPrefixLength)
                    {
                        throw new InvalidDataException("The BLZ stream ends inside a literal token.");
                    }

                    output[--destination] = output[--source];
                    continue;
                }

                if (source - info.UncompressedPrefixLength < 2)
                {
                    throw new InvalidDataException("The BLZ stream ends inside a back-reference token.");
                }

                byte first = output[--source];
                byte second = output[--source];
                int length = (first >> 4) + 3;
                int displacement = (((first & 0x0F) << 8) | second) + 3;
                if (length > destination - info.UncompressedPrefixLength || destination + displacement > output.Length)
                {
                    throw new InvalidDataException("The BLZ stream contains an out-of-range back-reference.");
                }

                for (int index = 0; index < length; index++)
                {
                    destination--;
                    output[destination] = output[destination + displacement];
                }
            }
        }

        return output;
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
        ArgumentOutOfRangeException.ThrowIfNegative(uncompressedPrefixLength);
        if (uncompressedPrefixLength > data.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uncompressedPrefixLength),
                uncompressedPrefixLength,
                "The verbatim prefix cannot extend beyond the input.");
        }

        var reverseGroups = new List<byte[]>();
        int cursor = data.Length;
        int bodyLength = 0;
        int bestEncodedLength = int.MaxValue;
        int bestGroupCount = 0;
        int bestPrefixLength = 0;
        int bestPaddingLength = 0;
        while (cursor > uncompressedPrefixLength)
        {
            var decoderOrder = new List<byte>(24);
            byte flags = 0;
            for (int bit = 7; bit >= 0 && cursor > uncompressedPrefixLength; bit--)
            {
                FindLongestMatch(data, cursor, uncompressedPrefixLength, out int length, out int displacement);
                if (length >= 3)
                {
                    flags |= (byte)(1 << bit);
                    int packedDisplacement = displacement - 3;
                    decoderOrder.Add((byte)(((length - 3) << 4) | (packedDisplacement >> 8)));
                    decoderOrder.Add((byte)packedDisplacement);
                    cursor -= length;
                }
                else
                {
                    decoderOrder.Add(data[--cursor]);
                }
            }

            byte[] group = new byte[decoderOrder.Count + 1];
            for (int index = 0; index < decoderOrder.Count; index++)
            {
                group[index] = decoderOrder[decoderOrder.Count - 1 - index];
            }

            group[^1] = flags;
            reverseGroups.Add(group);
            bodyLength += group.Length;
            int paddingLength = (4 - ((cursor + bodyLength + 8) & 3)) & 3;
            int candidateLength = checked(cursor + bodyLength + paddingLength + 8);
            if (candidateLength < bestEncodedLength &&
                bodyLength + paddingLength + 8 <= 0x00FFFFFF)
            {
                bestEncodedLength = candidateLength;
                bestGroupCount = reverseGroups.Count;
                bestPrefixLength = cursor;
                bestPaddingLength = paddingLength;
            }
        }

        if (bestEncodedLength >= data.Length)
        {
            encoded = [];
            return false;
        }

        int bestBodyLength = 0;
        for (int index = 0; index < bestGroupCount; index++)
        {
            bestBodyLength += reverseGroups[index].Length;
        }

        int compressedLength = bestBodyLength + bestPaddingLength + 8;
        encoded = new byte[bestEncodedLength];
        data[..bestPrefixLength].CopyTo(encoded);
        int output = bestPrefixLength;
        for (int index = bestGroupCount - 1; index >= 0; index--)
        {
            reverseGroups[index].CopyTo(encoded, output);
            output += reverseGroups[index].Length;
        }

        encoded.AsSpan(output, bestPaddingLength).Fill(0xFF);
        byte headerLength = checked((byte)(bestPaddingLength + 8));
        uint packedLength = (uint)compressedLength | ((uint)headerLength << 24);
        BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(bestEncodedLength - 8), packedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(
            encoded.AsSpan(bestEncodedLength - 4),
            checked((uint)(data.Length - bestEncodedLength)));
        return true;
    }

    /// <summary>Searches already decoded bytes to the right of the backward cursor using stable nearest-match tie breaking.</summary>
    private static void FindLongestMatch(
        ReadOnlySpan<byte> data,
        int cursor,
        int prefixLength,
        out int bestLength,
        out int bestDisplacement)
    {
        bestLength = 0;
        bestDisplacement = 0;
        int availableLookahead = data.Length - cursor;
        int maximumDisplacement = Math.Min(0x1002, availableLookahead);
        int maximumLength = Math.Min(18, cursor - prefixLength);
        for (int displacement = 3; displacement <= maximumDisplacement; displacement++)
        {
            int length = 0;
            while (length < maximumLength &&
                data[cursor - 1 - length] == data[cursor - 1 - length + displacement])
            {
                length++;
            }

            if (length > bestLength)
            {
                bestLength = length;
                bestDisplacement = displacement;
                if (length == maximumLength)
                {
                    break;
                }
            }
        }
    }
}
