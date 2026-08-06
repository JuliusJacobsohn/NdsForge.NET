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

    [Fact]
    public void BuildsTypedAnimatedDsiBannerWithAllCrcs()
    {
        var staticIndices = new byte[32 * 32];
        staticIndices[0] = 1;
        var animatedIndices = new byte[32 * 32];
        animatedIndices[0] = 2;
        var staticPalette = new ushort[16];
        staticPalette[1] = 0x001F;
        var animatedPalette = new ushort[16];
        animatedPalette[2] = 0x7C00;
        var step = new NdsBannerAnimationStep(12, 3, 5, FlipHorizontal: true, FlipVertical: true);

        NdsBanner banner = new NdsBannerBuilder(0x0103)
            .SetTitle(NdsBannerLanguage.Chinese, "动画")
            .SetIndexedIcon(staticIndices, staticPalette)
            .SetAnimatedFrame(3, animatedIndices, new ushort[16])
            .SetAnimatedFrame(5, new byte[32 * 32], animatedPalette)
            .SetAnimationSequence([step])
            .Build();

        Assert.True(banner.IsAnimated);
        Assert.Equal(0x23C0, banner.RawData.Length);
        Assert.Equal("动画", banner.Titles[NdsBannerLanguage.Chinese]);
        Assert.Equal([0, 0, 255, 255], banner.RenderAnimatedIconRgba32(3, 5)[..4]);
        Assert.Equal([0, 0, 255, 255], banner.RenderAnimationStepRgba32(step)[^4..]);
        Assert.Equal(step.Pack(), banner.GetAnimationSequence()[0]);
        Assert.Equal(0, banner.GetAnimationSequence()[1]);
        Assert.Equal([step], banner.GetAnimationSteps());
        Assert.Empty(banner.ValidateCrcs(0));
    }

    [Fact]
    public void AnimationStepRoundTripsEveryBitfield()
    {
        var step = new NdsBannerAnimationStep(255, 7, 6, FlipHorizontal: true, FlipVertical: true);

        Assert.Equal(step, NdsBannerAnimationStep.FromPacked(step.Pack()));
        Assert.Throws<InvalidDataException>(() => new NdsBannerAnimationStep(0, 0, 0).Pack());
        Assert.Throws<InvalidDataException>(() => new NdsBannerAnimationStep(1, 8, 0).Pack());
        Assert.Throws<InvalidDataException>(() => NdsBannerAnimationStep.FromPacked(0));
    }

    [Fact]
    public void StaticBannerRejectsAnimationWithoutChangingItsOutput()
    {
        var builder = new NdsBannerBuilder();

        Assert.Throws<InvalidOperationException>(() =>
            builder.SetAnimationSequence([new NdsBannerAnimationStep(1, 0, 0)]));
        Assert.Equal(0x840, builder.Build().RawData.Length);
    }
}
