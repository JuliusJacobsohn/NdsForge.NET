using NdsForge.Graphics.Palettes;

namespace NdsForge.Graphics.Sprites;

/// <summary>Preserves and interprets one three-word Nintendo DS object-attribute-memory entry.</summary>
public readonly record struct NitroObjectEntry
{
    private static readonly (int Width, int Height)[][] Sizes =
    [
        [(8, 8), (16, 16), (32, 32), (64, 64)],
        [(16, 8), (32, 8), (32, 16), (64, 32)],
        [(8, 16), (8, 32), (16, 32), (32, 64)],
    ];

    /// <summary>Creates an entry from its exact stored words.</summary>
    /// <param name="attribute0">Y, transform, mode, mosaic, depth, and shape word.</param>
    /// <param name="attribute1">X, transform/flip, and size word.</param>
    /// <param name="attribute2">Character name, priority, and palette word.</param>
    public NitroObjectEntry(ushort attribute0, ushort attribute1, ushort attribute2)
    {
        if ((attribute0 >> 14) == 3)
        {
            throw new InvalidDataException("OAM shape value three is prohibited.");
        }

        Attribute0 = attribute0;
        Attribute1 = attribute1;
        Attribute2 = attribute2;
    }

    /// <summary>Gets the exact first word.</summary>
    public ushort Attribute0 { get; }

    /// <summary>Gets the exact second word.</summary>
    public ushort Attribute1 { get; }

    /// <summary>Gets the exact third word.</summary>
    public ushort Attribute2 { get; }

    /// <summary>Gets the signed horizontal coordinate.</summary>
    public int X => SignExtend(Attribute1 & 0x01FF, 9);

    /// <summary>Gets the signed vertical coordinate.</summary>
    public int Y => (sbyte)(Attribute0 & 0x00FF);

    /// <summary>Gets the object width in pixels.</summary>
    public int Width => Sizes[Attribute0 >> 14][Attribute1 >> 14].Width;

    /// <summary>Gets the object height in pixels.</summary>
    public int Height => Sizes[Attribute0 >> 14][Attribute1 >> 14].Height;

    /// <summary>Gets whether affine rotation/scaling is enabled.</summary>
    public bool IsAffine => (Attribute0 & 0x0100) != 0;

    /// <summary>Gets whether an affine object uses a double-size display area.</summary>
    public bool IsDoubleSize => IsAffine && (Attribute0 & 0x0200) != 0;

    /// <summary>Gets whether a non-affine object is disabled.</summary>
    public bool IsDisabled => !IsAffine && (Attribute0 & 0x0200) != 0;

    /// <summary>Gets the rendering mode.</summary>
    public NitroObjectMode Mode => (NitroObjectMode)((Attribute0 >> 10) & 3);

    /// <summary>Gets whether object mosaic is enabled.</summary>
    public bool IsMosaic => (Attribute0 & 0x1000) != 0;

    /// <summary>Gets the indexed color depth.</summary>
    public NitroColorDepth Depth => (Attribute0 & 0x2000) == 0
        ? NitroColorDepth.Indexed4Bpp
        : NitroColorDepth.Indexed8Bpp;

    /// <summary>Gets the affine parameter-group index, or zero for a non-affine entry.</summary>
    public byte AffineGroup => IsAffine ? (byte)((Attribute1 >> 9) & 0x1F) : (byte)0;

    /// <summary>Gets whether a non-affine object is mirrored horizontally.</summary>
    public bool HorizontalFlip => !IsAffine && (Attribute1 & 0x1000) != 0;

    /// <summary>Gets whether a non-affine object is mirrored vertically.</summary>
    public bool VerticalFlip => !IsAffine && (Attribute1 & 0x2000) != 0;

    /// <summary>Gets the raw ten-bit character name.</summary>
    public ushort CharacterName => (ushort)(Attribute2 & 0x03FF);

    /// <summary>Gets the object priority from zero (front) through three (back).</summary>
    public byte Priority => (byte)((Attribute2 >> 10) & 3);

    /// <summary>Gets the four-bit palette selector.</summary>
    public byte PaletteIndex => (byte)(Attribute2 >> 12);

    /// <summary>Creates a non-affine indexed object entry from typed fields.</summary>
    /// <param name="x">Signed X coordinate from -256 through 255.</param>
    /// <param name="y">Signed Y coordinate from -128 through 127.</param>
    /// <param name="width">One hardware-supported width.</param>
    /// <param name="height">One hardware-supported height.</param>
    /// <param name="characterName">Ten-bit character name.</param>
    /// <param name="depth">Four- or eight-bit indexed depth.</param>
    /// <param name="paletteIndex">Four-bit palette selector.</param>
    /// <param name="priority">Priority from zero through three.</param>
    /// <param name="horizontalFlip">Horizontal mirror flag.</param>
    /// <param name="verticalFlip">Vertical mirror flag.</param>
    /// <param name="mode">Object rendering mode.</param>
    /// <param name="mosaic">Mosaic flag.</param>
    /// <returns>The encoded OAM entry.</returns>
    public static NitroObjectEntry Create(
        int x,
        int y,
        int width,
        int height,
        ushort characterName,
        NitroColorDepth depth,
        byte paletteIndex = 0,
        byte priority = 0,
        bool horizontalFlip = false,
        bool verticalFlip = false,
        NitroObjectMode mode = NitroObjectMode.Normal,
        bool mosaic = false)
    {
        if (x is < -256 or > 255) throw new ArgumentOutOfRangeException(nameof(x));
        if (y is < -128 or > 127) throw new ArgumentOutOfRangeException(nameof(y));
        if (characterName > 0x03FF) throw new ArgumentOutOfRangeException(nameof(characterName));
        if (paletteIndex > 15) throw new ArgumentOutOfRangeException(nameof(paletteIndex));
        if (priority > 3) throw new ArgumentOutOfRangeException(nameof(priority));
        if (depth is not (NitroColorDepth.Indexed4Bpp or NitroColorDepth.Indexed8Bpp))
            throw new ArgumentOutOfRangeException(nameof(depth));
        (int shape, int size) = FindSize(width, height);
        ushort a0 = (ushort)((byte)y | ((int)mode << 10) | (mosaic ? 0x1000 : 0) |
            (depth == NitroColorDepth.Indexed8Bpp ? 0x2000 : 0) | (shape << 14));
        ushort a1 = (ushort)((x & 0x01FF) | (horizontalFlip ? 0x1000 : 0) |
            (verticalFlip ? 0x2000 : 0) | (size << 14));
        ushort a2 = (ushort)(characterName | (priority << 10) | (paletteIndex << 12));
        return new(a0, a1, a2);
    }

    private static int SignExtend(int value, int bits)
    {
        int sign = 1 << (bits - 1);
        return (value ^ sign) - sign;
    }

    private static (int Shape, int Size) FindSize(int width, int height)
    {
        for (int shape = 0; shape < 3; shape++)
        {
            for (int size = 0; size < 4; size++)
            {
                if (Sizes[shape][size] == (width, height)) return (shape, size);
            }
        }

        throw new ArgumentException("The dimensions are not a supported OAM shape.", nameof(width));
    }
}
