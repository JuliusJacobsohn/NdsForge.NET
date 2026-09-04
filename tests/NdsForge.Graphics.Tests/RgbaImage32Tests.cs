using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Images;

namespace NdsForge.Graphics.Tests;

public sealed class RgbaImage32Tests
{
    [Fact]
    public void CopiesPixelsAndChecksCoordinates()
    {
        RgbaColor32[] pixels = [new(1, 2, 3), new(4, 5, 6)];
        var image = new RgbaImage32(2, 1, pixels);
        pixels[0] = new(9, 9, 9);

        Assert.Equal(new RgbaColor32(1, 2, 3), image.GetPixel(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(2, 0));
        Assert.Throws<ArgumentException>(() => new RgbaImage32(1, 1, pixels));
    }
}
