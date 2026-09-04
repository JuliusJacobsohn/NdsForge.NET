using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Images;

namespace NdsForge.Graphics.Tests;

public sealed class IndexedImageSafetyTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void RejectsEmptyNegativeAndOverflowingDimensions(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => IndexedImage4.FromRgba32(width, height, []));

    [Fact]
    public void RejectsWrongPixelCountsAndChecksConfiguredLimit()
    {
        Assert.Throws<ArgumentException>(() => IndexedImage4.FromRgba32(2, 1, [default]));
        Assert.Throws<ArgumentOutOfRangeException>(() => IndexedImage4.FromRgba32(2, 1, [default, default], new() { MaximumPixels = 1 }));
        Assert.Equal(2, IndexedImage4.FromRgba32(2, 1, [default, default], new() { MaximumPixels = 2 }).Width);
        Assert.Throws<InvalidOperationException>(() => IndexedImage4.FromRgba32(2, 1, [default, default]).EncodeTiles());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    public void RejectsInvalidPaletteBudgets(int count) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        IndexedImage4.FromRgba32(1, 1, [default], new() { MaximumColors = count }));

    [Fact]
    public void RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IndexedImage4.FromRgba32(1, 1, [default], new() { MaximumPixels = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => IndexedImage4.FromRgba32(1, 1, [default], new() { MaximumPixels = int.MaxValue }));
        Assert.Throws<ArgumentOutOfRangeException>(() => IndexedImage4.FromRgba32(1, 1, [default], new() { ColorReduction = (NitroColorReduction)123 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => IndexedImage4.FromRgba32(1, 1, [default], new() { PaletteOverflow = (IndexedPaletteOverflow)123 }));
    }

    [Fact]
    public void RejectModeCountsPackedOpaqueColorsAndIncludesTransparencyReservation()
    {
        RgbaColor32[] pixels = Enumerable.Range(0, 16).Select(i => new RgbaColor32((byte)(i * 8), 0, 0)).ToArray();
        Assert.Throws<ArgumentException>(() => IndexedImage4.FromRgba32(16, 1, pixels, new() { PaletteOverflow = IndexedPaletteOverflow.Reject }));
        Assert.Equal(16, IndexedImage4.FromRgba32(15, 1, pixels.AsSpan(0, 15), new() { PaletteOverflow = IndexedPaletteOverflow.Reject }).ColorCount);
        Assert.True(IndexedImage4.FromRgba32(16, 1, pixels).WasColorReduced);
    }

    [Fact]
    public void ExplicitPaletteRejectsEmptyOversizedAndMissingOpaqueColors()
    {
        Assert.Throws<ArgumentException>(() => IndexedImage4.MapToPalette(1, 1, [default], []));
        Assert.Throws<ArgumentException>(() => IndexedImage4.MapToPalette(1, 1, [default], new ushort[17]));
        Assert.Throws<ArgumentException>(() => IndexedImage4.MapToPalette(1, 1, [default], new ushort[3], new() { MaximumColors = 2 }));
        Assert.Throws<ArgumentException>(() => IndexedImage4.MapToPalette(1, 1, [new(0, 0, 0)], [0]));
        Assert.Equal(1, IndexedImage4.MapToPalette(1, 1, [default], [0]).ColorCount);
        Assert.Throws<ArgumentException>(() => IndexedImage4.MapToPalette(1, 1, [new(255, 0, 0)], [0, 0], new() { PaletteOverflow = IndexedPaletteOverflow.Reject }));
        Assert.False(IndexedImage4.MapToPalette(1, 1, [new(255, 0, 0)], [0, 31], new() { PaletteOverflow = IndexedPaletteOverflow.Reject }).WasColorReduced);
    }

    [Fact]
    public void RuntimeAssemblyReferencesEnforceNativePackageBoundary()
    {
        string[] names = typeof(IndexedImage4).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();
        Assert.All(names, name => Assert.True(name is "NdsForge.Nitro" or "netstandard" || name.StartsWith("System.", StringComparison.Ordinal), $"Unexpected graphics dependency: {name}"));
    }
}
