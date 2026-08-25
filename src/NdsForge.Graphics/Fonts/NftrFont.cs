using System.Buffers.Binary;

namespace NdsForge.Graphics.Fonts;

/// <summary>Models a bounded NFTR bitmap font with indexed glyphs, metrics, and character maps.</summary>
public sealed class NftrFont
{
    private readonly byte[] _originalData;
    private readonly int[] _glyphOffsets;
    private readonly int[] _metricOffsets;

    private NftrFont(ushort version, byte fontType, byte lineHeight, ushort fallbackGlyphIndex,
        NftrGlyphMetrics defaultMetrics, NftrTextEncoding encoding, NftrExtendedMetrics? extendedMetrics,
        byte cellWidth, byte cellHeight,
        ushort glyphDataLength, ushort glyphFlags, byte bitsPerPixel, byte rotationFlags,
        IReadOnlyList<NftrGlyph> glyphs, IReadOnlyList<NftrCharacterMap> characterMaps,
        byte[] originalData, int[] glyphOffsets, int[] metricOffsets)
    {
        Version = version;
        FontType = fontType;
        LineHeight = lineHeight;
        FallbackGlyphIndex = fallbackGlyphIndex;
        DefaultMetrics = defaultMetrics;
        Encoding = encoding;
        ExtendedMetrics = extendedMetrics;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        GlyphDataLength = glyphDataLength;
        GlyphFlags = glyphFlags;
        BitsPerPixel = bitsPerPixel;
        RotationFlags = rotationFlags;
        Glyphs = Array.AsReadOnly(glyphs.ToArray());
        CharacterMaps = Array.AsReadOnly(characterMaps.ToArray());
        _originalData = originalData;
        _glyphOffsets = glyphOffsets;
        _metricOffsets = metricOffsets;
    }

    /// <summary>Gets the raw standard-file version.</summary>
    public ushort Version { get; }
    /// <summary>Gets the FINF font-type byte.</summary>
    public byte FontType { get; }
    /// <summary>Gets the nominal line advance in pixels.</summary>
    public byte LineHeight { get; }
    /// <summary>Gets the glyph used when no character mapping exists.</summary>
    public ushort FallbackGlyphIndex { get; }
    /// <summary>Gets default horizontal glyph metrics.</summary>
    public NftrGlyphMetrics DefaultMetrics { get; }
    /// <summary>Gets the declared character-code convention.</summary>
    public NftrTextEncoding Encoding { get; }
    /// <summary>Gets optional extended FINF font bounds.</summary>
    public NftrExtendedMetrics? ExtendedMetrics { get; }
    /// <summary>Gets the stored glyph-cell width.</summary>
    public byte CellWidth { get; }
    /// <summary>Gets the stored glyph-cell height.</summary>
    public byte CellHeight { get; }
    /// <summary>Gets the byte length allocated to each encoded glyph.</summary>
    public ushort GlyphDataLength { get; }
    /// <summary>Gets the exact CGLP flags word.</summary>
    public ushort GlyphFlags { get; }
    /// <summary>Gets the number of bits per indexed pixel.</summary>
    public byte BitsPerPixel { get; }
    /// <summary>Gets the exact CGLP rotation and storage flags.</summary>
    public byte RotationFlags { get; }
    /// <summary>Gets glyphs in numeric index order.</summary>
    public IReadOnlyList<NftrGlyph> Glyphs { get; }
    /// <summary>Gets linked character-map blocks in traversal order.</summary>
    public IReadOnlyList<NftrCharacterMap> CharacterMaps { get; }

