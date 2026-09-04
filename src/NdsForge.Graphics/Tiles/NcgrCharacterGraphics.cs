using System.Buffers.Binary;
using NdsForge.Graphics.Palettes;

namespace NdsForge.Graphics.Tiles;

/// <summary>Models standard NCGR character graphics as row-major indexed pixels.</summary>
public sealed class NcgrCharacterGraphics
{
    private readonly byte[] _originalData;
    private readonly byte[] _pixels;

    private NcgrCharacterGraphics(
        ushort version,
        ushort storedWidthInTiles,
        ushort storedHeightInTiles,
        NitroColorDepth depth,
        NitroTileMapping mapping,
        uint storageFlags,
        int width,
        int height,
        byte[] pixels,
        NcgrSourceRegion? sourceRegion,
        byte[] originalData,
        int characterDataOffset)
    {
        Version = version;
        StoredWidthInTiles = storedWidthInTiles;
        StoredHeightInTiles = storedHeightInTiles;
        Depth = depth;
        Mapping = mapping;
        StorageFlags = storageFlags;
        Width = width;
        Height = height;
        _pixels = pixels;
        Pixels = Array.AsReadOnly(_pixels);
        SourceRegion = sourceRegion;
        _originalData = originalData;
        CharacterDataOffset = characterDataOffset;
    }

    /// <summary>Gets the raw standard-file version word.</summary>
    public ushort Version { get; }

    /// <summary>Gets the serialized width in tiles, or <see cref="ushort.MaxValue"/> when another resource supplies dimensions.</summary>
    public ushort StoredWidthInTiles { get; }

    /// <summary>Gets the serialized height in tiles, or <see cref="ushort.MaxValue"/> when another resource supplies dimensions.</summary>
    public ushort StoredHeightInTiles { get; }

    /// <summary>Gets whether the CHAR block leaves both dimensions unspecified.</summary>
    public bool HasUnspecifiedDimensions => StoredWidthInTiles == ushort.MaxValue && StoredHeightInTiles == ushort.MaxValue;

    /// <summary>Gets the interpreted raster width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the interpreted raster height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the indexed color depth.</summary>
    public NitroColorDepth Depth { get; }

    /// <summary>Gets the exact mapping-boundary value.</summary>
    public NitroTileMapping Mapping { get; }

    /// <summary>Gets the exact CHAR storage flags, including unknown bits.</summary>
    public uint StorageFlags { get; }

    /// <summary>Gets whether character data is serialized tile-by-tile instead of as a linear raster.</summary>
    public bool IsTileOrdered => (StorageFlags & 0xFF) == 0;

    /// <summary>Gets whether the CHAR flags request VRAM-transfer handling.</summary>
    public bool UsesVramTransfer => (StorageFlags >> 8) == 1;

    /// <summary>Gets row-major color indices.</summary>
    public IReadOnlyList<byte> Pixels { get; }

    /// <summary>Gets the optional CPOS source rectangle.</summary>
    public NcgrSourceRegion? SourceRegion { get; }

