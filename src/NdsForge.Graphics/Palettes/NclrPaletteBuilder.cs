using System.Buffers.Binary;
using NdsForge.Graphics.Colors;

namespace NdsForge.Graphics.Palettes;

/// <summary>Edits exact NCLR color words while preserving unrelated source bytes whenever requested.</summary>
public sealed class NclrPaletteBuilder
{
    private readonly NclrPalette _source;
    private readonly NitroColor555[] _colors;
    private readonly HashSet<int> _changed = [];

    /// <summary>Copies colors immediately so source and builder lifetimes remain independent.</summary>
    internal NclrPaletteBuilder(NclrPalette source)
    {
        _source = source;
        _colors = source.Colors.ToArray();
    }

    /// <summary>Replaces one exact stored BGR555 word.</summary>
    /// <param name="index">Zero-based serialized color index.</param>
    /// <param name="color">Replacement including preserved high bit.</param>
    /// <returns>This builder for fluent edits.</returns>
    public NclrPaletteBuilder ReplaceColor(int index, NitroColor555 color)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _colors.Length);
        _colors[index] = color;
        _changed.Add(index);
        return this;
    }

    /// <summary>Writes an exact preservation result by default or a canonical standard-file representation.</summary>
    /// <param name="preserveSourceLayout">Retains unknown blocks, padding, and trailing allocation bytes when <see langword="true"/>.</param>
    /// <returns>Complete NCLR bytes.</returns>
    public byte[] Build(bool preserveSourceLayout = true)
    {
        if (preserveSourceLayout)
        {
            (byte[] source, int colorOffset) = _source.GetPreservationData();
            byte[] result = source.ToArray();
            foreach (int index in _changed)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    result.AsSpan(colorOffset + (index * sizeof(ushort))),
                    _colors[index].PackedValue);
            }

            return result;
        }

        return WriteCanonical(
            _source.Version,
            _source.Depth,
            _source.IsExtendedPalette,
            _colors,
            _source.PaletteMapping);
    }

    /// <summary>Produces PLTT and optional PCMP blocks with format-defined offsets and no ambient codec dependency.</summary>
    internal static byte[] WriteCanonical(
        ushort version,
        NitroColorDepth depth,
        bool isExtendedPalette,
        IReadOnlyList<NitroColor555> colors,
        IReadOnlyList<ushort>? paletteMapping)
    {
        if (depth is not (NitroColorDepth.Indexed4Bpp or NitroColorDepth.Indexed8Bpp))
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        int colorByteLength = checked(colors.Count * sizeof(ushort));
        int paletteBlockLength = checked(0x18 + colorByteLength);
        int mappingCount = paletteMapping?.Count ?? 0;
        if (mappingCount > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(paletteMapping));
        }

        int mappingBlockLength = paletteMapping is null ? 0 : checked(0x10 + (mappingCount * sizeof(ushort)));
        int totalLength = checked(0x10 + paletteBlockLength + mappingBlockLength);
        byte[] result = new byte[totalLength];
        "RLCN"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 0xFEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), version);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)totalLength);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), paletteMapping is null ? (ushort)1 : (ushort)2);

        int paletteOffset = 0x10;
        "TTLP"u8.CopyTo(result.AsSpan(paletteOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(paletteOffset + 4), (uint)paletteBlockLength);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(paletteOffset + 8), (uint)depth);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(paletteOffset + 12), isExtendedPalette ? 1U : 0U);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(paletteOffset + 16), (uint)colorByteLength);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(paletteOffset + 20), 0x10);
        for (int index = 0; index < colors.Count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                result.AsSpan(paletteOffset + 0x18 + (index * sizeof(ushort))),
                colors[index].PackedValue);
        }

        if (paletteMapping is not null)
        {
            int mappingOffset = paletteOffset + paletteBlockLength;
            "PMCP"u8.CopyTo(result.AsSpan(mappingOffset));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(mappingOffset + 4), (uint)mappingBlockLength);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(mappingOffset + 8), (ushort)mappingCount);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(mappingOffset + 10), 0xBEEF);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(mappingOffset + 12), 8);
            for (int index = 0; index < mappingCount; index++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    result.AsSpan(mappingOffset + 0x10 + (index * sizeof(ushort))),
                    paletteMapping[index]);
            }
        }

        return result;
    }
}
