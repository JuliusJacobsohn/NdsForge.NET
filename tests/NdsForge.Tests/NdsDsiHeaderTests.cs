namespace NdsForge.Tests;

public sealed class NdsDsiHeaderTests
{
    [Fact]
    public void ParsesExtendedHeaderWithoutDiscardingRawData()
    {
        byte[] data = SyntheticImage.CreateDsiEnhanced();
        using NdsImage image = NdsImage.Load(data);

        NdsDsiHeader dsi = Assert.IsType<NdsDsiHeader>(image.Header.Dsi);

        Assert.Equal(NdsImageKind.NintendoDsiEnhanced, image.Header.Kind);
        Assert.Equal(0x11223344u, dsi.RegionFlags);
        Assert.Equal(0x55667788u, dsi.AccessControl);
        Assert.Equal(0x99AABBCCu, dsi.ScfgExtMask);
        Assert.Equal(0x5A, dsi.ApplicationFlags);
        Assert.Equal(0x89ABCDEF01234567ul, dsi.TitleId);
        Assert.Equal(0x10000u, dsi.PublicSaveSize);
        Assert.Equal(0x20000u, dsi.PrivateSaveSize);
        Assert.Equal(0xA5, dsi.RsaSignature.Span[0]);
        Assert.Equal(data.AsSpan(0x180, 0xE80), dsi.RawData.Span);
        Assert.Equal(new NdsRegion(0x1100, 0x80), image.Header.Arm9i?.Data);
        Assert.Equal(0x02E00000u, image.Header.Arm9i?.LoadAddress);
    }
}
