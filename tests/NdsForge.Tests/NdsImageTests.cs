namespace NdsForge.Tests;

public sealed class NdsImageTests
{
    [Fact]
    public void LoadParsesHeaderAndPrograms()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());

        Assert.Equal("TEST IMAGE", image.Header.Title);
        Assert.Equal("TEST", image.Header.GameCode);
        Assert.Equal("01", image.Header.MakerCode);
        Assert.Equal(NdsImageKind.NintendoDs, image.Header.Kind);
        Assert.Equal(128 * 1024, image.Header.DeviceCapacityBytes);
        Assert.Equal(new NdsRegion(0x200, 4), image.Header.Arm9.Data);
        Assert.Equal(0x02000000u, image.Header.Arm9.EntryAddress);
        Assert.Equal(new NdsRegion(0x204, 4), image.Header.Arm7.Data);
    }

    [Fact]
    public void ValidateAcceptsConsistentHeader()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());

        NdsValidationResult result = image.Validate();

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ValidateReportsHeaderCrcMismatch()
    {
        byte[] data = SyntheticImage.CreateHeaderOnly();
        data[0] ^= 0x01;
        using NdsImage image = NdsImage.Load(data);

        NdsValidationResult result = image.Validate();

        NdsDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("NDS1001", diagnostic.Code);
        Assert.Equal(NdsDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task OpenAsyncReadsHeaderWithoutLoadingWholeImage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ndsforge-{Guid.NewGuid():N}.nds");
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            await File.WriteAllBytesAsync(path, SyntheticImage.CreateHeaderOnly(), cancellationToken).ConfigureAwait(true);

            using NdsImage image = await NdsImage.OpenAsync(
                path,
                cancellationToken: cancellationToken).ConfigureAwait(true);

            Assert.Equal("TEST", image.Header.GameCode);
            Assert.Equal(0x4000, image.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
