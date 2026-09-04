using System.Buffers.Binary;

namespace NdsForge.Shared;

/// <summary>Internal size metadata shared by ROM-aware and standalone BLZ entry points.</summary>
internal readonly record struct BlzEngineInfo(
    int EncodedLength,
    int DecodedLength,
    int UncompressedPrefixLength,
    int CompressedRegionLength,
    byte HeaderLength,
    int AdditionalLength);

/// <summary>Dependency-free bottom-up LZ implementation compiled into each package that needs it.</summary>
internal static class BlzEngine
{
    /// <summary>Caps decoded arrays at the runtime's maximum ordinary byte-array length.</summary>
    internal const int DefaultMaximumDecodedLength = 0x7FFFFFC7;

    /// <summary>Validates the trailing envelope without interpreting its token stream.</summary>
    internal static bool TryInspect(ReadOnlySpan<byte> data, out BlzEngineInfo info)
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

    /// <summary>Expands a structurally valid bottom-up token stream within a caller-selected allocation limit.</summary>
    internal static byte[] Decompress(ReadOnlySpan<byte> data, int maximumDecodedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDecodedLength);
        if (!TryInspect(data, out BlzEngineInfo info))
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

    /// <summary>Creates a deterministic greedy bottom-up stream when it is smaller than the decoded input.</summary>
    internal static bool TryCompress(
        ReadOnlySpan<byte> data,
        out byte[] encoded,
        int uncompressedPrefixLength)
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

    /// <summary>Selects the nearest longest already-decoded match for stable greedy output.</summary>
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
