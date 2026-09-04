using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Images;

namespace NdsForge.Graphics.Tests;

public sealed class IndexedImageReductionTests
{
    [Fact]
    public void OneOpaqueSlotUsesFrequencyWeightedRoundedMean()
    {
        IndexedImage4 image = IndexedImage4.FromRgba32(4, 1, [new(0, 0, 0), new(0, 0, 0), new(0, 0, 0), new(248, 0, 0)], new() { MaximumColors = 2 });
        Assert.Equal((ushort)8, image.Palette.Span[1]);
        Assert.True(image.WasColorReduced);
        Assert.All(image.PaletteIndices.ToArray(), index => Assert.Equal(1, index));
    }

    [Fact]
    public void GradientReductionIsDeterministicOrderIndependentAndBoundsColorError()
    {
        RgbaColor32[] pixels = Enumerable.Range(0, 1024).Select(i => new RgbaColor32((byte)((i % 32) * 8), (byte)((i / 32) * 8), (byte)(((i % 32) + (i / 32)) * 4))).ToArray();
        IndexedImage4 image = IndexedImage4.FromRgba32(32, 32, pixels);
        IndexedImage4 again = IndexedImage4.FromRgba32(32, 32, pixels);
        IndexedImage4 reversed = IndexedImage4.FromRgba32(32, 32, pixels.Reverse().ToArray());
        Assert.True(image.WasColorReduced);
        Assert.InRange(image.ColorCount, 2, 16);
        Assert.Equal(image.Palette.ToArray(), again.Palette.ToArray());
        Assert.Equal(image.PaletteIndices.ToArray(), again.PaletteIndices.ToArray());
        Assert.Equal(image.Palette.ToArray(), reversed.Palette.ToArray());
        Assert.Equal(image.PaletteIndices.ToArray().Reverse(), reversed.PaletteIndices.ToArray());
        long squaredError = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            int color = image.Palette.Span[image.PaletteIndices.Span[i]];
            int red = (pixels[i].Red >> 3) - (color & 31);
            int green = (pixels[i].Green >> 3) - ((color >> 5) & 31);
            int blue = (pixels[i].Blue >> 3) - ((color >> 10) & 31);
            squaredError += (red * red) + (green * green) + (blue * blue);
            Assert.NotEqual(0, image.PaletteIndices.Span[i]);
        }
        Assert.True(squaredError < 20000, $"Squared five-bit RGB error: {squaredError}");
        IndexedImage4 roundTrip = IndexedImage4.MapToPalette(32, 32, image.Render().Pixels.ToArray(), image.Palette.Span,
            new() { PaletteOverflow = IndexedPaletteOverflow.Reject });
        Assert.Equal(image.PaletteIndices.ToArray(), roundTrip.PaletteIndices.ToArray());
    }

    [Fact]
    public void TransparentNoiseDoesNotChangeReducedOpaquePalette()
    {
        RgbaColor32[] opaque = Enumerable.Range(0, 32).Select(i => new RgbaColor32((byte)(i * 8), 0, 0)).ToArray();
        RgbaColor32[] combined = opaque.Concat(Enumerable.Range(0, 500).Select(i => new RgbaColor32((byte)(i & 255), 123, 211, 0))).ToArray();
        IndexedImage4 a = IndexedImage4.FromRgba32(32, 1, opaque);
        IndexedImage4 b = IndexedImage4.FromRgba32(combined.Length, 1, combined);
        Assert.Equal(a.Palette.ToArray(), b.Palette.ToArray());
        Assert.Equal(a.PaletteIndices.ToArray(), b.PaletteIndices.Span[..32].ToArray());
        Assert.All(b.PaletteIndices.Span[32..].ToArray(), i => Assert.Equal(0, i));
    }
}
