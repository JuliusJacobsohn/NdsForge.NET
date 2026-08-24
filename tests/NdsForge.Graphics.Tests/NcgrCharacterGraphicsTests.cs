using NdsForge.Graphics.Palettes;
using NdsForge.Graphics.Tiles;

namespace NdsForge.Graphics.Tests;

public sealed class NcgrCharacterGraphicsTests
{
    [Theory]
    [InlineData(NitroColorDepth.Indexed4Bpp, true)]
    [InlineData(NitroColorDepth.Indexed4Bpp, false)]
    [InlineData(NitroColorDepth.Indexed8Bpp, true)]
    [InlineData(NitroColorDepth.Indexed8Bpp, false)]
    public void CreatesAndRoundTripsEveryDepthAndStorageOrder(NitroColorDepth depth, bool tileOrdered)
    {
        byte mask = depth == NitroColorDepth.Indexed4Bpp ? (byte)0x0F : byte.MaxValue;
        byte[] pixels = Enumerable.Range(0, 16 * 16)
            .Select(index => (byte)(((index / 16) + (index % 16 * 3)) & mask))
            .ToArray();

        NcgrCharacterGraphics graphics = NcgrCharacterGraphics.Create(
            16,
            16,
            depth,
            pixels,
            tileOrdered,
            NitroTileMapping.OneDimensional64K);
        byte[] canonical = graphics.CreateBuilder().Build(preserveSourceLayout: false);
        NcgrCharacterGraphics reparsed = NcgrCharacterGraphics.Parse(canonical);

        Assert.Equal(pixels, reparsed.Pixels);
        Assert.Equal(16, reparsed.Width);
        Assert.Equal(16, reparsed.Height);
        Assert.Equal(tileOrdered, reparsed.IsTileOrdered);
        Assert.Equal(NitroTileMapping.OneDimensional64K, reparsed.Mapping);
        Assert.Equal(pixels[8], reparsed.GetTilePixel(1, 0, 0));
    }

    [Fact]
    public void SupportsUnspecifiedDimensionsAndExactPreservationEdits()
    {
        byte[] pixels = Enumerable.Range(0, 128).Select(index => (byte)(index & 15)).ToArray();
        byte[] canonical = NcgrCharacterGraphics.Create(
            8,
            16,
            NitroColorDepth.Indexed4Bpp,
            pixels,
            omitDimensions: true).CreateBuilder().Build();
        byte[] source = [.. canonical, 0xCC, 0xDD];
        NcgrCharacterGraphics graphics = NcgrCharacterGraphics.Parse(source);

        byte[] unchanged = graphics.CreateBuilder().Build();
        byte[] edited = graphics.CreateBuilder().ReplacePixel(7, 15, 14).Build();

        Assert.True(graphics.HasUnspecifiedDimensions);
        Assert.Equal(source, unchanged);
        Assert.Equal([0xCC, 0xDD], edited[^2..]);
        Assert.Equal((byte)14, NcgrCharacterGraphics.Parse(edited).Pixels[^1]);
    }

    [Fact]
    public void RejectsInvalidDimensionsDepthIndicesAndTruncation()
    {
        Assert.Throws<ArgumentException>(() => NcgrCharacterGraphics.Create(
            9, 8, NitroColorDepth.Indexed4Bpp, new byte[72]));
        Assert.Throws<ArgumentOutOfRangeException>(() => NcgrCharacterGraphics.Create(
            8, 8, NitroColorDepth.None, new byte[64]));
        Assert.Throws<ArgumentException>(() => NcgrCharacterGraphics.Create(
            8, 8, NitroColorDepth.Indexed4Bpp, Enumerable.Repeat((byte)16, 64).ToArray()));

        byte[] valid = NcgrCharacterGraphics.Create(
            8, 8, NitroColorDepth.Indexed4Bpp, new byte[64]).CreateBuilder().Build();
        Assert.Throws<InvalidDataException>(() => NcgrCharacterGraphics.Parse(valid.AsSpan()[..15]));
        NcgrCharacterGraphics graphics = NcgrCharacterGraphics.Parse(valid);
        Assert.Throws<ArgumentOutOfRangeException>(() => graphics.GetTilePixel(1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => graphics.CreateBuilder().ReplacePixel(0, 0, 16));
    }
}
