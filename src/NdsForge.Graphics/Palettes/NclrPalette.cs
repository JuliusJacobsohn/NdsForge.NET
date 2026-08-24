using System.Buffers.Binary;
using NdsForge.Graphics.Colors;

namespace NdsForge.Graphics.Palettes;

/// <summary>Models one NCLR PLTT color array plus its optional PCMP target-palette mapping.</summary>
public sealed class NclrPalette
{
    private readonly byte[] _originalData;

    /// <summary>Publishes fully validated colors while retaining a private source copy for preservation edits.</summary>
    private NclrPalette(
        ushort version,
        NitroColorDepth depth,
        bool isExtendedPalette,
        uint declaredColorDataLength,
        IReadOnlyList<NitroColor555> colors,
        IReadOnlyList<ushort>? paletteMapping,
        byte[] originalData,
        int colorDataOffset)
    {
        Version = version;
        Depth = depth;
        IsExtendedPalette = isExtendedPalette;
        DeclaredColorDataLength = declaredColorDataLength;
        Colors = colors;
        PaletteMapping = paletteMapping;
        _originalData = originalData;
        ColorDataOffset = colorDataOffset;
    }

    /// <summary>Gets the raw Nitro standard-file version word.</summary>
    public ushort Version { get; }

    /// <summary>Gets the indexed texture depth associated with the palette.</summary>
    public NitroColorDepth Depth { get; }

    /// <summary>Gets whether the PLTT metadata marks an extended palette.</summary>
    public bool IsExtendedPalette { get; }

    /// <summary>Gets the PLTT-declared byte count, which some producers leave inconsistent with the section.</summary>
    public uint DeclaredColorDataLength { get; }

    /// <summary>Gets exact BGR555 words in serialized order.</summary>
    public IReadOnlyList<NitroColor555> Colors { get; }

    /// <summary>Gets optional PCMP destination palette indices, or <see langword="null"/> when no mapping block exists.</summary>
    public IReadOnlyList<ushort>? PaletteMapping { get; }

    /// <summary>Parses a bounded standard NCLR file and preserves unsupported sections opaquely for no-op writes.</summary>
    /// <param name="data">Complete NCLR bytes, optionally followed by allocation padding.</param>
    /// <returns>A palette detached from caller-owned memory.</returns>
    public static NclrPalette Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x10 || !data[..4].SequenceEqual("RLCN"u8) ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 0xFEFF)
        {
            throw new InvalidDataException("The input does not begin with a supported NCLR header.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        uint rawFileLength = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (rawFileLength < 0x10 || rawFileLength > data.Length || headerLength != 0x10 || blockCount <= 0)
        {
            throw new InvalidDataException("The NCLR length, header size, or block count is invalid.");
        }

        int fileLength = (int)rawFileLength;
        int cursor = headerLength;
        NitroColorDepth? depth = null;
        bool extended = false;
        uint declaredColorLength = 0;
        NitroColor555[]? colors = null;
        ushort[]? mapping = null;
        int colorDataOffset = -1;
        for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            if (cursor > fileLength - 8)
            {
                throw new InvalidDataException("The NCLR block list is truncated.");
            }

            uint rawBlockLength = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 4)..]);
            if (rawBlockLength < 8 || rawBlockLength > fileLength - cursor)
            {
                throw new InvalidDataException($"NCLR block {blockIndex} has an invalid length {rawBlockLength}.");
            }

            int blockLength = (int)rawBlockLength;
            ReadOnlySpan<byte> signature = data.Slice(cursor, 4);
            if (signature.SequenceEqual("TTLP"u8))
            {
                if (colors is not null || blockLength < 0x18)
                {
                    throw new InvalidDataException("The NCLR contains a repeated or truncated PLTT block.");
                }

                uint rawDepth = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 8)..]);
                depth = rawDepth switch
                {
                    3 => NitroColorDepth.Indexed4Bpp,
                    4 => NitroColorDepth.Indexed8Bpp,
                    _ => throw new InvalidDataException($"The NCLR PLTT depth {rawDepth} is unsupported."),
                };
                extended = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 12)..]) != 0;
                declaredColorLength = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 16)..]);
                uint rawDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 20)..]);
                if (rawDataOffset > blockLength - 8)
                {
                    throw new InvalidDataException("The NCLR PLTT color offset lies outside its block.");
                }

                colorDataOffset = checked(cursor + 8 + (int)rawDataOffset);
                int colorByteLength = cursor + blockLength - colorDataOffset;
                if (colorByteLength < 0 || (colorByteLength & 1) != 0)
                {
                    throw new InvalidDataException("The NCLR PLTT color array has an invalid byte length.");
                }

                colors = new NitroColor555[colorByteLength / 2];
                for (int index = 0; index < colors.Length; index++)
                {
                    colors[index] = new(BinaryPrimitives.ReadUInt16LittleEndian(data[(colorDataOffset + (index * 2))..]));
                }
            }
            else if (signature.SequenceEqual("PMCP"u8))
            {
                if (mapping is not null || blockLength < 0x10)
                {
                    throw new InvalidDataException("The NCLR contains a repeated or truncated PCMP block.");
                }

                int count = BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 8)..]);
                uint rawDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 12)..]);
                if (rawDataOffset > blockLength - 8)
                {
                    throw new InvalidDataException("The NCLR PCMP mapping lies outside its block.");
                }

                int mappingOffset = cursor + 8 + (int)rawDataOffset;
                if (count > (cursor + blockLength - mappingOffset) / 2)
                {
                    throw new InvalidDataException("The NCLR PCMP mapping is truncated.");
                }

                mapping = new ushort[count];
                for (int index = 0; index < count; index++)
                {
                    mapping[index] = BinaryPrimitives.ReadUInt16LittleEndian(data[(mappingOffset + (index * 2))..]);
                }
            }

            cursor += blockLength;
        }

        if (cursor != fileLength || colors is null || depth is null)
        {
            throw new InvalidDataException("The NCLR blocks do not cover the declared file or no PLTT block exists.");
        }

        return new(version, depth.Value, extended, declaredColorLength, colors, mapping, data.ToArray(), colorDataOffset);
    }

    /// <summary>Creates a canonical NCLR from exact stored colors and an optional destination mapping.</summary>
    /// <param name="depth">Four- or eight-bit indexed texture mode.</param>
    /// <param name="colors">Exact BGR555 words to serialize.</param>
    /// <param name="isExtendedPalette">Extended-palette metadata flag.</param>
    /// <param name="paletteMapping">Optional PCMP destination indices.</param>
    /// <returns>A parsed deterministic NCLR model.</returns>
    public static NclrPalette Create(
        NitroColorDepth depth,
        IReadOnlyList<NitroColor555> colors,
        bool isExtendedPalette = false,
        IReadOnlyList<ushort>? paletteMapping = null)
    {
        ArgumentNullException.ThrowIfNull(colors);
        byte[] encoded = NclrPaletteBuilder.WriteCanonical(
            version: 0x0100,
            depth,
            isExtendedPalette,
            colors,
            paletteMapping);
        return Parse(encoded);
    }

    /// <summary>Creates an isolated exact-color replacement builder.</summary>
    /// <returns>A mutable edit plan detached from caller buffers.</returns>
    public NclrPaletteBuilder CreateBuilder() => new(this);

    /// <summary>Supplies private preservation state only to the paired builder.</summary>
    internal (byte[] Data, int ColorOffset) GetPreservationData() => (_originalData, ColorDataOffset);

    /// <summary>Gets the absolute source offset of the first color word.</summary>
    private int ColorDataOffset { get; }
}
