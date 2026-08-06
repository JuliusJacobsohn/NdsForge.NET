using System.Buffers.Binary;

namespace NdsForge.Corpus;

/// <summary>Generates tiny deterministic ELF and indexed-BMP inputs needed to exercise ndstool conversion branches.</summary>
internal static class OracleNativeAssets
{
    /// <summary>Writes a little-endian ARM ELF32 containing a bounded prefix of an extracted program as one loadable segment.</summary>
    /// <param name="binaryPath">Extracted raw ARM program.</param>
    /// <param name="elfPath">Generated ELF input.</param>
    /// <param name="loadAddress">Runtime base recorded in the program header.</param>
    /// <param name="entryAddress">Runtime entry recorded in the ELF header.</param>
    public static async Task WriteElfAsync(string binaryPath, string elfPath, uint loadAddress, uint entryAddress)
    {
        const int elfHeaderSize = 52;
        const int programHeaderSize = 32;
        const int dataOffset = elfHeaderSize + programHeaderSize;
        int length = checked((int)Math.Min(new FileInfo(binaryPath).Length, 4096));
        var header = new byte[dataOffset];
        header[0] = 0x7F;
        "ELF"u8.CopyTo(header.AsSpan(1));
        header[4] = 1;
        header[5] = 1;
        header[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(18), 40);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), entryAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), elfHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(40), elfHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(42), programHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(44), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(elfHeaderSize), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(elfHeaderSize + 4), dataOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(elfHeaderSize + 8), loadAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(elfHeaderSize + 12), loadAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(elfHeaderSize + 16), checked((uint)length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(elfHeaderSize + 20), checked((uint)length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(elfHeaderSize + 24), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(elfHeaderSize + 28), 4);

        await using var output = new FileStream(elfPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true);
        await output.WriteAsync(header).ConfigureAwait(false);
        await using var input = new FileStream(binaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        await CopyPrefixAsync(input, output, length).ConfigureAwait(false);
    }

    /// <summary>Writes an eight-bit indexed BMP whose indices stay within ndstool banner or monochrome-logo constraints.</summary>
    /// <param name="path">Generated bitmap path.</param>
    /// <param name="width">Pixel width, either 32 for banners or 104 for logos.</param>
    /// <param name="height">Pixel height, either 32 for banners or 16 for logos.</param>
    /// <param name="colors">Palette entries made available to the legacy converter.</param>
    public static async Task WriteIndexedBmpAsync(string path, int width, int height, int colors)
    {
        int stride = (width + 3) & ~3;
        int paletteBytes = colors * 4;
        int pixelOffset = 14 + 40 + paletteBytes;
        int fileSize = checked(pixelOffset + stride * height);
        var data = new byte[fileSize];
        "BM"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), fileSize);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), pixelOffset);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), height);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), 8);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(34), stride * height);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(46), colors);
        for (int index = 0; index < colors; index++)
        {
            byte intensity = checked((byte)(index * 255 / Math.Max(1, colors - 1)));
            data[54 + index * 4] = intensity;
            data[55 + index * 4] = intensity;
            data[56 + index * 4] = intensity;
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                data[pixelOffset + y * stride + x] = checked((byte)((x / 4 + y / 4) % colors));
            }
        }

        await File.WriteAllBytesAsync(path, data).ConfigureAwait(false);
    }

    /// <summary>Copies exactly the selected prefix without allocating it as a second full program buffer.</summary>
    /// <param name="source">Extracted program positioned at its first byte.</param>
    /// <param name="destination">Generated ELF positioned after headers.</param>
    /// <param name="length">Exact prefix byte count.</param>
    private static async Task CopyPrefixAsync(Stream source, Stream destination, int length)
    {
        byte[] buffer = new byte[Math.Min(length, 64 * 1024)];
        int remaining = length;
        while (remaining > 0)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining))).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Extracted ARM program ended before the selected ELF prefix.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            remaining -= read;
        }
    }
}
