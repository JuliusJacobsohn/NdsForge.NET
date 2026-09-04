using System.Security.Cryptography;
using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Images;

namespace NdsForge.Graphics.Tests;

public sealed class CartridgeLogoTests
{
    [Fact]
    public void AllRepeatedRowPatternsMatchCompleteEncodedDigest()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int pattern = 0; pattern < 256; pattern++)
        {
            byte[] pixels = Enumerable.Range(0, 1664).Select(i => (byte)(1 - ((pattern >> (7 - (i % 8))) & 1))).ToArray();
            CartridgeLogo logo = CartridgeLogo.FromPixels(pixels);
            hash.AppendData(logo.RawData.Span);
            Assert.Equal(pixels, CartridgeLogo.Parse(logo.RawData.Span).Pixels.ToArray());
            Assert.Equal(logo.EncodedBitLength, CartridgeLogo.MeasureEncodedBitLength(pixels));
            Assert.Equal(logo.RawData.ToArray(), logo.WriteCanonical());
            Assert.Equal(156, logo.RawData.Length);
        }
        Assert.Equal("1291CE17F568B4A99D1DF78E9554CFF9EEB0A6C1B5A94D1A2ED28D18F692707A", Convert.ToHexString(hash.GetHashAndReset()));
    }

    [Fact]
    public void EverySingleBackgroundPixelMatchesEncodingAndTilePlacement()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int position = 0; position < 1664; position++)
        {
            byte[] pixels = Enumerable.Repeat((byte)1, 1664).ToArray();
            pixels[position] = 0;
            CartridgeLogo logo = CartridgeLogo.FromPixels(pixels);
            hash.AppendData(logo.RawData.Span);
            Assert.Equal(pixels, CartridgeLogo.Parse(logo.RawData.Span).Pixels.ToArray());
            byte[] tiles = logo.EncodeTiles();
            Assert.Equal(208, tiles.Length);
            int x = position % 104;
            int y = position / 104;
            int row = (((y / 8) * 13) + (x / 8)) * 8 + (y % 8);
            Assert.Equal((byte)(255 ^ (1 << (x % 8))), tiles[row]);
            tiles[row] = 255;
            Assert.All(tiles, value => Assert.Equal(255, value));
        }
        Assert.Equal("A3A5CB61C48F98976DED302DFF25F96E5E31A85028A4E9BEB7BB0633C6368B08", Convert.ToHexString(hash.GetHashAndReset()));
    }

    [Theory]
    [InlineData(0, 436, "650D9050D0E9F6881977FA34F9870FB18E42FB1EE2C05FF4A24720BD7A7959ED")]
    [InlineData(1, 448, "6AD79C150EA7C40AA111F23CA2CC222C1852943831DD5C3D7A353B3C281A6CB4")]
    public void SolidPlanesHaveFixedCanonicalIdentities(byte pixel, int bits, string expected)
    {
        byte[] pixels = Enumerable.Repeat(pixel, 1664).ToArray();
        CartridgeLogo logo = CartridgeLogo.FromPixels(pixels);
        Assert.Equal(bits, logo.EncodedBitLength);
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(logo.RawData.Span)));
        Assert.Equal(pixels, CartridgeLogo.Parse(logo.RawData.Span).Pixels.ToArray());
    }

    [Fact]
    public void CreationParsingAndOutputCopiesDoNotAliasCallerBuffers()
    {
        byte[] pixels = new byte[1664];
        CartridgeLogo created = CartridgeLogo.FromPixels(pixels);
        pixels[0] = 1;
        byte[] encoded = created.WritePreserved();
        CartridgeLogo parsed = CartridgeLogo.Parse(encoded);
        encoded[0] ^= 255;
        byte[] canonical = parsed.WriteCanonical();
        canonical[0] ^= 255;
        byte[] tiles = parsed.EncodeTiles();
        tiles[0] = 255;
        Assert.Equal(created.RawData.ToArray(), parsed.RawData.ToArray());
        Assert.All(parsed.Pixels.ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(0, parsed.EncodeTiles()[0]);
        Assert.Equal(CartridgeLogo.FromPixels(new byte[1664]).RawData.ToArray(), parsed.WriteCanonical());
    }

    [Fact]
    public void EveryUnusedTailBitIsPreservedButRemovedByCanonicalWriting()
    {
        CartridgeLogo source = CartridgeLogo.FromPixels(new byte[1664]);
        byte[] changed = source.WritePreserved();
        for (int bit = source.EncodedBitLength; bit < 1248; bit++)
        {
            int index = ((bit / 32) * 4) + 3 - ((bit / 8) % 4);
            changed[index] |= (byte)(0x80 >> (bit % 8));
        }
        CartridgeLogo parsed = CartridgeLogo.Parse(changed);
        Assert.Equal(changed, parsed.WritePreserved());
        Assert.NotEqual(changed, parsed.WriteCanonical());
        Assert.Equal(source.WritePreserved(), parsed.WriteCanonical());
        Assert.Equal(source.Pixels.ToArray(), parsed.Pixels.ToArray());
        Assert.Equal(436, parsed.EncodedBitLength);
    }

    [Fact]
    public void RgbaImportAndRenderingUseExactColorsIncludingAlpha()
    {
        RgbaColor32 foreground = new(20, 70, 220, 120);
        RgbaColor32 background = new(180, 80, 40, 0);
        RgbaColor32[] colors = Enumerable.Range(0, 1664).Select(i => i % 104 < 52 ? foreground : background).ToArray();
        CartridgeLogo logo = CartridgeLogo.FromRgba32(colors, foreground, background);
        colors[0] = background;
        RgbaImage32 rendered = logo.Render(foreground, background);
        Assert.Equal(104, rendered.Width);
        Assert.Equal(16, rendered.Height);
        for (int i = 0; i < 1664; i++)
        {
            Assert.Equal(i % 104 < 52 ? 1 : 0, logo.Pixels.Span[i]);
            Assert.Equal(i % 104 < 52 ? foreground : background, rendered.Pixels[i]);
        }
        Assert.Equal(logo.RawData.ToArray(), CartridgeLogo.FromRgba32(rendered.Pixels.ToArray(), foreground, background).RawData.ToArray());
        Assert.All(logo.Render(background, background).Pixels, color => Assert.Equal(background, color));
    }

    [Fact]
    public async Task GeneratedFieldIntegratesWithImageBuilderAndDependentChecksums()
    {
        CartridgeLogo logo = CartridgeLogo.FromPixels(Enumerable.Range(0, 1664).Select(i => (byte)(i % 104 < 52 ? 1 : 0)).ToArray());
        var builder = new NdsImageBuilder
        {
            GameCode = "LG01",
            Arm9 = new(NdsProcessor.Arm9, [1, 2, 3, 4], 0x02000000, 0x02000000),
            Arm7 = new(NdsProcessor.Arm7, [5, 6, 7, 8], 0x02380000, 0x02380000),
        };
        builder.SetNintendoLogo(logo.RawData.Span);
        using NdsImage image = NdsImage.Load(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(logo.RawData.ToArray(), image.Header.RawData.Slice(0xC0, 156).ToArray());
        Assert.True(image.Validate().IsValid);
        Assert.Equal(logo.Pixels.ToArray(), CartridgeLogo.Parse(image.Header.RawData.Span.Slice(0xC0, 156)).Pixels.ToArray());
        Assert.Equal(image.Header.NintendoLogoCrc, NdsChecksums.ComputeCrc16(logo.RawData.Span));
    }
}
