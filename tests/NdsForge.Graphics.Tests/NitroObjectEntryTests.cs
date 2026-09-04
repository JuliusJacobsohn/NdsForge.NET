using NdsForge.Graphics.Palettes;
using NdsForge.Graphics.Sprites;

namespace NdsForge.Graphics.Tests;

public sealed class NitroObjectEntryTests
{
    [Theory]
    [InlineData(8, 8)]
    [InlineData(64, 64)]
    [InlineData(32, 8)]
    [InlineData(8, 32)]
    public void CreatesAndProjectsHardwareShapes(int width, int height)
    {
        NitroObjectEntry value = NitroObjectEntry.Create(
            -17, -8, width, height, 321, NitroColorDepth.Indexed4Bpp,
            paletteIndex: 9, priority: 2, horizontalFlip: true, verticalFlip: true,
            mode: NitroObjectMode.SemiTransparent, mosaic: true);
        Assert.Equal(-17, value.X);
        Assert.Equal(-8, value.Y);
        Assert.Equal(width, value.Width);
        Assert.Equal(height, value.Height);
        Assert.Equal((ushort)321, value.CharacterName);
        Assert.Equal((byte)9, value.PaletteIndex);
        Assert.Equal((byte)2, value.Priority);
        Assert.True(value.HorizontalFlip);
        Assert.True(value.VerticalFlip);
        Assert.True(value.IsMosaic);
        Assert.Equal(NitroObjectMode.SemiTransparent, value.Mode);
    }

    [Fact]
    public void ProjectsAffineFieldsAndRejectsProhibitedValues()
    {
        var affine = new NitroObjectEntry(0x2300, 0x2A00, 0);
        Assert.True(affine.IsAffine);
        Assert.True(affine.IsDoubleSize);
        Assert.Equal((byte)21, affine.AffineGroup);
        Assert.False(affine.HorizontalFlip);
        Assert.Equal(NitroColorDepth.Indexed8Bpp, affine.Depth);
        Assert.Throws<InvalidDataException>(() => new NitroObjectEntry(0xC000, 0, 0));
        Assert.Throws<ArgumentException>(() => NitroObjectEntry.Create(0, 0, 12, 12, 0, NitroColorDepth.Indexed4Bpp));
    }
}
