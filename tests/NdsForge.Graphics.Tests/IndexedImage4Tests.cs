using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Images;

namespace NdsForge.Graphics.Tests;

public sealed class IndexedImage4Tests
{
    [Theory]
    [InlineData(0, "076A27C79E5ACE2A3D47F9DD2E83E4FF6EA8872B3C2218F66C92B89B55F36560")]
    [InlineData(1, "981B8AC0E448C2A01DF760648F17BA027D1ED0A9ADA17AA4CC74B9694B45D4AD")]
    [InlineData(2, "B3D0E1CD2E268569311B96826BFA03FEA954D9D56E2D3DD6961B751D9D155D89")]
    [InlineData(3, "38673AD353FD49685DF30930456844672FA339F896FFE20B93F496CB323809E4")]
    public void ExactIconsMatchPackedVectors(int pattern, string tilesHash)
    {
        RgbaColor32[] pixels = Enumerable.Range(0, 1024).Select(i => Pixel(pattern, i)).ToArray();
        IndexedImage4 image = IndexedImage4.FromRgba32(32, 32, pixels);
        Assert.Equal(tilesHash, Convert.ToHexString(SHA256.HashData(image.EncodeTiles())));
        string palette = pattern switch
        {
            1 => "00001F0000000000000000000000000000000000000000000000000000000000",
            3 => "0000A2244449E66D88122A37CC5B6E7C1021B245546AF60E98333A54DC787E1D",
            _ => new string('0', 64),
        };
        Assert.Equal(palette, Convert.ToHexString(image.EncodePalette()));
        Assert.False(image.WasColorReduced);
        Assert.True(image.HasTransparentIndex);
        Assert.Equal(pattern switch { 0 => 1, 3 => 16, _ => 2 }, image.ColorCount);
        IndexedImage4 again = IndexedImage4.FromRgba32(32, 32, image.Render().Pixels.ToArray());
        Assert.Equal(image.PaletteIndices.ToArray(), again.PaletteIndices.ToArray());
        Assert.Equal(image.Palette.ToArray(), again.Palette.ToArray());
    }