    /// <summary>Parses one bounded, little-endian NCGR standard file.</summary>
    /// <param name="data">Complete NCGR allocation, optionally followed by padding.</param>
    /// <returns>A detached character-graphics model.</returns>
    public static NcgrCharacterGraphics Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x10 || !data[..4].SequenceEqual("RGCN"u8) ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 0xFEFF)
        {
            throw new InvalidDataException("The input does not begin with a supported NCGR header.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        uint rawFileLength = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (rawFileLength < 0x10 || rawFileLength > data.Length || headerLength != 0x10 || blockCount <= 0)
        {
            throw new InvalidDataException("The NCGR length, header size, or block count is invalid.");
        }

        int fileLength = (int)rawFileLength;
        int cursor = headerLength;
        ushort storedWidth = 0;
        ushort storedHeight = 0;
        NitroColorDepth depth = NitroColorDepth.None;
        NitroTileMapping mapping = default;
        uint flags = 0;
        byte[]? pixels = null;
        NcgrSourceRegion? sourceRegion = null;
        int dataOffset = -1;
        for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            ValidateBlock(data, fileLength, cursor, blockIndex, out int blockLength);
            ReadOnlySpan<byte> signature = data.Slice(cursor, 4);
            if (signature.SequenceEqual("RAHC"u8))
            {
                if (pixels is not null || blockLength < 0x20)
                {
                    throw new InvalidDataException("The NCGR contains a repeated or truncated CHAR block.");
                }

                storedHeight = BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 8)..]);
                storedWidth = BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 10)..]);
                uint rawDepth = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 12)..]);
                depth = rawDepth switch
                {
                    3 => NitroColorDepth.Indexed4Bpp,
                    4 => NitroColorDepth.Indexed8Bpp,
                    _ => throw new InvalidDataException($"The NCGR depth {rawDepth} is unsupported."),
                };
                mapping = (NitroTileMapping)BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 16)..]);
                flags = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 20)..]);
                int byteLength = BinaryPrimitives.ReadInt32LittleEndian(data[(cursor + 24)..]);
                uint relativeOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 28)..]);
                if (byteLength < 0 || relativeOffset > blockLength - 8 || byteLength > blockLength - 8 - relativeOffset)
                {
                    throw new InvalidDataException("The NCGR character payload is outside its block.");
                }

                dataOffset = checked(cursor + 8 + (int)relativeOffset);
                byte[] encoded = data.Slice(dataOffset, byteLength).ToArray();
                byte[] serializedPixels = DecodeIndices(encoded, depth);
                (int width, int height) = ResolveDimensions(storedWidth, storedHeight, serializedPixels.Length);
                pixels = IsTileOrderedFlag(flags)
                    ? FromTileOrder(serializedPixels, width, height)
                    : serializedPixels;
            }
            else if (signature.SequenceEqual("SOPC"u8))
            {
                if (sourceRegion is not null || blockLength != 0x10)
                {
                    throw new InvalidDataException("The NCGR contains a repeated or malformed CPOS block.");
                }

                sourceRegion = new(
                    BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 8)..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 10)..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 12)..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 14)..]));
            }

            cursor += blockLength;
        }

        if (cursor != fileLength || pixels is null)
        {
            throw new InvalidDataException("The NCGR blocks do not cover the declared file or no CHAR block exists.");
        }

        (int finalWidth, int finalHeight) = ResolveDimensions(storedWidth, storedHeight, pixels.Length);
        return new(version, storedWidth, storedHeight, depth, mapping, flags, finalWidth, finalHeight,
            pixels, sourceRegion, data.ToArray(), dataOffset);
    }

    /// <summary>Creates canonical character graphics from row-major indices.</summary>
    /// <param name="width">Raster width, which must be tile-aligned.</param>
    /// <param name="height">Raster height, which must be tile-aligned.</param>
    /// <param name="depth">Four- or eight-bit indexed depth.</param>
    /// <param name="pixels">Row-major color indices.</param>
    /// <param name="tileOrdered">Whether to serialize by 8-by-8 tiles.</param>
    /// <param name="mapping">Mapping boundary metadata.</param>
    /// <param name="omitDimensions">Writes 0xFFFF dimensions for NCER/NSCR-driven resources.</param>
    /// <returns>A parsed deterministic NCGR model.</returns>
    public static NcgrCharacterGraphics Create(
        int width,
        int height,
        NitroColorDepth depth,
        IReadOnlyList<byte> pixels,
        bool tileOrdered = true,
        NitroTileMapping mapping = NitroTileMapping.TwoDimensional,
        bool omitDimensions = false)
    {
        ValidatePixels(width, height, depth, pixels);
        ushort storedWidth = omitDimensions ? ushort.MaxValue : checked((ushort)(width / 8));
        ushort storedHeight = omitDimensions ? ushort.MaxValue : checked((ushort)(height / 8));
        byte[] encoded = NcgrCharacterGraphicsBuilder.WriteCanonical(
            0x0100, storedWidth, storedHeight, depth, mapping, tileOrdered ? 0U : 1U,
            pixels, sourceRegion: null);
        return Parse(encoded);
    }

    /// <summary>Gets one pixel from a tile numbered in row-major tile order.</summary>
    /// <param name="tileIndex">Zero-based tile number.</param>
    /// <param name="x">Horizontal coordinate within the tile.</param>
    /// <param name="y">Vertical coordinate within the tile.</param>
    /// <returns>The color index.</returns>
    public byte GetTilePixel(int tileIndex, int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tileIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, 8);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, 8);
        int tileColumns = Width / 8;
        int tileX = tileIndex % tileColumns;
        int tileY = tileIndex / tileColumns;
        if (tileY >= Height / 8)
        {
            throw new ArgumentOutOfRangeException(nameof(tileIndex));
        }

        return _pixels[((tileY * 8 + y) * Width) + (tileX * 8) + x];
    }

    /// <summary>Creates an isolated indexed-pixel replacement builder.</summary>
    /// <returns>A mutable edit plan.</returns>
    public NcgrCharacterGraphicsBuilder CreateBuilder() => new(this);

    internal (byte[] Data, int CharacterOffset) GetPreservationData() => (_originalData, CharacterDataOffset);

    private int CharacterDataOffset { get; }

    private static void ValidateBlock(ReadOnlySpan<byte> data, int fileLength, int cursor, int blockIndex, out int blockLength)
    {
        if (cursor > fileLength - 8)
        {
            throw new InvalidDataException("The NCGR block list is truncated.");
        }

        uint rawLength = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 4)..]);
        if (rawLength < 8 || rawLength > fileLength - cursor)
        {
            throw new InvalidDataException($"NCGR block {blockIndex} has an invalid length {rawLength}.");
        }

        blockLength = (int)rawLength;
    }

    private static byte[] DecodeIndices(ReadOnlySpan<byte> encoded, NitroColorDepth depth)
    {
        if (depth == NitroColorDepth.Indexed8Bpp)
        {
            return encoded.ToArray();
        }

        byte[] result = new byte[checked(encoded.Length * 2)];
        for (int index = 0; index < encoded.Length; index++)
        {
            result[index * 2] = (byte)(encoded[index] & 0x0F);
            result[(index * 2) + 1] = (byte)(encoded[index] >> 4);
        }

        return result;
    }

    private static (int Width, int Height) ResolveDimensions(ushort storedWidth, ushort storedHeight, int pixelCount)
    {
        if ((storedWidth == ushort.MaxValue) != (storedHeight == ushort.MaxValue))
        {
            throw new InvalidDataException("NCGR dimensions must either both be specified or both be 0xFFFF.");
        }

        int width = storedWidth == ushort.MaxValue ? 8 : checked(storedWidth * 8);
        if (width <= 0 || pixelCount % width != 0)
        {
            throw new InvalidDataException("The NCGR payload does not form complete rows.");
        }

        int height = pixelCount / width;
        if (storedHeight != ushort.MaxValue && height != storedHeight * 8)
        {
            throw new InvalidDataException("The NCGR payload does not match its dimensions.");
        }

        if ((width & 7) != 0 || (height & 7) != 0)
        {
            throw new InvalidDataException("The NCGR payload does not form complete tiles.");
        }

        return (width, height);
    }

    private static byte[] FromTileOrder(ReadOnlySpan<byte> source, int width, int height)
    {
        byte[] result = new byte[source.Length];
        int tileColumns = width / 8;
        for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
        {
            int tile = sourceIndex / 64;
            int within = sourceIndex % 64;
            int x = ((tile % tileColumns) * 8) + (within % 8);
            int y = ((tile / tileColumns) * 8) + (within / 8);
            if (y >= height)
            {
                throw new InvalidDataException("The NCGR tile payload exceeds its dimensions.");
            }

            result[(y * width) + x] = source[sourceIndex];
        }

        return result;
    }

    private static bool IsTileOrderedFlag(uint flags) => (flags & 0xFF) == 0;

    private static void ValidatePixels(int width, int height, NitroColorDepth depth, IReadOnlyList<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if ((width & 7) != 0 || (height & 7) != 0)
        {
            throw new ArgumentException("NCGR dimensions must be multiples of eight.");
        }

        if (depth is not (NitroColorDepth.Indexed4Bpp or NitroColorDepth.Indexed8Bpp))
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        if (pixels.Count != checked(width * height))
        {
            throw new ArgumentException("The pixel count does not match the NCGR dimensions.", nameof(pixels));
        }

        int maximum = depth == NitroColorDepth.Indexed4Bpp ? 15 : 255;
        if (pixels.Any(value => value > maximum))
        {
            throw new ArgumentException("A color index exceeds the selected NCGR depth.", nameof(pixels));
        }
    }
}
