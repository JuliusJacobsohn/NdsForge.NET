using System.Buffers.Binary;
using NdsForge.Graphics.Palettes;

namespace NdsForge.Graphics.Tiles;

/// <summary>Edits NCGR indices with exact source-layout preservation or deterministic reconstruction.</summary>
public sealed class NcgrCharacterGraphicsBuilder
{
    private readonly NcgrCharacterGraphics _source;
    private readonly byte[] _pixels;
    private bool _changed;

    internal NcgrCharacterGraphicsBuilder(NcgrCharacterGraphics source)
    {
        _source = source;
        _pixels = source.Pixels.ToArray();
    }

    /// <summary>Replaces one row-major color index.</summary>
    /// <param name="x">Zero-based horizontal coordinate.</param>
    /// <param name="y">Zero-based vertical coordinate.</param>
    /// <param name="colorIndex">New color index within the selected bit depth.</param>
    /// <returns>This builder.</returns>
    public NcgrCharacterGraphicsBuilder ReplacePixel(int x, int y, byte colorIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, _source.Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, _source.Height);
        if (_source.Depth == NitroColorDepth.Indexed4Bpp && colorIndex > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(colorIndex));
        }

        _pixels[(y * _source.Width) + x] = colorIndex;
        _changed = true;
        return this;
    }

    /// <summary>Writes an exact preservation result by default or a canonical NCGR.</summary>
    /// <param name="preserveSourceLayout">Retains unknown blocks, padding, and trailing allocation bytes when true.</param>
    /// <returns>Complete NCGR bytes.</returns>
    public byte[] Build(bool preserveSourceLayout = true)
    {
        byte[] characterData = EncodeCharacterData(
            _pixels,
            _source.Width,
            _source.Height,
            _source.Depth,
            _source.IsTileOrdered);
        if (preserveSourceLayout)
        {
            (byte[] source, int characterOffset) = _source.GetPreservationData();
            byte[] result = source.ToArray();
            if (_changed)
            {
                characterData.CopyTo(result.AsSpan(characterOffset));
            }

            return result;
        }

        return WriteCanonical(
            _source.Version,
            _source.StoredWidthInTiles,
            _source.StoredHeightInTiles,
            _source.Depth,
            _source.Mapping,
            _source.StorageFlags,
            _pixels,
            _source.SourceRegion);
    }

    internal static byte[] WriteCanonical(
        ushort version,
        ushort storedWidth,
        ushort storedHeight,
        NitroColorDepth depth,
        NitroTileMapping mapping,
        uint flags,
        IReadOnlyList<byte> pixels,
        NcgrSourceRegion? sourceRegion)
    {
        int width = storedWidth == ushort.MaxValue ? 8 : checked(storedWidth * 8);
        if (width <= 0 || pixels.Count % width != 0)
        {
            throw new ArgumentException("The pixels do not match the stored NCGR width.", nameof(pixels));
        }

        int height = pixels.Count / width;
        byte[] characterData = EncodeCharacterData(pixels, width, height, depth, (flags & 0xFF) == 0);
        int characterBlockLength = checked(0x20 + characterData.Length);
        int sourceBlockLength = sourceRegion is null ? 0 : 0x10;
        int fileLength = checked(0x10 + characterBlockLength + sourceBlockLength);
        byte[] result = new byte[fileLength];
        "RGCN"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 0xFEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), version);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)fileLength);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), sourceRegion is null ? (ushort)1 : (ushort)2);

        int characterOffset = 0x10;
        "RAHC"u8.CopyTo(result.AsSpan(characterOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(characterOffset + 4), (uint)characterBlockLength);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(characterOffset + 8), storedHeight);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(characterOffset + 10), storedWidth);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(characterOffset + 12), (uint)depth);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(characterOffset + 16), (uint)mapping);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(characterOffset + 20), flags);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(characterOffset + 24), characterData.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(characterOffset + 28), 0x18);
        characterData.CopyTo(result.AsSpan(characterOffset + 0x20));

        if (sourceRegion is NcgrSourceRegion region)
        {
            int sourceOffset = characterOffset + characterBlockLength;
            "SOPC"u8.CopyTo(result.AsSpan(sourceOffset));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sourceOffset + 4), 0x10);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(sourceOffset + 8), region.X);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(sourceOffset + 10), region.Y);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(sourceOffset + 12), region.Width);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(sourceOffset + 14), region.Height);
        }

        return result;
    }

    private static byte[] EncodeCharacterData(
        IReadOnlyList<byte> pixels,
        int width,
        int height,
        NitroColorDepth depth,
        bool tileOrdered)
    {
        if (width <= 0 || height <= 0 || (width & 7) != 0 || (height & 7) != 0 || pixels.Count != checked(width * height))
        {
            throw new ArgumentException("NCGR pixels must form a positive, tile-aligned raster.", nameof(pixels));
        }

        byte[] serialized = tileOrdered ? ToTileOrder(pixels, width, height) : pixels.ToArray();
        if (depth == NitroColorDepth.Indexed8Bpp)
        {
            return serialized;
        }

        if (depth != NitroColorDepth.Indexed4Bpp)
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        byte[] encoded = new byte[serialized.Length / 2];
        for (int index = 0; index < encoded.Length; index++)
        {
            byte low = serialized[index * 2];
            byte high = serialized[(index * 2) + 1];
            if (low > 15 || high > 15)
            {
                throw new ArgumentException("A color index exceeds four bits.", nameof(pixels));
            }

            encoded[index] = (byte)(low | (high << 4));
        }

        return encoded;
    }

    private static byte[] ToTileOrder(IReadOnlyList<byte> source, int width, int height)
    {
        byte[] result = new byte[source.Count];
        int destination = 0;
        for (int tileY = 0; tileY < height; tileY += 8)
        {
            for (int tileX = 0; tileX < width; tileX += 8)
            {
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        result[destination++] = source[((tileY + y) * width) + tileX + x];
                    }
                }
            }
        }

        return result;
    }
}
