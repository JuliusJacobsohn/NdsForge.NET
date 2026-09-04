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
        int lateDsExtensions = 0;
        int programParameterTables = 0;
        int compressedPrograms = 0;
        int compressedOverlays = 0;
        int authenticatedOverlays = 0;
        int unavailableReferenceExtractions = 0;
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            unavailableReferenceExtractions += CorpusExpectations.Read(entry).Operations.Single(
                static operation => operation.Name == "extract-all").ExitCode == 0 ? 0 : 1;
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
            lateDsExtensions += image.Header.DsExtended is not null ? 1 : 0;
            programParameterTables += image.Header.Arm9.Parameters is not null ? 1 : 0;
            compressedPrograms += image.Header.Arm9.Parameters?.IsCompressed == true ? 1 : 0;
            compressedOverlays += image.Arm9Overlays.Concat(image.Arm7Overlays).Count(static overlay => overlay.IsCompressed);
            authenticatedOverlays += image.Arm9Overlays.Concat(image.Arm7Overlays).Count(static overlay => overlay.IsAuthenticated);
        }

        Assert.Equal(133, dsImages);
        Assert.Equal(9, dsiImages);
        Assert.Equal(9, animatedBanners);
        Assert.Equal(120, arm9OverlayImages);
        Assert.Equal(0, arm7OverlayImages); // Known gap: add an exact legal fixture before claiming real-image ARM7 coverage.
        Assert.Equal(1716, mismatchedOverlayIds);
        Assert.Equal(3208, highByteNames);
        Assert.Equal(9074, unnamedAllocations);
        Assert.Equal(133, sdkFooters);
        Assert.Equal(67, lateDsExtensions);
        Assert.Equal(141, programParameterTables);
        Assert.Equal(58, compressedPrograms);
        Assert.Equal(4945, compressedOverlays);
        Assert.Equal(2985, authenticatedOverlays);
        Assert.Equal(1, unavailableReferenceExtractions);
    }
}
