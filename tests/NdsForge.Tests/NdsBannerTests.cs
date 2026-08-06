namespace NdsForge.Tests;

public sealed class NdsBannerTests
{
    [Fact]
    public void ParsesLocalizedTitlesAndValidCrc()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateWithBanner());

        NdsBanner banner = Assert.IsType<NdsBanner>(image.Banner);

        Assert.Equal(1, banner.Version);
        Assert.Equal(6, banner.LanguageCount);
        Assert.Equal("English Title", banner.Titles[NdsBannerLanguage.English]);
        Assert.False(banner.IsAnimated);
        Assert.True(image.Validate().IsValid);
    }

    [Fact]
    public void RendersTiledBgr555IconAsRgba()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateWithBanner());

        byte[] pixels = image.Banner!.RenderIconRgba32();

        Assert.Equal([255, 0, 0, 255], pixels[..4]);
        Assert.Equal([0, 0, 0, 0], pixels[4..8]);
    }

    [Fact]
    public void ValidationReportsBannerCrcMismatch()
    {
        byte[] data = SyntheticImage.CreateWithBanner();
        data[0x320] ^= 0x10;
        using NdsImage image = NdsImage.Load(data);

        NdsDiagnostic diagnostic = Assert.Single(
            image.Validate().Diagnostics,
            static value => value.Code == "NDS1301");

        Assert.Contains("Banner CRC slot 0", diagnostic.Message, StringComparison.Ordinal);
    }
}
