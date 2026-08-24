namespace NdsForge.Graphics.Maps;

/// <summary>Describes one NSCR tile placement.</summary>
public readonly record struct NscrMapEntry
{
    /// <summary>Creates one validated standard or extended map entry.</summary>
    /// <param name="tileIndex">Tile number from 0 through 1023.</param>
    /// <param name="horizontalFlip">Whether to mirror the tile horizontally.</param>
    /// <param name="verticalFlip">Whether to mirror the tile vertically.</param>
    /// <param name="paletteIndex">Palette selector from 0 through 15.</param>
    public NscrMapEntry(ushort tileIndex, bool horizontalFlip = false, bool verticalFlip = false, byte paletteIndex = 0)
    {
        if (tileIndex > 0x03FF)
        {
            throw new ArgumentOutOfRangeException(nameof(tileIndex));
        }

        if (paletteIndex > 0x0F)
        {
            throw new ArgumentOutOfRangeException(nameof(paletteIndex));
        }

        TileIndex = tileIndex;
        HorizontalFlip = horizontalFlip;
        VerticalFlip = verticalFlip;
        PaletteIndex = paletteIndex;
    }

    /// <summary>Gets the source tile number.</summary>
    public ushort TileIndex { get; }

    /// <summary>Gets whether the tile is mirrored horizontally.</summary>
    public bool HorizontalFlip { get; }

    /// <summary>Gets whether the tile is mirrored vertically.</summary>
    public bool VerticalFlip { get; }

    /// <summary>Gets the palette selector.</summary>
    public byte PaletteIndex { get; }

    /// <summary>Gets the packed 16-bit text/extended representation.</summary>
    public ushort PackedValue => (ushort)(TileIndex |
        (HorizontalFlip ? 0x0400 : 0) |
        (VerticalFlip ? 0x0800 : 0) |
        (PaletteIndex << 12));

    /// <summary>Decodes one packed 16-bit text/extended representation.</summary>
    /// <param name="value">Stored NSCR entry.</param>
    /// <returns>The decoded placement.</returns>
    public static NscrMapEntry FromPackedValue(ushort value) => new(
        (ushort)(value & 0x03FF),
        (value & 0x0400) != 0,
        (value & 0x0800) != 0,
        (byte)(value >> 12));
}
