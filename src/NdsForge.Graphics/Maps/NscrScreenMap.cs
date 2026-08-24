using System.Buffers.Binary;
using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Images;
using NdsForge.Graphics.Palettes;
using NdsForge.Graphics.Tiles;

namespace NdsForge.Graphics.Maps;

/// <summary>Models one standard NSCR background screen map.</summary>
public sealed class NscrScreenMap
{
    private readonly NscrMapEntry[] _entries;
    private readonly byte[] _originalData;

    private NscrScreenMap(
        ushort version,
        int width,
        int height,
        NitroPaletteSelection paletteSelection,
        NitroBackgroundKind backgroundKind,
        int declaredDataLength,
        NscrMapEntry[] entries,
        byte[] originalData,
        int mapDataOffset)
    {
        Version = version;
        Width = width;
        Height = height;
        PaletteSelection = paletteSelection;
        BackgroundKind = backgroundKind;
        DeclaredDataLength = declaredDataLength;
        _entries = entries;
        Entries = Array.AsReadOnly(_entries);
        _originalData = originalData;
        MapDataOffset = mapDataOffset;
    }

    /// <summary>Gets the raw standard-file version word.</summary>
    public ushort Version { get; }

    /// <summary>Gets the screen width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the screen height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the number of tile columns.</summary>
    public int TileColumns => Width / 8;

    /// <summary>Gets the number of tile rows.</summary>
    public int TileRows => Height / 8;

    /// <summary>Gets the palette-selection mode.</summary>
    public NitroPaletteSelection PaletteSelection { get; }

    /// <summary>Gets the background-map representation.</summary>
    public NitroBackgroundKind BackgroundKind { get; }

    /// <summary>Gets the SCRN-declared payload byte count.</summary>
    public int DeclaredDataLength { get; }

    /// <summary>Gets row-major tile placements.</summary>
    public IReadOnlyList<NscrMapEntry> Entries { get; }

