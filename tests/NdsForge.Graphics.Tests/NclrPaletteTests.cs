using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Palettes;

namespace NdsForge.Graphics.Tests;

public sealed class NclrPaletteTests
{
    [Fact]
    public void CreatesParsesAndDeterministicallyWritesPaletteMapping()
    {
        NitroColor555[] colors = [new(0), new(0x001F), new(0x83E0), new(0x7C00)];

        NclrPalette palette = NclrPalette.Create(
            NitroColorDepth.Indexed4Bpp,
            colors,
            isExtendedPalette: true,
            paletteMapping: [3, 7]);
        byte[] first = palette.CreateBuilder().Build();
        byte[] second = palette.CreateBuilder().Build(preserveSourceLayout: false);

        Assert.Equal(first, second);
        Assert.Equal(colors, palette.Colors);
        Assert.Equal([3, 7], palette.PaletteMapping);
        Assert.True(palette.IsExtendedPalette);
        Assert.Equal((uint)8, palette.DeclaredColorDataLength);
    }

    [Fact]
    public void PreservesTrailingBytesAndPatchesOnlySelectedColor()
    {
        byte[] canonical = NclrPalette.Create(
            NitroColorDepth.Indexed8Bpp,
            [new(0x001F), new(0x03E0)]).CreateBuilder().Build();
        byte[] source = [.. canonical, 0xAA, 0xBB];
        NclrPalette palette = NclrPalette.Parse(source);

        byte[] unchanged = palette.CreateBuilder().Build();
        byte[] edited = palette.CreateBuilder().ReplaceColor(1, new(0x7C00)).Build();

        Assert.Equal(source, unchanged);
        Assert.Equal(source.Length, edited.Length);
        Assert.Equal([0xAA, 0xBB], edited[^2..]);
        Assert.Equal((ushort)0x7C00, NclrPalette.Parse(edited).Colors[1].PackedValue);
    }

    [Fact]
    public void RejectsTruncatedBlocksUnsupportedDepthAndOutOfRangeMapping()
    {
        byte[] valid = NclrPalette.Create(
            NitroColorDepth.Indexed4Bpp,
            [new(0)],
            paletteMapping: [0]).CreateBuilder().Build();

        Assert.Throws<InvalidDataException>(() => NclrPalette.Parse(valid.AsSpan()[..15]));
        Assert.Throws<InvalidDataException>(() => NclrPalette.Parse(Mutate(valid, 24, 5)));
        int mappingBlock = 0x10 + 0x1A;
        Assert.Throws<InvalidDataException>(() => NclrPalette.Parse(Mutate(valid, mappingBlock + 12, 0xFF)));
        Assert.Throws<ArgumentOutOfRangeException>(() => NclrPalette.Create(
            (NitroColorDepth)99,
            [new(0)]));
    }

    private static byte[] Mutate(byte[] source, int offset, byte value)
    {
        byte[] result = source.ToArray();
        result[offset] = value;
        return result;
    }
}
