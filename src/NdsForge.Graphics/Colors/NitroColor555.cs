namespace NdsForge.Graphics.Colors;

/// <summary>Preserves a Nintendo DS little-endian BGR555 palette word, including its unused high bit.</summary>
public readonly record struct NitroColor555
{
    /// <summary>Creates a color from its exact stored 16-bit value.</summary>
    /// <param name="packedValue">Red in bits 0-4, green in 5-9, blue in 10-14, and preserved bit 15.</param>
    public NitroColor555(ushort packedValue) => PackedValue = packedValue;

    /// <summary>Gets the exact stored word.</summary>
    public ushort PackedValue { get; }

    /// <summary>Expands five-bit channels to full-range eight-bit RGBA.</summary>
    /// <returns>An opaque color; index-based transparency is applied by tile or sprite composition.</returns>
    public RgbaColor32 ToRgba32()
    {
        int red = PackedValue & 0x1F;
        int green = (PackedValue >> 5) & 0x1F;
        int blue = (PackedValue >> 10) & 0x1F;
        return new(Expand(red), Expand(green), Expand(blue));
    }

    /// <summary>Quantizes RGB channels to their nearest five-bit values and optionally sets stored bit 15.</summary>
    /// <param name="color">Input color whose alpha channel is not encoded by BGR555.</param>
    /// <param name="highBit">Value retained in bit 15 for formats that assign it external meaning.</param>
    /// <returns>The packed Nintendo DS color.</returns>
    public static NitroColor555 FromRgba32(RgbaColor32 color, bool highBit = false)
    {
        int red = Quantize(color.Red);
        int green = Quantize(color.Green);
        int blue = Quantize(color.Blue);
        return new((ushort)(red | (green << 5) | (blue << 10) | (highBit ? 0x8000 : 0)));
    }

    /// <summary>Scales the complete five-bit range to the nearest eight-bit channel value.</summary>
    private static byte Expand(int value) => (byte)(((value * 255) + 15) / 31);

    /// <summary>Rounds an eight-bit channel to the nearest representable five-bit value.</summary>
    private static int Quantize(byte value) => ((value * 31) + 127) / 255;
}
