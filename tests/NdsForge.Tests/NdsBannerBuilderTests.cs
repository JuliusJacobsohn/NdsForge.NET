namespace NdsForge.Tests;

public sealed class NdsBannerBuilderTests
{
    [Fact]
    public void BuildsDeterministicChecksummedIndexedBanner()
    {
        var indices = new byte[32 * 32];
        indices[0] = 1;
        var palette = new ushort[16];
        palette[1] = 0x03E0;

        NdsBanner banner = new NdsBannerBuilder()
            .SetTitle(NdsBannerLanguage.English, "Built in C#")
            .SetIndexedIcon(indices, palette)
            .Build();

        Assert.Equal("Built in C#", banner.Titles[NdsBannerLanguage.English]);
        Assert.Equal([0, 255, 0, 255], banner.RenderIconRgba32()[..4]);
        Assert.Empty(banner.ValidateCrcs(0));
        Assert.Equal(banner.RawData.ToArray(), new NdsBannerBuilder()
            .SetTitle(NdsBannerLanguage.English, "Built in C#")
            .SetIndexedIcon(indices, palette)
            .Build()
            .RawData.ToArray());
    }
}