    [Fact]
    public void EveryGrayValueMatchesPackedRamp()
    {
        byte[] words = new byte[512];
        for (int i = 0; i < 256; i++)
        {
            IndexedImage4 image = IndexedImage4.FromRgba32(1, 1, [new((byte)i, (byte)i, (byte)i)]);
            BinaryPrimitives.WriteUInt16LittleEndian(words.AsSpan(i * 2), image.Palette.Span[1]);
        }
        Assert.Equal("71F380A6C6AD7E7AFF05D3418363DDB0169EF06A4DE1363E814C7CDF751889BE", Convert.ToHexString(SHA256.HashData(words)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    public void AlphaThresholdIsInclusiveAndHiddenColorsDoNotConsumeSlots(byte threshold)
    {
        RgbaColor32[] pixels = Enumerable.Range(0, 256).Select(i => new RgbaColor32(200, 80, 40, (byte)i)).ToArray();
        IndexedImage4 image = IndexedImage4.FromRgba32(16, 16, pixels, new() { AlphaThreshold = threshold });
        Assert.Equal(threshold == 255 ? 1 : 2, image.ColorCount);
        for (int i = 0; i < pixels.Length; i++) { Assert.Equal(i <= threshold ? 0 : 1, image.PaletteIndices.Span[i]); }
    }

    [Fact]
    public void DeduplicatesAfterPackingAndRetainsFirstAppearanceOrder()
    {
        RgbaColor32[] pixels = Enumerable.Range(0, 8).Select(i => new RgbaColor32((byte)(8 + i), (byte)(16 + i), (byte)(24 + i))).ToArray();
        IndexedImage4 image = IndexedImage4.FromRgba32(8, 1, pixels);
        Assert.Equal(2, image.ColorCount);
        Assert.Equal((ushort)0x0C41, image.Palette.Span[1]);
        Assert.All(image.PaletteIndices.ToArray(), index => Assert.Equal(1, index));
        IndexedImage4 ordered = IndexedImage4.FromRgba32(4, 1, [new(0, 255, 0), new(255, 0, 0), default, new(0, 255, 0)]);
        Assert.Equal(new byte[] { 1, 2, 0, 1 }, ordered.PaletteIndices.ToArray());
        Assert.Equal(new ushort[] { 0, 0x3E0, 0x1F }, ordered.Palette.Span[..3].ToArray());
    }

    [Fact]
    public void NearestPackingIsExplicitAndDoesNotChangeExistingColorBehavior()
    {
        IndexedImage4 truncated = IndexedImage4.FromRgba32(1, 1, [new(7, 7, 7)]);
        IndexedImage4 nearest = IndexedImage4.FromRgba32(1, 1, [new(7, 7, 7)], new() { ColorReduction = NitroColorReduction.Nearest });
        Assert.Equal((ushort)0, truncated.Palette.Span[1]);
        Assert.Equal((ushort)0x421, nearest.Palette.Span[1]);
        Assert.Equal(NitroColor555.FromRgba32(new(7, 7, 7)).PackedValue, nearest.Palette.Span[1]);
    }

    [Fact]
    public void ExplicitPalettePreservesBitsOrderAndDuplicatesWithStableDistanceTies()
    {
        ushort[] palette = [0xFFFF, 0x8002, 0, 0x8002];
        RgbaColor32[] pixels = [default, new(8, 0, 0), new(0, 0, 0), new(16, 0, 0)];
        IndexedImage4 image = IndexedImage4.MapToPalette(4, 1, pixels, palette);
        Assert.Equal(new byte[] { 0, 1, 2, 1 }, image.PaletteIndices.ToArray());
        Assert.Equal(palette, image.Palette.Span[..4].ToArray());
        Assert.True(image.WasColorReduced);
        palette[1] = 0;
        pixels[1] = default;
        Assert.Equal((ushort)0x8002, image.Palette.Span[1]);
        Assert.Equal(new RgbaColor32(16, 0, 0), image.Render().Pixels[1]);
        Assert.Equal(default, image.Render().Pixels[0]);
    }

    [Fact]
    public void WithoutTransparencyAllSixteenSlotsAreOpaqueAndAlphaIsIgnored()
    {
        RgbaColor32[] pixels = Enumerable.Range(0, 16).Select(i => new RgbaColor32((byte)(i * 8), 0, 0, 0)).ToArray();
        IndexedImage4 image = IndexedImage4.FromRgba32(16, 1, pixels, new() { ReserveTransparentIndex = false, PaletteOverflow = IndexedPaletteOverflow.Reject });
        Assert.False(image.HasTransparentIndex);
        Assert.Equal(16, image.ColorCount);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (byte)i), image.PaletteIndices.ToArray());
        Assert.All(image.Render().Pixels, pixel => Assert.Equal(255, pixel.Alpha));
    }

    [Fact]
    public void RectangularTileOrderIsIndependentFromRowOrder()
    {
        RgbaColor32[] pixels = Enumerable.Range(0, 16 * 24).Select(i => new RgbaColor32((byte)(((i / 16 / 8 * 2) + (i % 16 / 8) + 1) * 8), 0, 0)).ToArray();
        IndexedImage4 image = IndexedImage4.FromRgba32(16, 24, pixels);
        byte[] tiles = image.EncodeTiles();
        for (int tile = 0; tile < 6; tile++) { Assert.All(tiles.AsSpan(tile * 32, 32).ToArray(), value => Assert.Equal((tile + 1) * 17, value)); }
    }

    internal static RgbaColor32 Pixel(int pattern, int i) => pattern switch
    {
        0 => new((byte)(i & 255), (byte)((i >> 2) & 255), (byte)((i * 13) & 255), 0),
        1 => new(255, 0, 0),
        2 => (i & 1) == 0 ? new(40, 70, 90, 0) : new(0, 0, 0),
        _ => (i & 15) == 0 ? default : new((byte)((i & 15) * 16), (byte)(((i & 15) * 40) & 255), (byte)(((i & 15) * 72) & 255)),
    };
}
