using System.Security.Cryptography;
using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Images;

namespace NdsForge.Graphics.Tests;

public sealed class BannerConversionTests
{
    [Theory]
    [InlineData(1, 0, "1A9F5DB3B1A031F68C750C42E3246BB5DF673C693B06513720C5491C08B2DE8D")]
    [InlineData(1, 1, "07E6C522EC25E618760252A045054A75B207F5DCD8695A80D0D048F3B4DD7821")]
    [InlineData(1, 2, "D791987F14528FFE3D0BC50EFD746671E689BBF8B13156E00C43B8734B930C3B")]
    [InlineData(1, 3, "F561E24DADAB7AEAA70B5B9337CE08DBF1EE26B057734E6E83C5A675033E3457")]
    [InlineData(2, 0, "408154425F451C5853EC278D021840E0D8C9B265FF3892F6FD6EAA1841A77B80")]
    [InlineData(2, 1, "2AA5AD89BC5C8787374CD052BD653E75A1A4302A1A0B9BEB0C61F0D66C4F9F59")]
    [InlineData(2, 2, "6EEDAF52F447579F813C87632A79038E54B95C7B5A5F748609EE1F7FF5E58760")]
    [InlineData(2, 3, "6F7668137F8A33CE749491F19C2A997F28A3E1E6F8234E23889D21873724EADC")]
    [InlineData(3, 0, "FF690348863E1DE36CDBED4E61468B93211095B47630AA1B417E447E9EF9DC2B")]
    [InlineData(3, 1, "60BE8EA89FCA5CC7BF88CEEF2409C039672A5690B9617BC55D089EB112A6887E")]
    [InlineData(3, 2, "FE3F7E29A253E47B3268C51B2772174CEB8637200ADF60D7C60C85C069F2D389")]
    [InlineData(3, 3, "98794DF0F4EF1AE153CAB03AA8981B87D838DCA6E06896484ED2B542C9D5B121")]
    [InlineData(0x103, 0, "1D6390C47AE267D64F941D822902EA78C4C0E46BDA7C7DEBEEA7A609CFDE729E")]
    [InlineData(0x103, 1, "C68CDE26FD5D7C50122AD08EC03C5D85A22B0A6E57AAE6D2E8B2D8C8EB73C793")]
    [InlineData(0x103, 2, "0334B9DE9DD1E42F0369DE5F0A4F090B863A8DBB977AA7E5E3A0AD920B22FE1A")]
    [InlineData(0x103, 3, "D719D4938BCEF2EC298128A153F052F1899E8852AB9F0903C9F607F3404A7E74")]
    public void EveryBannerVersionMatchesCompleteIndexedConversionVector(ushort version, int pattern, string expected)
    {
        IndexedImage4 icon = IndexedImage4.FromRgba32(32, 32, Enumerable.Range(0, 1024).Select(i => IndexedImage4Tests.Pixel(pattern, i)).ToArray());
        NdsBanner banner = new NdsBannerBuilder(version).SetTitle(NdsBannerLanguage.English, "RGBA fixture")
            .SetIndexedIcon(icon.PaletteIndices.Span, icon.Palette.Span).Build();
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(banner.RawData.Span)));
        Assert.Equal(icon.EncodeTiles(), banner.RawData.Span.Slice(0x20, 512).ToArray());
        Assert.Equal(icon.EncodePalette(), banner.RawData.Span.Slice(0x220, 32).ToArray());
        Assert.Equal(banner.RawData.ToArray(), banner.WithRepairedCrcs().RawData.ToArray());
        Assert.Equal(banner.RawData.ToArray(), NdsBanner.Parse(banner.RawData).RawData.ToArray());
    }

    [Fact]
    public void AllEightRgbaFramesAndFullSequenceMatchCompleteBannerVector()
    {
        IndexedImage4 icon = IndexedImage4.FromRgba32(32, 32, Enumerable.Range(0, 1024).Select(i => IndexedImage4Tests.Pixel(3, i)).ToArray());
        var builder = new NdsBannerBuilder(0x103).SetTitle(NdsBannerLanguage.English, "RGBA fixture")
            .SetIndexedIcon(icon.PaletteIndices.Span, icon.Palette.Span);
        var frames = new IndexedImage4[8];
        for (int slot = 0; slot < 8; slot++)
        {
            frames[slot] = IndexedImage4.FromRgba32(32, 32, Enumerable.Range(0, 1024).Select(i => FramePixel(i, slot)).ToArray());
            builder.SetAnimatedFrame(slot, frames[slot].PaletteIndices.Span, frames[slot].Palette.Span);
        }
        NdsBannerAnimationStep[] steps = Enumerable.Range(0, 63).Select(i => new NdsBannerAnimationStep((byte)(i + 1), (byte)(i & 7), (byte)((7 - i) & 7), (i & 1) != 0, (i & 2) != 0)).ToArray();
        NdsBanner banner = builder.SetAnimationSequence(steps).Build();
        Assert.Equal("8B16A275DC39D27AF3248CC5412A006D48665868E305C422D8406331163B8F79", Convert.ToHexString(SHA256.HashData(banner.RawData.Span)));
        Assert.Equal(steps, banner.GetAnimationSteps());
        Assert.Equal(0, banner.GetAnimationSequence()[63]);
        Assert.Equal(new ushort[] { 0x4F8D, 0x1E94, 0x5952, 0xF800 }, banner.StoredCrcs);
        for (int slot = 0; slot < 8; slot++)
        {
            Assert.Equal(frames[slot].EncodeTiles(), banner.RawData.Span.Slice(0x1240 + (slot * 512), 512).ToArray());
            Assert.Equal(frames[slot].EncodePalette(), banner.RawData.Span.Slice(0x2240 + (slot * 32), 32).ToArray());
        }
        foreach (NdsBannerAnimationStep step in steps)
        {
            byte[] rendered = banner.RenderAnimationStepRgba32(step);
            for (int i = 0; i < 1024; i++)
            {
                int x = step.FlipHorizontal ? 31 - (i % 32) : i % 32;
                int y = step.FlipVertical ? 31 - (i / 32) : i / 32;
                byte index = frames[step.TileFrame].PaletteIndices.Span[(y * 32) + x];
                int packed = frames[step.PaletteFrame].Palette.Span[index];
                Assert.Equal(index == 0 ? 0 : 255, rendered[(i * 4) + 3]);
                if (index == 0) { continue; }
                Assert.Equal(packed & 31, rendered[i * 4] >> 3);
                Assert.Equal((packed >> 5) & 31, rendered[(i * 4) + 1] >> 3);
                Assert.Equal((packed >> 10) & 31, rendered[(i * 4) + 2] >> 3);
            }
        }
    }

    private static RgbaColor32 FramePixel(int i, int slot)
    {
        int color = ((i % 32) + (3 * (i / 32)) + slot) & 15;
        return color == 0 ? default : new((byte)(color * 16), (byte)(((color * 40) + (slot * 8)) & 255), (byte)(((color * 72) + (slot * 16)) & 255));
    }
}
