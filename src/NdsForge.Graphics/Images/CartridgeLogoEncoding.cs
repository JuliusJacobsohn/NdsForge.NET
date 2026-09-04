using System.Buffers.Binary;

namespace NdsForge.Graphics.Images;

/// <summary>Implements bounded fixed-field packing, word differences, and the cartridge prefix-code format.</summary>
internal static class CartridgeLogoEncoding
{
    // Each word contains a leading sentinel one followed by the format's code bits.
    // The sentinel retains leading zeros and makes code length explicit without an embedded tree layout.
    private static ReadOnlySpan<byte> Codes => [0b11, 0b10110, 0b101010, 0b10100,
        0b100010, 0b1011110, 0b1010110, 0b1000110, 0b100110, 0b1011111,
        0b1010111, 0b1000111, 0b10010, 0b101110, 0b100111, 0b10000];

    private static readonly byte[] Symbols = CreateSymbolLookup();

    internal static byte[] Decode(ReadOnlySpan<byte> source, out int usedBits)
    {
        int position = 0;
        Span<byte> differences = stackalloc byte[212];
        for (int i = 0; i < differences.Length; i++)
        {
            byte low = ReadSymbol(source, ref position);
            byte high = ReadSymbol(source, ref position);
            differences[i] = (byte)(low | (high << 4));
            if (i == 3 && BinaryPrimitives.ReadUInt32LittleEndian(differences) != 0x0000D082)
            {
                throw new FormatException("The cartridge logo filter header must describe 208 bytes of 16-bit differences.");
            }
        }

        Span<byte> tiles = stackalloc byte[208];
        ushort previous = 0;
        for (int i = 0; i < tiles.Length; i += 2)
        {
            previous = unchecked((ushort)(previous + BinaryPrimitives.ReadUInt16LittleEndian(differences[(i + 4)..])));
            BinaryPrimitives.WriteUInt16LittleEndian(tiles[i..], previous);
        }
        byte[] pixels = new byte[CartridgeLogo.Width * CartridgeLogo.Height];
        for (int y = 0; y < CartridgeLogo.Height; y++)
        {
            for (int x = 0; x < CartridgeLogo.Width; x++)
            {
                pixels[(y * CartridgeLogo.Width) + x] = (byte)((tiles[TileRowOffset(x, y)] >> (x % 8)) & 1);
            }
        }
        usedBits = position;
        return pixels;
    }

    internal static byte[] Encode(ReadOnlySpan<byte> pixels, out int usedBits)
    {
        byte[] differences = CreateDifferences(pixels);
        usedBits = CountBits(differences);
        if (usedBits > CartridgeLogo.EncodedByteLength * 8)
        {
            throw new InvalidOperationException($"The image needs {usedBits} encoded bits; the cartridge logo field holds at most 1248. No pixels were discarded.");
        }

        byte[] encoded = new byte[CartridgeLogo.EncodedByteLength];
        int position = 0;
        foreach (byte value in differences)
        {
            WriteSymbol(value & 15, encoded, ref position);
            WriteSymbol(value >> 4, encoded, ref position);
        }
        return encoded;
    }

    internal static int Measure(ReadOnlySpan<byte> pixels) => CountBits(CreateDifferences(pixels));

    internal static byte[] PackPixels(ReadOnlySpan<byte> pixels)
    {
        byte[] tiles = new byte[208];
        for (int y = 0; y < CartridgeLogo.Height; y++)
        {
            for (int x = 0; x < CartridgeLogo.Width; x++)
            {
                tiles[TileRowOffset(x, y)] |= (byte)(pixels[(y * CartridgeLogo.Width) + x] << (x % 8));
            }
        }
        return tiles;
    }

    private static byte[] CreateDifferences(ReadOnlySpan<byte> pixels)
    {
        byte[] tiles = PackPixels(pixels);
        byte[] differences = new byte[212];
        BinaryPrimitives.WriteUInt32LittleEndian(differences, 0x0000D082);
        ushort previous = 0;
        for (int i = 0; i < tiles.Length; i += 2)
        {
            ushort current = BinaryPrimitives.ReadUInt16LittleEndian(tiles.AsSpan(i));
            BinaryPrimitives.WriteUInt16LittleEndian(differences.AsSpan(i + 4), unchecked((ushort)(current - previous)));
            previous = current;
        }
        return differences;
    }

    private static int CountBits(ReadOnlySpan<byte> differences)
    {
        int count = 0;
        foreach (byte value in differences) { count += CodeLength(Codes[value & 15]) + CodeLength(Codes[value >> 4]); }
        return count;
    }

    private static int CodeLength(int code) => System.Numerics.BitOperations.Log2((uint)code);

    private static int TileRowOffset(int x, int y) => ((y / 8) * CartridgeLogo.Width) + ((x / 8) * 8) + (y % 8);

    private static int StreamByteOffset(int bit) => ((bit / 32) * 4) + 3 - ((bit % 32) / 8);

    private static byte ReadSymbol(ReadOnlySpan<byte> source, ref int position)
    {
        int code = 1;
        do
        {
            if (position == CartridgeLogo.EncodedByteLength * 8) { throw new FormatException("The cartridge logo stream ends before all pixels are decoded."); }
            int bit = (source[StreamByteOffset(position)] >> (7 - (position % 8))) & 1;
            position++;
            code = (code << 1) | bit;
        } while (Symbols[code] == byte.MaxValue);
        return Symbols[code];
    }

    private static void WriteSymbol(int symbol, Span<byte> destination, ref int position)
    {
        int code = Codes[symbol];
        for (int shift = CodeLength(code) - 1; shift >= 0; shift--)
        {
            destination[StreamByteOffset(position)] |= (byte)(((code >> shift) & 1) << (7 - (position % 8)));
            position++;
        }
    }

    private static byte[] CreateSymbolLookup()
    {
        byte[] symbols = new byte[128];
        Array.Fill(symbols, byte.MaxValue);
        for (byte i = 0; i < Codes.Length; i++) { symbols[Codes[i]] = i; }
        return symbols;
    }
}
