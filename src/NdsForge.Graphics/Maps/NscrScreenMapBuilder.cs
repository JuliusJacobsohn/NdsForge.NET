using System.Buffers.Binary;

namespace NdsForge.Graphics.Maps;

/// <summary>Edits NSCR tile placements with source preservation or deterministic reconstruction.</summary>
public sealed class NscrScreenMapBuilder
{
    private readonly NscrScreenMap _source;
    private readonly NscrMapEntry[] _entries;
    private readonly HashSet<int> _changed = [];

    internal NscrScreenMapBuilder(NscrScreenMap source)
    {
        _source = source;
        _entries = source.Entries.ToArray();
    }

    /// <summary>Replaces one row-major tile placement.</summary>
    /// <param name="x">Zero-based tile-column coordinate.</param>
    /// <param name="y">Zero-based tile-row coordinate.</param>
    /// <param name="entry">Replacement placement.</param>
    /// <returns>This builder.</returns>
    public NscrScreenMapBuilder ReplaceEntry(int x, int y, NscrMapEntry entry)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, _source.TileColumns);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, _source.TileRows);
        ValidateAffineEntry(_source.BackgroundKind, entry);
        int index = (y * _source.TileColumns) + x;
        _entries[index] = entry;
        _changed.Add(index);
        return this;
    }

    /// <summary>Writes an exact preservation result by default or a canonical NSCR.</summary>
    /// <param name="preserveSourceLayout">Retains unknown blocks, padding, and trailing allocation bytes when true.</param>
    /// <returns>Complete NSCR bytes.</returns>
    public byte[] Build(bool preserveSourceLayout = true)
    {
        if (preserveSourceLayout)
        {
            (byte[] source, int mapOffset) = _source.GetPreservationData();
            byte[] result = source.ToArray();
            foreach (int index in _changed)
            {
                if (_source.BackgroundKind == NitroBackgroundKind.Affine)
                {
                    result[mapOffset + index] = (byte)_entries[index].TileIndex;
                }
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        result.AsSpan(mapOffset + (index * sizeof(ushort))),
                        _entries[index].PackedValue);
                }
            }

            return result;
        }

        return WriteCanonical(
            _source.Version,
            _source.Width,
            _source.Height,
            _source.PaletteSelection,
            _source.BackgroundKind,
            _entries);
    }

    internal static byte[] WriteCanonical(
        ushort version,
        int width,
        int height,
        NitroPaletteSelection paletteSelection,
        NitroBackgroundKind backgroundKind,
        IReadOnlyList<NscrMapEntry> entries)
    {
        int expectedCount = checked((width / 8) * (height / 8));
        if (width <= 0 || height <= 0 || (width & 7) != 0 || (height & 7) != 0 || entries.Count != expectedCount)
        {
            throw new ArgumentException("The NSCR entries do not match positive tile-aligned dimensions.", nameof(entries));
        }

        int entryWidth = backgroundKind == NitroBackgroundKind.Affine ? 1 : 2;
        int mapByteLength = checked(entries.Count * entryWidth);
        int blockLength = checked(0x14 + mapByteLength);
        int fileLength = checked(0x10 + blockLength);
        byte[] result = new byte[fileLength];
        "RCSN"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 0xFEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), version);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)fileLength);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), 1);
        "NRCS"u8.CopyTo(result.AsSpan(0x10));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x14), (uint)blockLength);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x18), checked((ushort)width));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x1A), checked((ushort)height));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x1C), (ushort)paletteSelection);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x1E), (ushort)backgroundKind);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x20), mapByteLength);
        int mapOffset = 0x24;
        for (int index = 0; index < entries.Count; index++)
        {
            ValidateAffineEntry(backgroundKind, entries[index]);
            if (backgroundKind == NitroBackgroundKind.Affine)
            {
                result[mapOffset + index] = (byte)entries[index].TileIndex;
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    result.AsSpan(mapOffset + (index * sizeof(ushort))),
                    entries[index].PackedValue);
            }
        }

        return result;
    }

    private static void ValidateAffineEntry(NitroBackgroundKind backgroundKind, NscrMapEntry entry)
    {
        if (backgroundKind == NitroBackgroundKind.Affine &&
            (entry.TileIndex > byte.MaxValue || entry.HorizontalFlip || entry.VerticalFlip || entry.PaletteIndex != 0))
        {
            throw new ArgumentException("Affine NSCR entries can contain only an eight-bit tile number.", nameof(entry));
        }
    }
}
