using System.Buffers.Binary;

namespace NdsForge.Nitro.Compression;

/// <summary>Expands Nintendo BIOS four- and eight-bit Huffman streams from their serialized decision trees.</summary>
public static class HuffmanCodec
{
    /// <summary>Decodes a bounded serialized tree and little-endian 32-bit decision words.</summary>
    /// <param name="data">Complete Huffman stream beginning with type <c>0x24</c> or <c>0x28</c>.</param>
    /// <param name="maximumDecodedLength">Largest byte output accepted from untrusted metadata.</param>
    /// <returns>Header-declared bytes; four-bit symbols are packed high nibble first.</returns>
    public static byte[] Decompress(
        ReadOnlySpan<byte> data,
        int maximumDecodedLength = NitroCompression.DefaultMaximumDecodedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDecodedLength);
        if (!NitroCompression.TryInspect(data, out NitroCompressionInfo info) ||
            info.Type is not (NitroCompressionType.Huffman4 or NitroCompressionType.Huffman8))
        {
            throw new InvalidDataException("The input is not a four- or eight-bit Nitro Huffman stream.");
        }

        if (info.DecodedLength > maximumDecodedLength)
        {
            throw new InvalidDataException(
                $"The decoded length {info.DecodedLength} exceeds the configured limit {maximumDecodedLength}.");
        }

        if (data.Length <= info.HeaderLength)
        {
            throw new InvalidDataException("The Huffman stream does not contain a serialized tree size.");
        }

        int treeStorageLength = (data[info.HeaderLength] + 1) * 2;
        int treeOffset = info.HeaderLength + 1;
        int treeLength = treeStorageLength - 1;
        int bitstreamOffset = info.HeaderLength + treeStorageLength;
        if (treeLength < 3 || bitstreamOffset > data.Length - 4)
        {
            throw new InvalidDataException("The Huffman tree or first decision word is truncated.");
        }

        ValidateTree(data, treeOffset, bitstreamOffset, info.Type == NitroCompressionType.Huffman4);

        byte[] output = new byte[info.DecodedLength];
        int symbolsRequired = checked(output.Length * (info.Type == NitroCompressionType.Huffman4 ? 2 : 1));
        int symbolsWritten = 0;
        int outputOffset = 0;
        int nodeOffset = treeOffset;
        int source = bitstreamOffset;
        while (symbolsWritten < symbolsRequired)
        {
            if (data.Length - source < sizeof(uint))
            {
                throw new InvalidDataException("The Huffman decision bitstream ended before its declared output.");
            }

            uint decisions = BinaryPrimitives.ReadUInt32LittleEndian(data[source..]);
            source += sizeof(uint);
            for (int bit = 31; bit >= 0 && symbolsWritten < symbolsRequired; bit--)
            {
                byte node = data[nodeOffset];
                int branch = (int)((decisions >> bit) & 1);
                int childPair = (nodeOffset & ~1) + (((node & 0x3F) + 1) * 2);
                int childOffset = childPair + branch;
                if (childOffset < treeOffset || childOffset >= bitstreamOffset)
                {
                    throw new InvalidDataException("The Huffman tree contains an out-of-range child offset.");
                }

                int leafMask = branch == 0 ? 0x80 : 0x40;
                if ((node & leafMask) == 0)
                {
                    nodeOffset = childOffset;
                    continue;
                }

                byte symbol = data[childOffset];
                if (info.Type == NitroCompressionType.Huffman4)
                {
                    if (symbol > 0x0F)
                    {
                        throw new InvalidDataException("A four-bit Huffman leaf exceeds one nibble.");
                    }

                    if ((symbolsWritten & 1) == 0)
                    {
                        output[outputOffset] = (byte)(symbol << 4);
                    }
                    else
                    {
                        output[outputOffset++] |= symbol;
                    }
                }
                else
                {
                    output[outputOffset++] = symbol;
                }

                symbolsWritten++;
                nodeOffset = treeOffset;
            }
        }

        return output;
    }

    private static void ValidateTree(ReadOnlySpan<byte> data, int treeOffset, int treeEnd, bool fourBit)
    {
        Span<bool> decisionNodes = stackalloc bool[512];
        decisionNodes.Clear();
        decisionNodes[0] = true;
        for (int cursor = treeOffset; cursor < treeEnd; cursor++)
        {
            if (!decisionNodes[cursor - treeOffset])
            {
                continue;
            }

            byte node = data[cursor];
            int childPair = (cursor & ~1) + (((node & 0x3F) + 1) * 2);
            if (childPair < treeOffset || childPair >= treeEnd - 1)
            {
                throw new InvalidDataException("The Huffman tree contains an out-of-range child offset.");
            }

            for (int branch = 0; branch < 2; branch++)
            {
                int child = childPair + branch;
                if ((node & (branch == 0 ? 0x80 : 0x40)) == 0)
                {
                    decisionNodes[child - treeOffset] = true;
                }
                else if (fourBit && data[child] > 0x0F)
                {
                    throw new InvalidDataException("A four-bit Huffman leaf exceeds one nibble.");
                }
            }
        }
    }
}
