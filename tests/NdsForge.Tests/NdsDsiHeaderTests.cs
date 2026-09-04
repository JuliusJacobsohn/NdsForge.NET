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
        Assert.Equal(NdsDsiCryptoPolicy.HasDsiRegion |
            NdsDsiCryptoPolicy.UsesModcrypt, image.Header.DsiCryptoPolicy &
            (NdsDsiCryptoPolicy)0x0F);
        Assert.Equal(0xA0, image.Header.UnknownDsiFlagBits);
        Assert.Equal(NdsDsiLaunchPolicy.Jump | NdsDsiLaunchPolicy.TemporaryJump, image.Header.DsiLaunchPolicy &
            (NdsDsiLaunchPolicy)0x03);
        Assert.Equal(0xF0, image.Header.UnknownDsiLaunchBits);
        Assert.Equal(0x11223344u, dsi.RegionFlags);
        Assert.Equal(
            NdsDsiRegionPermissions.Europe,
            dsi.Regions & (NdsDsiRegionPermissions)0x3F);
        Assert.Equal(0x11223340u, dsi.UnknownRegionFlagBits);
        Assert.Equal(0x55667788u, dsi.AccessControl);
        Assert.Equal(0x55660000u, dsi.UnknownAccessControlBits);
        Assert.Equal(0x99AABBCCu, dsi.ScfgExtMask);
        Assert.Equal(0x5A, dsi.ApplicationFlags);
        Assert.Equal(NdsDsiApplicationFeatures.RequiresEula |
            NdsDsiApplicationFeatures.ShowsNetworkIcon |
            NdsDsiApplicationFeatures.ShowsWirelessIcon |
            NdsDsiApplicationFeatures.AuthenticatesPrograms, dsi.ApplicationFeatures);
        Assert.Equal(Enumerable.Range(0, 0x30).Select(static index => (byte)(index * 3 + 1)),
            dsi.MemoryBanks.RawData.ToArray());
        Assert.Equal(0x0A070401u, dsi.MemoryBanks.GlobalBanks[0]);
        Assert.Equal(0x8B8885u, dsi.MemoryBanks.Bank9WriteProtection);
        Assert.Equal(0x8E, dsi.MemoryBanks.WramControl);
        Assert.Equal([1, 2, 3, 4, 5, 6], dsi.SharedDataFileSizes);
        Assert.Equal(7, dsi.EulaVersion);
        Assert.Equal(0x81, dsi.AgeRatingsUsage);
        Assert.True(dsi.UsesAgeRatings);
        NdsDsiAgeRating esrb = dsi.Ratings[(int)NdsDsiAgeRatingAuthority.Esrb];
        Assert.Equal(0xEA, esrb.RawValue);
        Assert.Equal(10, esrb.MinimumAge);
        Assert.True(esrb.HasReservedBit);
        Assert.True(esrb.IsProhibitedOrPending);
        Assert.True(esrb.IsEnabled);
        Assert.Equal(0x89ABCDEF01234567ul, dsi.TitleId);
        Assert.Equal(0x10000u, dsi.PublicSaveSize);
        Assert.Equal(0x20000u, dsi.PrivateSaveSize);
        Assert.Equal(0xA5, dsi.RsaSignature.Span[0]);
        Assert.Equal(data.AsSpan(0x180, 0xE80), dsi.RawData.Span);
        Assert.Equal(new NdsRegion(0x1100, 0x80), image.Header.Arm9i?.Data);
        Assert.Equal(0x02E00000u, image.Header.Arm9i?.LoadAddress);
    }
}
