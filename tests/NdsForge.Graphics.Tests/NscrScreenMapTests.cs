using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Images;
using NdsForge.Graphics.Maps;
using NdsForge.Graphics.Palettes;
using NdsForge.Graphics.Tiles;

namespace NdsForge.Graphics.Tests;

public sealed class NscrScreenMapTests
{
    [Fact]
    public void PacksEveryTextEntryFieldAndPreservesTrailingBytes()
    {
        var entry = new NscrMapEntry(0x0234, horizontalFlip: true, verticalFlip: true, paletteIndex: 9);
        Assert.Equal((ushort)0x9E34, entry.PackedValue);
        Assert.Equal(entry, NscrMapEntry.FromPackedValue(entry.PackedValue));

        byte[] canonical = NscrScreenMap.Create(
            8, 8, NitroPaletteSelection.SixteenBySixteen, NitroBackgroundKind.Text, [entry])
            .CreateBuilder().Build();
        byte[] source = [.. canonical, 0xAA];
        NscrScreenMap map = NscrScreenMap.Parse(source);
        var replacement = new NscrMapEntry(3, paletteIndex: 2);
        byte[] edited = map.CreateBuilder().ReplaceEntry(0, 0, replacement).Build();

        Assert.Equal(source, map.CreateBuilder().Build());
        Assert.Equal((byte)0xAA, edited[^1]);
        Assert.Equal(replacement, NscrScreenMap.Parse(edited).Entries[0]);
    }

    [Fact]
    public void WritesAndReadsAffineEntries()
    {
        NscrMapEntry[] entries = Enumerable.Range(0, 4).Select(index => new NscrMapEntry((ushort)index)).ToArray();
        NscrScreenMap map = NscrScreenMap.Create(
            16, 16, NitroPaletteSelection.Single256, NitroBackgroundKind.Affine, entries);

        Assert.Equal(entries, map.Entries);
        Assert.Equal(4, map.DeclaredDataLength);
        Assert.Equal(map.CreateBuilder().Build(), map.CreateBuilder().Build(preserveSourceLayout: false));
        Assert.Throws<ArgumentException>(() => map.CreateBuilder().ReplaceEntry(
            0, 0, new NscrMapEntry(1, horizontalFlip: true)));
    }

    [Fact]
    public void RendersPaletteSelectionFlipsAndTransparentIndex()
    {
        byte[] tilePixels = new byte[16 * 8];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                tilePixels[(y * 16) + x] = (byte)x;
                tilePixels[(y * 16) + 8 + x] = (byte)(8 + x);
            }
        }

        NcgrCharacterGraphics characters = NcgrCharacterGraphics.Create(
            16, 8, NitroColorDepth.Indexed4Bpp, tilePixels);
        NitroColor555[] colors = Enumerable.Range(0, 32)
            .Select(index => new NitroColor555((ushort)index))
            .ToArray();
        NclrPalette palette = NclrPalette.Create(NitroColorDepth.Indexed4Bpp, colors);
        NscrScreenMap map = NscrScreenMap.Create(
            16,
            8,
            NitroPaletteSelection.SixteenBySixteen,
            NitroBackgroundKind.Text,
            [new NscrMapEntry(0, horizontalFlip: true), new NscrMapEntry(1, paletteIndex: 1)]);

        RgbaImage32 image = map.Render(characters, palette);

        Assert.Equal(colors[7].ToRgba32(), image.GetPixel(0, 0));
        Assert.Equal((byte)0, image.GetPixel(7, 0).Alpha);
        Assert.Equal(colors[24].ToRgba32(), image.GetPixel(8, 0));
        Assert.Equal(16, image.Width);
        Assert.Equal(8, image.Height);
    }

    [Fact]
    public void RejectsMalformedMetadataCountsAndPaletteReferences()
    {
        Assert.Throws<ArgumentException>(() => NscrScreenMap.Create(
            16, 8, NitroPaletteSelection.Single256, NitroBackgroundKind.Text, [new NscrMapEntry()]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NscrMapEntry(1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NscrMapEntry(0, paletteIndex: 16));

        byte[] valid = NscrScreenMap.Create(
            8, 8, NitroPaletteSelection.Single256, NitroBackgroundKind.Text, [new NscrMapEntry()])
            .CreateBuilder().Build();
        Assert.Throws<InvalidDataException>(() => NscrScreenMap.Parse(valid.AsSpan()[..15]));

        NcgrCharacterGraphics characters = NcgrCharacterGraphics.Create(
            8, 8, NitroColorDepth.Indexed8Bpp, Enumerable.Repeat((byte)2, 64).ToArray());
        NclrPalette palette = NclrPalette.Create(NitroColorDepth.Indexed8Bpp, [new NitroColor555()]);
        NscrScreenMap map = NscrScreenMap.Parse(valid);
        Assert.Throws<InvalidDataException>(() => map.Render(characters, palette));
    }
}
