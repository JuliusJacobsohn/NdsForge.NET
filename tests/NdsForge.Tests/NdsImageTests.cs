using System.Buffers.Binary;

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

    [Theory]
    [InlineData(0x300u, 0u)]
    [InlineData(0u, 3u)]
    [InlineData(0x3FFFu, 3u)]
    public void ValidateReportsMalformedDebugProgramRange(uint offset, uint size)
    {
        byte[] data = SyntheticImage.CreateHeaderOnly();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x160), offset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x164), size);
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(0x15E),
            NdsChecksums.ComputeCrc16(data.AsSpan(0, 0x15E)));
        using NdsImage image = NdsImage.Load(data);

        NdsValidationResult result = image.Validate();

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "NDS1109");
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

    [Fact]
    public void LoadDetectsArm9NitroFooter()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateWithArm9Footer());

        Assert.Equal(new NdsRegion(0x204, 12), image.Header.Arm9.Footer);
        Assert.Equal(new NdsRegion(0x200, 16), image.Header.Arm9.CompleteData);
    }

    [Fact]
    public void OpenReadsSeekableStreamAndHonorsOwnership()
    {
        using var stream = new MemoryStream(SyntheticImage.CreateHeaderOnly());

        using (NdsImage image = NdsImage.Open(stream, leaveOpen: true))
        {
            Assert.Equal("TEST", image.Header.GameCode);
        }

        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task OpenAsyncDisposesOwnedStream()
    {
        var stream = new MemoryStream(SyntheticImage.CreateHeaderOnly());

        using (NdsImage image = await NdsImage.OpenAsync(
            stream,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true))
        {
            Assert.Equal("TEST", image.Header.GameCode);
        }

        Assert.False(stream.CanRead);
    }
}
