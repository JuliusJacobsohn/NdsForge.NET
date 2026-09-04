using System.Buffers.Binary;
using NdsForge.Graphics.Fonts;

namespace NdsForge.Graphics.Tests;

public sealed class NftrFontTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ParsePreserveAndCanonicalWriteCoverIndexedDepths(byte depth)
    {
        byte[] encoded = CreateSpecimen(depth);

        NftrFont font = NftrFont.Parse(encoded);

        Assert.Equal(0x0101, font.Version);
        Assert.Equal(2, font.FontType);
        Assert.Equal(9, font.LineHeight);
        Assert.Equal(3, font.FallbackGlyphIndex);
        Assert.Equal(new NftrGlyphMetrics(-1, 3, 4), font.DefaultMetrics);
        Assert.Equal(NftrTextEncoding.Utf16, font.Encoding);
        Assert.Equal(3, font.CellWidth);
        Assert.Equal(2, font.CellHeight);
        Assert.Equal(depth, font.BitsPerPixel);
        Assert.Equal(4, font.Glyphs.Count);
        Assert.Equal(3, font.CharacterMaps.Count);
        Assert.Equal([NftrCharacterMapMethod.Direct, NftrCharacterMapMethod.Table, NftrCharacterMapMethod.Scan],
            font.CharacterMaps.Select(static map => map.Method));
        Assert.Equal(encoded, font.CreateBuilder().Build());
        AssertGlyphSemanticsEqual(font, NftrFont.Parse(font.CreateBuilder().Build(preserveSourceLayout: false)));
    }

    [Fact]
    public void BuilderPatchesPixelsAndMetricsWithoutChangingOtherSourceBytes()
    {
        byte[] encoded = CreateSpecimen(3);
        NftrFont font = NftrFont.Parse(encoded);
        byte[] replacement = [7, 6, 5, 4, 3, 2];

        byte[] edited = font.CreateBuilder()
            .ReplaceGlyphPixels(2, replacement)
            .ReplaceGlyphMetrics(2, new NftrGlyphMetrics(-3, 2, 5))
            .Build();

        NftrFont reparsed = NftrFont.Parse(edited);
        Assert.Equal(replacement, reparsed.Glyphs[2].StoredPixels);
        Assert.Equal(new NftrGlyphMetrics(-3, 2, 5), reparsed.Glyphs[2].Metrics);
        Assert.Equal(font.Glyphs[1].StoredPixels, reparsed.Glyphs[1].StoredPixels);
        Assert.Equal(font.CharacterMaps.SelectMany(static map => map.Mappings),
            reparsed.CharacterMaps.SelectMany(static map => map.Mappings));
    }

    [Fact]
    public void CharacterLookupHonorsDirectTableAndSparseMaps()
    {
        NftrFont font = NftrFont.Parse(CreateSpecimen(2));

        Assert.True(font.TryGetGlyphIndex(0x20, out ushort direct));
        Assert.Equal(0, direct);
        Assert.True(font.TryGetGlyphIndex(0x30, out ushort table));
        Assert.Equal(2, table);
        Assert.False(font.TryGetGlyphIndex(0x31, out _));
        Assert.True(font.TryGetGlyphIndex(0x50, out ushort sparse));
        Assert.Equal(2, sparse);
    }

    [Fact]
    public void ParserPreservesBytesBeyondDeclaredFileAllocation()
    {
        byte[] specimen = CreateSpecimen(1);
        byte[] allocation = [.. specimen, 0xFF, 0xA5, 0x00];

        NftrFont font = NftrFont.Parse(allocation);

        Assert.Equal(allocation, font.CreateBuilder().Build());
        Assert.Equal(specimen.Length, font.CreateBuilder().Build(preserveSourceLayout: false).Length);
    }

    [Fact]
    public void ExtendedFinfMetricsSurviveCanonicalWrite()
    {
        NftrFont font = NftrFont.Parse(CreateSpecimen(2, includeExtendedMetrics: true));

        Assert.Equal(new NftrExtendedMetrics(12, 7, -2, 1), font.ExtendedMetrics);
        NftrFont rebuilt = NftrFont.Parse(font.CreateBuilder().Build(preserveSourceLayout: false));
        Assert.Equal(font.ExtendedMetrics, rebuilt.ExtendedMetrics);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ParserRejectsMalformedStructures(int rawCorruption)
    {
        Corruption corruption = (Corruption)rawCorruption;
        byte[] encoded = CreateSpecimen(2);
        int cglp = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(0x20))) - 8;
        int firstMap = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(0x28))) - 8;
        int tableMap = firstMap + 0x18;
        switch (corruption)
        {
            case Corruption.BadSignature:
                encoded[0] = 0;
                break;
            case Corruption.TruncatedFile:
                BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(8), (uint)(encoded.Length + 1));
                break;
            case Corruption.InvalidCglpPointer:
                BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(0x20), 8);
                break;
            case Corruption.InvalidDepth:
                encoded[cglp + 14] = 0;
                break;
            case Corruption.InvalidGlyphReference:
                BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(tableMap + 20), 4);
                break;
            case Corruption.CyclicMapChain:
                BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(firstMap + 16), (uint)(firstMap + 8));
                break;
            default:
                throw new InvalidOperationException();
        }

        Assert.Throws<InvalidDataException>(() => NftrFont.Parse(encoded));
    }

    [Fact]
    public void BuilderValidatesReplacementBounds()
    {
        NftrFontBuilder builder = NftrFont.Parse(CreateSpecimen(2)).CreateBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.ReplaceGlyphPixels(4, new byte[6]));
        Assert.Throws<ArgumentException>(() => builder.ReplaceGlyphPixels(0, new byte[5]));
        Assert.Throws<ArgumentException>(() => builder.ReplaceGlyphPixels(0, new byte[] { 0, 1, 2, 3, 4, 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.ReplaceGlyphMetrics(-1, default));
    }

    private static byte[] CreateSpecimen(byte depth, bool includeExtendedMetrics = false)
    {
        int glyphLength = (6 * depth + 7) / 8;
        int cglpLength = 0x10 + (4 * glyphLength);
        int finfOffset = 0x10;
        int finfLength = includeExtendedMetrics ? 0x20 : 0x1C;
        int cglpOffset = finfOffset + finfLength;
        int cwdhOffset = cglpOffset + cglpLength;
        int directOffset = cwdhOffset + 0x1C;
        int tableOffset = directOffset + 0x18;
        int scanOffset = tableOffset + 0x1C;
        int total = scanOffset + 0x20;
        byte[] result = new byte[total];
        "RTFN"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 0xFEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), 0x0101);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), 6);

        WriteBlock(result, finfOffset, "FNIF"u8, finfLength);
        result[finfOffset + 8] = 2;
        result[finfOffset + 9] = 9;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(finfOffset + 10), 3);
        result[finfOffset + 12] = 0xFF;
        result[finfOffset + 13] = 3;
        result[finfOffset + 14] = 4;
        result[finfOffset + 15] = (byte)NftrTextEncoding.Utf16;
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(finfOffset + 16), (uint)(cglpOffset + 8));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(finfOffset + 20), (uint)(cwdhOffset + 8));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(finfOffset + 24), (uint)(directOffset + 8));
        if (includeExtendedMetrics)
        {
            result[finfOffset + 28] = 12;
            result[finfOffset + 29] = 7;
            result[finfOffset + 30] = 0xFE;
            result[finfOffset + 31] = 1;
        }

        WriteBlock(result, cglpOffset, "PLGC"u8, cglpLength);
        result[cglpOffset + 8] = 3;
        result[cglpOffset + 9] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(cglpOffset + 10), (ushort)glyphLength);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(cglpOffset + 12), 0x1020);
        result[cglpOffset + 14] = depth;
        for (int glyph = 0; glyph < 4; glyph++)
        {
            byte[] pixels = Enumerable.Range(0, 6).Select(pixel => (byte)((pixel + glyph) & ((1 << depth) - 1))).ToArray();
            EncodePixels(pixels, depth, result.AsSpan(cglpOffset + 0x10 + (glyph * glyphLength), glyphLength));
        }

        WriteBlock(result, cwdhOffset, "HDWC"u8, 0x1C);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(cwdhOffset + 8), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(cwdhOffset + 10), 3);
        for (int glyph = 0; glyph < 4; glyph++)
        {
            result[cwdhOffset + 0x10 + (glyph * 3)] = (byte)(glyph - 1);
            result[cwdhOffset + 0x11 + (glyph * 3)] = (byte)(glyph + 1);
            result[cwdhOffset + 0x12 + (glyph * 3)] = (byte)(glyph + 2);
        }

        WriteMapHeader(result, directOffset, 0x18, 0x20, 0x21, NftrCharacterMapMethod.Direct, tableOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(directOffset + 20), 0);
        WriteMapHeader(result, tableOffset, 0x1C, 0x30, 0x32, NftrCharacterMapMethod.Table, scanOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(tableOffset + 20), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(tableOffset + 22), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(tableOffset + 24), 3);
        WriteMapHeader(result, scanOffset, 0x20, 0x40, 0x50, NftrCharacterMapMethod.Scan, null);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(scanOffset + 20), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(scanOffset + 22), 0x40);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(scanOffset + 24), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(scanOffset + 26), 0x50);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(scanOffset + 28), 2);
        return result;
    }

    private static void WriteBlock(byte[] target, int offset, ReadOnlySpan<byte> magic, int length)
    {
        magic.CopyTo(target.AsSpan(offset));
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset + 4), (uint)length);
    }

    private static void WriteMapHeader(byte[] target, int offset, int length, ushort first, ushort last,
        NftrCharacterMapMethod method, int? next)
    {
        WriteBlock(target, offset, "PAMC"u8, length);
        BinaryPrimitives.WriteUInt16LittleEndian(target.AsSpan(offset + 8), first);
        BinaryPrimitives.WriteUInt16LittleEndian(target.AsSpan(offset + 10), last);
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset + 12), (uint)method);
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset + 16), next is null ? 0u : (uint)(next.Value + 8));
    }

    private static void EncodePixels(IReadOnlyList<byte> pixels, int depth, Span<byte> target)
    {
        int bit = 0;
        foreach (byte pixel in pixels)
        {
            for (int plane = depth - 1; plane >= 0; plane--, bit++)
                target[bit >> 3] |= (byte)(((pixel >> plane) & 1) << (7 - (bit & 7)));
        }
    }

    private static void AssertGlyphSemanticsEqual(NftrFont expected, NftrFont actual)
    {
        Assert.Equal(expected.Glyphs.Select(static glyph => glyph.Metrics), actual.Glyphs.Select(static glyph => glyph.Metrics));
        for (int index = 0; index < expected.Glyphs.Count; index++)
            Assert.Equal(expected.Glyphs[index].StoredPixels, actual.Glyphs[index].StoredPixels);
        Assert.Equal(expected.CharacterMaps.SelectMany(static map => map.Mappings),
            actual.CharacterMaps.SelectMany(static map => map.Mappings));
    }

    private enum Corruption
    {
        BadSignature,
        TruncatedFile,
        InvalidCglpPointer,
        InvalidDepth,
        InvalidGlyphReference,
        CyclicMapChain,
    }
}
