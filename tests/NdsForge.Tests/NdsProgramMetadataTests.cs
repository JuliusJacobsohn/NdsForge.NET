namespace NdsForge.Tests;

public sealed class NdsProgramMetadataTests
{
    [Fact]
    public void ParsesLateDsHeaderFooterAndProgramParameters()
    {
        byte[] bytes = SyntheticImage.CreateLateDsAuthenticated();
        using NdsImage image = NdsImage.Load(bytes);

        NdsDsExtendedHeader extension = Assert.IsType<NdsDsExtendedHeader>(image.Header.DsExtended);
        NdsProgramFooter footer = Assert.IsType<NdsProgramFooter>(image.Header.Arm9.FooterMetadata);
        NdsProgramParameters parameters = Assert.IsType<NdsProgramParameters>(image.Header.Arm9.Parameters);

        Assert.Equal(0x1000, image.Header.RawData.Length);
        Assert.Equal(NdsProgramFeatures.AuthenticatesBanner | NdsProgramFeatures.AuthenticatesPrograms, extension.ProgramFeatures);
        Assert.Equal(0x1040u, extension.Arm9ParametersOffset);
        Assert.Equal(0x31, extension.BannerHmac.Span[0]);
        Assert.Equal(0x32, extension.ProgramsHmac.Span[0]);
        Assert.Equal(0x33, extension.Arm9OverlaysHmac.Span[0]);
        Assert.Equal(0x34, extension.RsaSignature.Span[0]);
        Assert.Equal(new NdsRegion(0x1100, 12), footer.Data);
        Assert.Equal(0x40u, footer.ParametersOffset);
        Assert.Equal(0x80u, footer.OverlayHmacTableOffset);
        Assert.Equal(new NdsRegion(0x1040, 0x24), parameters.Data);
        Assert.Equal(0x40u, parameters.RelativeOffset);
        Assert.Equal(0x02000080u, parameters.CompressedEndAddress);
        Assert.Equal(0x80u, parameters.CompressedLength);
        Assert.True(parameters.IsCompressed);
        Assert.Equal(new NdsSdkVersion(5, 5, 30003), parameters.SdkVersion);
        Assert.Equal("5.5.30003", parameters.SdkVersion.ToString());
        Assert.Equal(0xDEC00621u, parameters.LittleEndianMarker);
        Assert.Equal(0x2106C0DEu, parameters.BigEndianMarker);
    }

    [Fact]
    public void ClassicDsHeaderDoesNotSpeculativelyReadReservedBytes()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());

        Assert.Null(image.Header.DsExtended);
        Assert.Equal(0x200, image.Header.RawData.Length);
        Assert.Null(image.Header.Arm9.Parameters);
    }

    [Fact]
    public void RejectsAnInRangeFooterPointerWithoutCanonicalParameterMarkers()
    {
        byte[] bytes = SyntheticImage.CreateLateDsAuthenticated();
        bytes.AsSpan(0x105C, 8).Clear();

        using NdsImage image = NdsImage.Load(bytes);

        Assert.NotNull(image.Header.Arm9.FooterMetadata);
        Assert.Null(image.Header.Arm9.Parameters);
    }

    [Fact]
    public void ProjectsOverlayControlBitsWithoutDiscardingReservedFlags()
    {
        byte[] bytes = SyntheticImage.CreateWithOverlay();
        bytes[0x24F] = 0xA3;
        using NdsImage image = NdsImage.Load(bytes);

        NdsOverlay overlay = Assert.Single(image.Arm9Overlays);
        Assert.True(overlay.IsCompressed);
        Assert.True(overlay.IsAuthenticated);
        Assert.Equal(0xA0, overlay.ReservedFlags);
        Assert.Equal(0xA3, overlay.Flags);
    }
}