    /// <summary>Parses one bounded little-endian NFTR standard file.</summary>
    /// <param name="data">Complete NFTR allocation, optionally followed by padding.</param>
    /// <returns>A detached font model.</returns>
    public static NftrFont Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x10 || !data[..4].SequenceEqual("RTFN"u8) ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 0xFEFF)
            throw new InvalidDataException("The input does not begin with a supported NFTR header.");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        uint rawLength = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (rawLength < 0x10 || rawLength > data.Length || headerLength != 0x10 || blockCount < 4)
            throw new InvalidDataException("The NFTR length, header size, or block count is invalid.");
        int fileLength = (int)rawLength;
        Dictionary<int, int> blocks = ReadBlocks(data, fileLength, blockCount);
        int finf = FindBlock(data, blocks, "FNIF"u8);
        int finfLength = blocks[finf];
        if (finfLength is not (0x1C or 0x20)) throw new InvalidDataException("The NFTR FINF layout is unsupported.");
        int body = finf + 8;
        byte fontType = data[body];
        byte lineHeight = data[body + 1];
        ushort fallback = BinaryPrimitives.ReadUInt16LittleEndian(data[(body + 2)..]);
        var defaultMetrics = new NftrGlyphMetrics((sbyte)data[body + 4], data[body + 5], data[body + 6]);
        byte rawEncoding = data[body + 7];
        if (rawEncoding > 3) throw new InvalidDataException("The NFTR text encoding is unsupported.");
        NftrExtendedMetrics? extendedMetrics = finfLength == 0x20
            ? new(data[body + 20], data[body + 21], (sbyte)data[body + 22], (sbyte)data[body + 23])
            : null;
        int cglp = ResolvePointer(data, blocks, body + 8, "PLGC"u8);
        int cwdh = ResolvePointer(data, blocks, body + 12, "HDWC"u8);
        int firstCmap = ResolvePointer(data, blocks, body + 16, "PAMC"u8);

        if (blocks[cglp] < 0x10) throw new InvalidDataException("The NFTR CGLP section is truncated.");
        byte width = data[cglp + 8];
        byte height = data[cglp + 9];
        ushort glyphLength = BinaryPrimitives.ReadUInt16LittleEndian(data[(cglp + 10)..]);
        ushort glyphFlags = BinaryPrimitives.ReadUInt16LittleEndian(data[(cglp + 12)..]);
        byte depth = data[cglp + 14];
        byte rotation = data[cglp + 15];
        int glyphPayloadLength = blocks[cglp] - 0x10;
        int glyphPaddingLength = glyphPayloadLength % glyphLength;
        if (width == 0 || height == 0 || glyphLength == 0 || depth is 0 or > 8 ||
            (long)width * height * depth > glyphLength * 8L || glyphPaddingLength > 3)
            throw new InvalidDataException("The NFTR CGLP glyph geometry is invalid.");
        int glyphCount = glyphPayloadLength / glyphLength;
        if (glyphCount == 0) throw new InvalidDataException("The NFTR contains no glyphs.");
        var pixels = new byte[glyphCount][];
        var glyphOffsets = new int[glyphCount];
        for (int index = 0; index < glyphCount; index++)
        {
            int offset = cglp + 0x10 + (index * glyphLength);
            glyphOffsets[index] = offset;
            pixels[index] = DecodePixels(data.Slice(offset, glyphLength), width * height, depth);
        }

        (NftrGlyphMetrics[] metrics, int[] metricOffsets) = ReadMetrics(data, blocks, cwdh, glyphCount, defaultMetrics);
        var glyphs = new NftrGlyph[glyphCount];
        for (int index = 0; index < glyphCount; index++) glyphs[index] = new(index, pixels[index], metrics[index]);
        IReadOnlyList<NftrCharacterMap> maps = ReadMaps(data, blocks, firstCmap, glyphCount);
        return new(version, fontType, lineHeight, fallback, defaultMetrics, (NftrTextEncoding)rawEncoding, extendedMetrics,
            width, height, glyphLength, glyphFlags, depth, rotation, glyphs, maps,
            data.ToArray(), glyphOffsets, metricOffsets);
    }

    /// <summary>Tries to resolve a stored character code to a glyph index.</summary>
    /// <param name="characterCode">Character code in the font's declared encoding.</param>
    /// <param name="glyphIndex">Receives the mapped zero-based glyph index on success.</param>
    /// <returns><see langword="true"/> when a mapping exists; otherwise <see langword="false"/>.</returns>
    public bool TryGetGlyphIndex(ushort characterCode, out ushort glyphIndex)
    {
        foreach (NftrCharacterMap map in CharacterMaps)
        {
            foreach (NftrCharacterMapping mapping in map.Mappings)
            {
                if (mapping.CharacterCode != characterCode) continue;
                glyphIndex = mapping.GlyphIndex;
                return true;
            }
        }
        glyphIndex = default;
        return false;
    }

    /// <summary>Creates an isolated glyph and metric edit plan.</summary>
    /// <returns>A builder initialized from this font.</returns>
    public NftrFontBuilder CreateBuilder() => new(this);

    internal (byte[] Data, int[] GlyphOffsets, int[] MetricOffsets) GetPreservationData() =>
        (_originalData, _glyphOffsets, _metricOffsets);

    private static Dictionary<int, int> ReadBlocks(ReadOnlySpan<byte> data, int fileLength, int count)
    {
        var result = new Dictionary<int, int>();
        int cursor = 0x10;
        for (int index = 0; index < count; index++)
        {
            if (cursor > fileLength - 8) throw new InvalidDataException("The NFTR block list is truncated.");
            uint raw = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 4)..]);
            if (raw < 8 || raw > fileLength - cursor) throw new InvalidDataException("An NFTR block length is invalid.");
            result.Add(cursor, (int)raw);
            cursor += (int)raw;
        }
        if (cursor != fileLength) throw new InvalidDataException("NFTR blocks do not cover the declared file.");
        return result;
    }

    private static int FindBlock(ReadOnlySpan<byte> data, Dictionary<int, int> blocks, ReadOnlySpan<byte> magic)
    {
        int result = -1;
        foreach (int offset in blocks.Keys)
        {
            if (!data.Slice(offset, 4).SequenceEqual(magic)) continue;
            if (result >= 0) throw new InvalidDataException("The NFTR repeats a singleton block.");
            result = offset;
        }
        return result >= 0 ? result : throw new InvalidDataException("The NFTR omits a required block.");
    }

    private static int ResolvePointer(ReadOnlySpan<byte> data, Dictionary<int, int> blocks,
        int pointerOffset, ReadOnlySpan<byte> magic)
    {
        uint raw = BinaryPrimitives.ReadUInt32LittleEndian(data[pointerOffset..]);
        if (raw < 8 || raw - 8 > int.MaxValue) throw new InvalidDataException("An NFTR block pointer is invalid.");
        int offset = (int)raw - 8;
        if (!blocks.ContainsKey(offset) || !data.Slice(offset, 4).SequenceEqual(magic))
            throw new InvalidDataException("An NFTR block pointer has the wrong target.");
        return offset;
    }

    private static byte[] DecodePixels(ReadOnlySpan<byte> encoded, int pixelCount, int depth)
    {
        var result = new byte[pixelCount];
        int bit = 0;
        for (int pixel = 0; pixel < pixelCount; pixel++)
        {
            int value = 0;
            for (int plane = 0; plane < depth; plane++, bit++)
                value = (value << 1) | ((encoded[bit >> 3] >> (7 - (bit & 7))) & 1);
            result[pixel] = (byte)value;
        }
        return result;
    }

    private static (NftrGlyphMetrics[] Metrics, int[] Offsets) ReadMetrics(ReadOnlySpan<byte> data,
        Dictionary<int, int> blocks, int first, int glyphCount, NftrGlyphMetrics fallback)
    {
        NftrGlyphMetrics[] metrics = Enumerable.Repeat(fallback, glyphCount).ToArray();
        int[] offsets = Enumerable.Repeat(-1, glyphCount).ToArray();
        var visited = new HashSet<int>();
        int current = first;
        while (current >= 0)
        {
            if (!visited.Add(current) || blocks[current] < 0x10)
                throw new InvalidDataException("The NFTR CWDH chain is invalid.");
            int firstGlyph = BinaryPrimitives.ReadUInt16LittleEndian(data[(current + 8)..]);
            int lastGlyph = BinaryPrimitives.ReadUInt16LittleEndian(data[(current + 10)..]);
            if (lastGlyph < firstGlyph || lastGlyph >= glyphCount || lastGlyph - firstGlyph + 1 > (blocks[current] - 0x10) / 3)
                throw new InvalidDataException("An NFTR CWDH range is invalid.");
            for (int glyph = firstGlyph; glyph <= lastGlyph; glyph++)
            {
                if (offsets[glyph] >= 0)
                    throw new InvalidDataException("The NFTR repeats a glyph metric record.");
                int offset = current + 0x10 + ((glyph - firstGlyph) * 3);
                metrics[glyph] = new((sbyte)data[offset], data[offset + 1], data[offset + 2]);
                offsets[glyph] = offset;
            }
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(data[(current + 12)..]);
            if (next == 0) break;
            if (next < 8 || next - 8 > int.MaxValue || !blocks.ContainsKey((int)next - 8) ||
                !data.Slice((int)next - 8, 4).SequenceEqual("HDWC"u8))
                throw new InvalidDataException("An NFTR CWDH link is invalid.");
            current = (int)next - 8;
        }
        return (metrics, offsets);
    }

    private static NftrCharacterMap[] ReadMaps(ReadOnlySpan<byte> data,
        Dictionary<int, int> blocks, int first, int glyphCount)
    {
        var result = new List<NftrCharacterMap>();
        var visited = new HashSet<int>();
        int current = first;
        while (current >= 0)
        {
            if (!visited.Add(current) || blocks[current] < 0x14)
                throw new InvalidDataException("The NFTR CMAP chain is invalid.");
            ushort firstCharacter = BinaryPrimitives.ReadUInt16LittleEndian(data[(current + 8)..]);
            ushort lastCharacter = BinaryPrimitives.ReadUInt16LittleEndian(data[(current + 10)..]);
            uint rawMethod = BinaryPrimitives.ReadUInt32LittleEndian(data[(current + 12)..]);
            if (lastCharacter < firstCharacter || rawMethod > 2)
                throw new InvalidDataException("An NFTR CMAP header is invalid.");
            var mappings = new List<NftrCharacterMapping>();
            int range = lastCharacter - firstCharacter + 1;
            if (rawMethod == 0)
            {
                if (blocks[current] < 0x16) throw new InvalidDataException("An NFTR direct CMAP is truncated.");
                ushort firstGlyph = BinaryPrimitives.ReadUInt16LittleEndian(data[(current + 20)..]);
                for (int index = 0; index < range; index++) AddMapping(mappings, firstCharacter + index, firstGlyph + index, glyphCount);
            }
            else if (rawMethod == 1)
            {
                if (range > (blocks[current] - 0x14) / 2) throw new InvalidDataException("An NFTR table CMAP is truncated.");
                for (int index = 0; index < range; index++)
                {
                    ushort glyph = BinaryPrimitives.ReadUInt16LittleEndian(data[(current + 20 + (index * 2))..]);
                    if (glyph != ushort.MaxValue) AddMapping(mappings, firstCharacter + index, glyph, glyphCount);
                }
            }
            else
            {
                if (blocks[current] < 0x16) throw new InvalidDataException("An NFTR scan CMAP is truncated.");
                int count = BinaryPrimitives.ReadUInt16LittleEndian(data[(current + 20)..]);
                if (count > (blocks[current] - 0x16) / 4) throw new InvalidDataException("An NFTR scan CMAP is truncated.");
                for (int index = 0; index < count; index++)
                {
                    int pair = current + 22 + (index * 4);
                    ushort character = BinaryPrimitives.ReadUInt16LittleEndian(data[pair..]);
                    ushort glyph = BinaryPrimitives.ReadUInt16LittleEndian(data[(pair + 2)..]);
                    if (character < firstCharacter || character > lastCharacter)
                        throw new InvalidDataException("An NFTR scan mapping lies outside its range.");
                    AddMapping(mappings, character, glyph, glyphCount);
                }
            }
            result.Add(new(firstCharacter, lastCharacter, (NftrCharacterMapMethod)rawMethod, mappings));
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(data[(current + 16)..]);
            if (next == 0) break;
            if (next < 8 || next - 8 > int.MaxValue || !blocks.ContainsKey((int)next - 8) ||
                !data.Slice((int)next - 8, 4).SequenceEqual("PAMC"u8))
                throw new InvalidDataException("An NFTR CMAP link is invalid.");
            current = (int)next - 8;
        }
        return result.ToArray();
    }

    private static void AddMapping(List<NftrCharacterMapping> mappings, int character, int glyph, int glyphCount)
    {
        if (glyph < 0 || glyph >= glyphCount || character > ushort.MaxValue)
            throw new InvalidDataException("An NFTR character mapping references an invalid glyph.");
        mappings.Add(new((ushort)character, (ushort)glyph));
    }
}
