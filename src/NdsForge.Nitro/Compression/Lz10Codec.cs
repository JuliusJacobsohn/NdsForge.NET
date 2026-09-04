namespace NdsForge.Nitro.Compression;

/// <summary>Implements the original Nintendo BIOS LZSS stream identified by type byte <c>0x10</c>.</summary>
public static class Lz10Codec
{
    /// <summary>Expands literals and two-byte back-references into the header-declared output size.</summary>
    /// <param name="data">Complete LZ10 stream including its common header.</param>
    /// <param name="maximumDecodedLength">Largest output accepted from untrusted metadata.</param>
    /// <returns>Exactly the decoded bytes declared by the header.</returns>
    public static byte[] Decompress(
        ReadOnlySpan<byte> data,
        int maximumDecodedLength = NitroCompression.DefaultMaximumDecodedLength)
    {
        NitroCompressionInfo info = NitroCompression.ReadHeader(
            data,
            NitroCompressionType.Lz10,
            maximumDecodedLength);
        byte[] output = new byte[info.DecodedLength];
        int source = info.HeaderLength;
        int destination = 0;
        while (destination < output.Length)
        {
            if (source >= data.Length)
            {
                throw new InvalidDataException("The LZ10 stream ended before its declared output was produced.");
            }

            byte flags = data[source++];
            for (int mask = 0x80; mask != 0 && destination < output.Length; mask >>= 1)
            {
                if ((flags & mask) == 0)
                {
                    if (source >= data.Length)
                    {
                        throw new InvalidDataException("The LZ10 stream ends inside a literal token.");
                    }

                    output[destination++] = data[source++];
                    continue;
                }

                if (data.Length - source < 2)
                {
                    throw new InvalidDataException("The LZ10 stream ends inside a back-reference token.");
                }

                byte first = data[source++];
                byte second = data[source++];
                int length = (first >> 4) + 3;
                int displacement = (((first & 0x0F) << 8) | second) + 1;
                CopyMatch(output, ref destination, length, displacement, "LZ10");
            }
        }

        return output;
    }

    /// <summary>Creates a deterministic greedy LZ10 stream with nearest-match tie breaking.</summary>
    /// <param name="data">Nonempty uncompressed bytes.</param>
    /// <returns>A self-describing LZ10 stream, whether or not it is smaller than the input.</returns>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        byte[] header = NitroCompression.CreateHeader(NitroCompressionType.Lz10, data.Length);
        var output = new List<byte>(header.Length + data.Length);
        output.AddRange(header);
        int position = 0;
        while (position < data.Length)
        {
            int flagsOffset = output.Count;
            output.Add(0);
            byte flags = 0;
            for (int bit = 7; bit >= 0 && position < data.Length; bit--)
            {
                NitroLzMatchFinder.Find(data, position, 18, out int length, out int displacement);
                if (length >= 3)
                {
                    flags |= (byte)(1 << bit);
                    int packedDisplacement = displacement - 1;
                    output.Add((byte)(((length - 3) << 4) | (packedDisplacement >> 8)));
                    output.Add((byte)packedDisplacement);
                    position += length;
                }
                else
                {
                    output.Add(data[position++]);
                }
            }

            output[flagsOffset] = flags;
        }

        return output.ToArray();
    }

    /// <summary>Copies an overlap-capable match while clipping the final token to the declared output size.</summary>
    internal static void CopyMatch(
        Span<byte> output,
        ref int destination,
        int length,
        int displacement,
        string format)
    {
        if (displacement > destination)
        {
            throw new InvalidDataException($"The {format} stream references bytes before the decoded output.");
        }

        for (int index = 0; index < length && destination < output.Length; index++)
        {
            output[destination] = output[destination - displacement];
            destination++;
        }
    }
}
