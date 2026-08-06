namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Guards the real-image corpus against accidentally shrinking to many examples of the same simple layout.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusCoverageTests
{
    /// <summary>Freezes the reviewed DS/DSi mix and makes both represented features and known corpus absences explicit.</summary>
    [Fact]
    public async Task CorpusRetainsItsRequiredFeatureDiversity()
    {
        int dsImages = 0;
        int dsiImages = 0;
        int animatedBanners = 0;
        int arm9OverlayImages = 0;
        int arm7OverlayImages = 0;
        int mismatchedOverlayIds = 0;
        int highByteNames = 0;
        int unnamedAllocations = 0;
        int sdkFooters = 0;
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            dsImages += image.Header.Kind == NdsImageKind.NintendoDs ? 1 : 0;
            dsiImages += image.Header.Kind == NdsImageKind.NintendoDsiEnhanced ? 1 : 0;
            animatedBanners += image.Banner?.IsAnimated == true ? 1 : 0;
            arm9OverlayImages += image.Arm9Overlays.Count > 0 ? 1 : 0;
            arm7OverlayImages += image.Arm7Overlays.Count > 0 ? 1 : 0;
            mismatchedOverlayIds += image.Arm9Overlays.Concat(image.Arm7Overlays).Count(
                static overlay => overlay.Id != overlay.FileId);
            highByteNames += image.FileSystem.Files.Count(
                static file => file.FullPath.Any(static character => character > 0x7F));
            unnamedAllocations += image.FileSystem.Allocations.Count - image.FileSystem.Files.Count;
            sdkFooters += image.Header.Arm9.Footer is not null ? 1 : 0;
        }

        Assert.Equal(51, dsImages);
        Assert.Equal(6, dsiImages);
        Assert.Equal(6, animatedBanners);
        Assert.Equal(51, arm9OverlayImages);
        Assert.Equal(0, arm7OverlayImages); // Known gap: add an exact legal fixture before claiming real-image ARM7 coverage.
        Assert.Equal(855, mismatchedOverlayIds);
        Assert.Equal(1, highByteNames);
        Assert.Equal(5209, unnamedAllocations);
        Assert.Equal(51, sdkFooters);
    }
}