    /// <summary>Parses one bounded, little-endian NSCR standard file.</summary>
    /// <param name="data">Complete NSCR allocation, optionally followed by padding.</param>
    /// <returns>A detached screen-map model.</returns>
    public static NscrScreenMap Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x10 || !data[..4].SequenceEqual("RCSN"u8) ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 0xFEFF)
        {
            throw new InvalidDataException("The input does not begin with a supported NSCR header.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        uint rawFileLength = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (rawFileLength < 0x10 || rawFileLength > data.Length || headerLength != 0x10 || blockCount <= 0)
        {
            throw new InvalidDataException("The NSCR length, header size, or block count is invalid.");
        }

        int fileLength = (int)rawFileLength;
        int cursor = headerLength;
        int width = 0;
        int height = 0;
        NitroPaletteSelection selection = default;
        NitroBackgroundKind kind = default;
        int dataLength = 0;
        int dataOffset = -1;
        NscrMapEntry[]? entries = null;
        for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            if (cursor > fileLength - 8)
            {
                throw new InvalidDataException("The NSCR block list is truncated.");
            }

            uint rawBlockLength = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 4)..]);
            if (rawBlockLength < 8 || rawBlockLength > fileLength - cursor)
            {
                throw new InvalidDataException($"NSCR block {blockIndex} has an invalid length {rawBlockLength}.");
            }

            int blockLength = (int)rawBlockLength;
            if (data.Slice(cursor, 4).SequenceEqual("NRCS"u8))
            {
                if (entries is not null || blockLength < 0x14)
                {
                    throw new InvalidDataException("The NSCR contains a repeated or truncated SCRN block.");
                }

                width = BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 8)..]);
                height = BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 10)..]);
                selection = (NitroPaletteSelection)BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 12)..]);
                kind = (NitroBackgroundKind)BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 14)..]);
                dataLength = BinaryPrimitives.ReadInt32LittleEndian(data[(cursor + 16)..]);
                dataOffset = cursor + 0x14;
                if (dataLength < 0 || dataLength > blockLength - 0x14)
                {
                    throw new InvalidDataException("The NSCR map payload lies outside its block.");
                }

                ValidateMetadata(width, height, selection, kind);
                int entryWidth = kind == NitroBackgroundKind.Affine ? 1 : 2;
                int expectedCount = checked((width / 8) * (height / 8));
                if (dataLength != checked(expectedCount * entryWidth))
                {
                    throw new InvalidDataException("The NSCR payload does not match its dimensions and background kind.");
                }

                entries = new NscrMapEntry[expectedCount];
                for (int index = 0; index < entries.Length; index++)
                {
                    entries[index] = kind == NitroBackgroundKind.Affine
                        ? new NscrMapEntry(data[dataOffset + index])
                        : NscrMapEntry.FromPackedValue(BinaryPrimitives.ReadUInt16LittleEndian(data[(dataOffset + (index * 2))..]));
                }
            }

            cursor += blockLength;
        }

        if (cursor != fileLength || entries is null)
        {
            throw new InvalidDataException("The NSCR blocks do not cover the declared file or no SCRN block exists.");
        }

        return new(version, width, height, selection, kind, dataLength, entries, data.ToArray(), dataOffset);
    }

    /// <summary>Creates a canonical NSCR from row-major tile placements.</summary>
    /// <param name="width">Screen width in pixels and a multiple of eight.</param>
    /// <param name="height">Screen height in pixels and a multiple of eight.</param>
    /// <param name="paletteSelection">Palette-selection mode.</param>
    /// <param name="backgroundKind">Text, affine, or extended entry representation.</param>
    /// <param name="entries">Exactly one placement per screen tile.</param>
    /// <returns>A parsed deterministic NSCR model.</returns>
    public static NscrScreenMap Create(
        int width,
        int height,
        NitroPaletteSelection paletteSelection,
        NitroBackgroundKind backgroundKind,
        IReadOnlyList<NscrMapEntry> entries)
    {
        ValidateMetadata(width, height, paletteSelection, backgroundKind);
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count != checked((width / 8) * (height / 8)))
        {
            throw new ArgumentException("The entry count does not match the NSCR dimensions.", nameof(entries));
        }

        byte[] encoded = NscrScreenMapBuilder.WriteCanonical(
            0x0100, width, height, paletteSelection, backgroundKind, entries);
        return Parse(encoded);
    }

    /// <summary>Composes this map with character graphics and a palette into a dependency-free RGBA raster.</summary>
    /// <param name="characters">Source 8-by-8 indexed tiles.</param>
    /// <param name="palette">Source BGR555 palette collection.</param>
    /// <param name="transparentIndexZero">Makes local color index zero transparent when true.</param>
    /// <returns>The rendered screen.</returns>
    public RgbaImage32 Render(
        NcgrCharacterGraphics characters,
        NclrPalette palette,
        bool transparentIndexZero = true)
    {
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(palette);
        var output = new RgbaColor32[checked(Width * Height)];
        for (int entryIndex = 0; entryIndex < _entries.Length; entryIndex++)
        {
            NscrMapEntry entry = _entries[entryIndex];
            int tileX = entryIndex % TileColumns;
            int tileY = entryIndex / TileColumns;
            for (int y = 0; y < 8; y++)
            {
                int sourceY = entry.VerticalFlip ? 7 - y : y;
                for (int x = 0; x < 8; x++)
                {
                    int sourceX = entry.HorizontalFlip ? 7 - x : x;
                    byte localIndex = characters.GetTilePixel(entry.TileIndex, sourceX, sourceY);
                    int paletteIndex = ResolvePaletteIndex(localIndex, entry.PaletteIndex);
                    if ((uint)paletteIndex >= (uint)palette.Colors.Count)
                    {
                        throw new InvalidDataException($"NSCR entry {entryIndex} selects missing palette color {paletteIndex}.");
                    }

                    RgbaColor32 color = palette.Colors[paletteIndex].ToRgba32();
                    if (transparentIndexZero && localIndex == 0)
                    {
                        color = new(color.Red, color.Green, color.Blue, 0);
                    }

                    output[((tileY * 8 + y) * Width) + (tileX * 8) + x] = color;
                }
            }
        }

        return new(Width, Height, output);
    }

    /// <summary>Creates an isolated tile-placement replacement builder.</summary>
    /// <returns>A mutable edit plan.</returns>
    public NscrScreenMapBuilder CreateBuilder() => new(this);

    internal (byte[] Data, int MapOffset) GetPreservationData() => (_originalData, MapDataOffset);

    private int MapDataOffset { get; }

    private int ResolvePaletteIndex(byte colorIndex, byte paletteIndex) => PaletteSelection switch
    {
        NitroPaletteSelection.SixteenBySixteen => checked((paletteIndex * 16) + colorIndex),
        NitroPaletteSelection.Single256 => colorIndex,
        NitroPaletteSelection.Extended256 => checked((paletteIndex * 256) + colorIndex),
        _ => throw new InvalidDataException($"Unsupported NSCR palette selection {(ushort)PaletteSelection}."),
    };

    private static void ValidateMetadata(
        int width,
        int height,
        NitroPaletteSelection paletteSelection,
        NitroBackgroundKind backgroundKind)
    {
        if (width <= 0 || height <= 0 || width > ushort.MaxValue || height > ushort.MaxValue ||
            (width & 7) != 0 || (height & 7) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "NSCR dimensions must be positive, 16-bit, and tile-aligned.");
        }

        if (paletteSelection is not (NitroPaletteSelection.SixteenBySixteen or
            NitroPaletteSelection.Single256 or NitroPaletteSelection.Extended256))
        {
            throw new ArgumentOutOfRangeException(nameof(paletteSelection));
        }

        if (backgroundKind is not (NitroBackgroundKind.Text or
            NitroBackgroundKind.Affine or NitroBackgroundKind.Extended))
        {
            throw new ArgumentOutOfRangeException(nameof(backgroundKind));
        }
    }
}
