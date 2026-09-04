namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Checks declared extents independently from common used size and nominal cartridge capacity.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusSizeInfoTests
{
    [Theory]
    [InlineData("9A3F3001DEE8ACFFDFB705EF89B36DFAEB9D6EDCFB47CBB067F13375719BF1C9", 16777216L, 10590240u, 10590376L)]
    [InlineData("0B3C6C9F0287880249F04B032E4DA0CCDE1CE9E11CDF6BCF2FE77344A585CB5B", 33554432L, 32004608u, 32838656L)]
    [Trait("CorpusTier", "Full")]
    public async Task KnownPostUsedContentIsIncluded(string identity, long physical, uint commonUsed, long declaredEnd)
    {
        CorpusExpectationIndexEntry entry = CorpusExpectations.Entries.Single(item => item.RomSha256.Equals(identity, StringComparison.OrdinalIgnoreCase));
        using NdsImage image = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry),
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(physical, image.SizeInfo.PhysicalSize);
        Assert.Equal(commonUsed, image.SizeInfo.CommonUsedSize);
        Assert.Equal(declaredEnd, image.SizeInfo.DeclaredContentEnd);
        Assert.Equal(new NdsRegion(declaredEnd, physical - declaredEnd), image.SizeInfo.TrailingData);
    }

    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task AllCartridgeExtentsRetainDeclaredProgramsAllocationsAndTrailers()
    {
        int count = 0;
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            NdsImageSizeInfo sizes = image.SizeInfo;
            Assert.True(sizes.DeclaredContentEnd <= image.Length);
            Assert.True(sizes.DeclaredContentEnd >= image.Header.UsedImageSize);
            Assert.True(sizes.DeclaredContentEnd >= image.Header.Arm9.CompleteData.End);
            Assert.True(sizes.DeclaredContentEnd >= image.Header.Arm7.CompleteData.End);
            Assert.All(image.FileSystem.Allocations, allocation => Assert.True(allocation.Data.End <= sizes.DeclaredContentEnd));
            if (image.Header.Dsi is { } dsi)
            {
                Assert.True(sizes.DeclaredContentEnd >= dsi.TotalImageSize);
                Assert.True(sizes.DeclaredContentEnd >= image.Header.Arm9i!.Data.End);
                Assert.True(sizes.DeclaredContentEnd >= image.Header.Arm7i!.Data.End);
            }
            if (image.DownloadPlaySignatureRegion is { } trailer) { Assert.True(sizes.DeclaredContentEnd >= trailer.End); }
            Assert.DoesNotContain(sizes.Diagnostics, static item => item.Severity == NdsDiagnosticSeverity.Error);
            count++;
        }
        Assert.Equal(142, count);
    }
}
