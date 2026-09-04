namespace NdsForge.Nitro.Compression;

/// <summary>Implements variable-length LZSS streams identified by type byte <c>0x11</c>.</summary>
public static class Lz11Codec
{
    /// <summary>Expands short, medium, and long back-reference tokens with strict source and window bounds.</summary>
    /// <param name="data">Complete LZ11 stream including its common header.</param>
    /// <param name="maximumDecodedLength">Largest output accepted from untrusted metadata.</param>
    /// <returns>Exactly the decoded bytes declared by the header.</returns>
    public static byte[] Decompress(
        ReadOnlySpan<byte> data,
        int maximumDecodedLength = NitroCompression.DefaultMaximumDecodedLength)
    {
        NitroCompressionInfo info = NitroCompression.ReadHeader(
            data,
            NitroCompressionType.Lz11,
            maximumDecodedLength);
        byte[] output = new byte[info.DecodedLength];
        int source = info.HeaderLength;
        int destination = 0;
        while (destination < output.Length)
        {
            if (source >= data.Length)
            {
                throw new InvalidDataException("The LZ11 stream ended before its declared output was produced.");
            }

            byte flags = data[source++];
            for (int mask = 0x80; mask != 0 && destination < output.Length; mask >>= 1)
            {
                if ((flags & mask) == 0)
                {
                    if (source >= data.Length)
                    {
                        throw new InvalidDataException("The LZ11 stream ends inside a literal token.");
                    }

                    output[destination++] = data[source++];
                    continue;
                }

                ReadMatch(data, ref source, out int length, out int displacement);
                Lz10Codec.CopyMatch(output, ref destination, length, displacement, "LZ11");
            }
        }

        return output;
    }

    /// <summary>Creates deterministic nearest-match LZ11 tokens, including long runs up to 65,808 bytes.</summary>
    /// <param name="data">Nonempty uncompressed bytes.</param>
    /// <returns>A self-describing LZ11 stream.</returns>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        byte[] header = NitroCompression.CreateHeader(NitroCompressionType.Lz11, data.Length);
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
                NitroLzMatchFinder.Find(data, position, 0x10110, out int length, out int displacement);
                if (length >= 3)
                {
                    flags |= (byte)(1 << bit);
                    WriteMatch(output, length, displacement);
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

    /// <summary>Reads one variable-width token after the flag bit selects a match.</summary>
    private static void ReadMatch(
        ReadOnlySpan<byte> data,
        ref int source,
        out int length,
        out int displacement)
    {
        if (source >= data.Length)
        {
            throw new InvalidDataException("The LZ11 stream ends before a back-reference token.");
        }

        byte first = data[source++];
        int indicator = first >> 4;
        if (indicator == 0)
        {
            if (data.Length - source < 2)
            {
                throw new InvalidDataException("The LZ11 stream ends inside a medium back-reference.");
            }

            byte second = data[source++];
            byte third = data[source++];
            length = ((first & 0x0F) << 4) + (second >> 4) + 0x11;
            displacement = (((second & 0x0F) << 8) | third) + 1;
        }
        else if (indicator == 1)
        {
            if (data.Length - source < 3)
            {
                throw new InvalidDataException("The LZ11 stream ends inside a long back-reference.");
            }

            byte second = data[source++];
            byte third = data[source++];
            byte fourth = data[source++];
            length = ((first & 0x0F) << 12) + (second << 4) + (third >> 4) + 0x111;
            displacement = (((third & 0x0F) << 8) | fourth) + 1;
        }
        else
        {
            if (source >= data.Length)
            {
                throw new InvalidDataException("The LZ11 stream ends inside a short back-reference.");
            }

            byte second = data[source++];
            length = indicator + 1;
            displacement = (((first & 0x0F) << 8) | second) + 1;
        }
    }

    /// <summary>Chooses the shortest token width able to represent one match.</summary>
    private static void WriteMatch(List<byte> output, int length, int displacement)
    {
        int packedDisplacement = displacement - 1;
        if (length <= 0x10)
        {
            output.Add((byte)(((length - 1) << 4) | (packedDisplacement >> 8)));
            output.Add((byte)packedDisplacement);
        }
        else if (length <= 0x110)
        {
            int packedLength = length - 0x11;
            output.Add((byte)(packedLength >> 4));
            output.Add((byte)((packedLength << 4) | (packedDisplacement >> 8)));
            output.Add((byte)packedDisplacement);
        }
        else
        {
            int packedLength = length - 0x111;
            output.Add((byte)(0x10 | (packedLength >> 12)));
            output.Add((byte)(packedLength >> 4));
            output.Add((byte)((packedLength << 4) | (packedDisplacement >> 8)));
            output.Add((byte)packedDisplacement);
        }
    }
}
