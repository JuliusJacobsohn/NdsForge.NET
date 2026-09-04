namespace NdsForge.Nitro.Compression;

/// <summary>Implements Nintendo BIOS repeated-byte and literal blocks identified by type byte <c>0x30</c>.</summary>
public static class RleCodec
{
    /// <summary>Expands bounded literal and repeated-byte blocks to the common header's exact output length.</summary>
    /// <param name="data">Complete run-length stream including its common header.</param>
    /// <param name="maximumDecodedLength">Largest output accepted from untrusted metadata.</param>
    /// <returns>Exactly the decoded bytes declared by the header.</returns>
    public static byte[] Decompress(
        ReadOnlySpan<byte> data,
        int maximumDecodedLength = NitroCompression.DefaultMaximumDecodedLength)
    {
        NitroCompressionInfo info = NitroCompression.ReadHeader(
            data,
            NitroCompressionType.RunLength,
            maximumDecodedLength);
        byte[] output = new byte[info.DecodedLength];
        int source = info.HeaderLength;
        int destination = 0;
        while (destination < output.Length)
        {
            if (source >= data.Length)
            {
                throw new InvalidDataException("The run-length stream ended before its declared output was produced.");
            }

            byte control = data[source++];
            bool repeated = (control & 0x80) != 0;
            int length = (control & 0x7F) + (repeated ? 3 : 1);
            if (length > output.Length - destination)
            {
                throw new InvalidDataException("A run-length block exceeds the declared output size.");
            }

            if (repeated)
            {
                if (source >= data.Length)
                {
                    throw new InvalidDataException("The run-length stream ends before a repeated byte.");
                }

                output.AsSpan(destination, length).Fill(data[source++]);
            }
            else
            {
                if (data.Length - source < length)
                {
                    throw new InvalidDataException("The run-length stream ends inside a literal block.");
                }

                data.Slice(source, length).CopyTo(output.AsSpan(destination));
                source += length;
            }

            destination += length;
        }

        return output;
    }

    /// <summary>Creates deterministic maximal repeated runs and literal blocks from nonempty bytes.</summary>
    /// <param name="data">Nonempty uncompressed bytes.</param>
    /// <returns>A self-describing run-length stream.</returns>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        byte[] header = NitroCompression.CreateHeader(NitroCompressionType.RunLength, data.Length);
        var output = new List<byte>(header.Length + data.Length);
        output.AddRange(header);
        int position = 0;
        while (position < data.Length)
        {
            int runLength = GetRunLength(data, position, 130);
            if (runLength >= 3)
            {
                output.Add((byte)(0x80 | (runLength - 3)));
                output.Add(data[position]);
                position += runLength;
                continue;
            }

            int literalStart = position;
            position += runLength;
            while (position < data.Length && position - literalStart < 128)
            {
                runLength = GetRunLength(data, position, 130);
                if (runLength >= 3)
                {
                    break;
                }

                position += Math.Min(runLength, 128 - (position - literalStart));
            }

            int literalLength = position - literalStart;
            output.Add((byte)(literalLength - 1));
            for (int index = literalStart; index < position; index++)
            {
                output.Add(data[index]);
            }
        }

        return output.ToArray();
    }

    /// <summary>Counts a same-byte sequence without crossing either the input or one control byte's capacity.</summary>
    private static int GetRunLength(ReadOnlySpan<byte> data, int position, int maximumLength)
    {
        int length = 1;
        int limit = Math.Min(data.Length - position, maximumLength);
        while (length < limit && data[position + length] == data[position])
        {
            length++;
        }

        return length;
    }
}
