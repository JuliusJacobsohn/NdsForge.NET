using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Compares every ndstool extraction artifact with the corresponding lazy NdsForge region or NitroFS allocation.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusExtractionTests
{
    /// <summary>Guards the format rule that overlay metadata's runtime ID is independent from its FAT payload ID.</summary>
    [Theory]
    [MemberData(nameof(CorpusExpectations.Cases), MemberType = typeof(CorpusExpectations))]
    public async Task OverlayPayloadResolutionUsesFileId(CorpusExpectationIndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string path = CorpusExpectations.Resolve(entry);
        using NdsImage image = await NdsImage.OpenAsync(
            path,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        foreach (NdsOverlay overlay in image.Arm9Overlays.Concat(image.Arm7Overlays))
        {
            if (overlay.FileId < image.FileSystem.Allocations.Count)
            {
                Assert.Equal(image.FileSystem.Allocations[checked((int)overlay.FileId)].Data, overlay.Data);
            }
            else
            {
                Assert.Null(overlay.Data);
            }
        }
    }

    /// <summary>Hashes all extracted programs, tables, metadata, named files, and legacy overlay outputs without writing proprietary data.</summary>
    [Theory]
    [MemberData(nameof(CorpusExpectations.Cases), MemberType = typeof(CorpusExpectations))]
    public async Task ExtractableComponentsMatchNdstool(CorpusExpectationIndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CorpusExpectation expectation = CorpusExpectations.Read(entry);
        string path = CorpusExpectations.Resolve(entry);
        ExpectedOperation extraction = expectation.Operations.Single(static operation => operation.Name == "extract-all");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using NdsImage image = await NdsImage.OpenAsync(path, cancellationToken: cancellationToken).ConfigureAwait(true);

        IReadOnlyDictionary<string, ExpectedArtifact> actual = await CaptureNdstoolExtractionViewAsync(image, cancellationToken)
            .ConfigureAwait(true);
        ExpectedArtifact[] expected = extraction.Artifacts
            .Select(static artifact => artifact with { Path = NormalizeLegacyHostPath(artifact.Path) })
            .OrderBy(static artifact => artifact.Path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.Length, expected.Select(static artifact => artifact.Path).Distinct(StringComparer.Ordinal).Count());
        if (extraction.ExitCode == 0)
        {
            Assert.Equal(expected.Select(static artifact => artifact.Path), actual.Keys.Order(StringComparer.Ordinal));
        }
        else
        {
            Assert.NotEmpty(expected);
            Assert.True(actual.Count > expected.Length);
        }
        foreach (ExpectedArtifact artifact in expected)
        {
            Assert.True(actual.TryGetValue(artifact.Path, out ExpectedArtifact? observed), $"NdsForge did not map {artifact.Path}.");
            Assert.Equal(artifact.Length, observed.Length);
            Assert.Equal(artifact.Sha256, observed.Sha256, ignoreCase: true);
        }
    }

    /// <summary>Builds the same observable extraction namespace as ndstool, including its documented overlay-ID indexing defect.</summary>
    private static async Task<IReadOnlyDictionary<string, ExpectedArtifact>> CaptureNdstoolExtractionViewAsync(
        NdsImage image,
        CancellationToken cancellationToken)
    {
        var artifacts = new Dictionary<string, ExpectedArtifact>(StringComparer.Ordinal);
        var hashes = new Dictionary<NdsRegion, string>();
        await AddAsync("arm9.bin", image.Header.Arm9.CompleteData).ConfigureAwait(true);
        await AddAsync("arm7.bin", image.Header.Arm7.Data).ConfigureAwait(true);
        await AddAsync("arm9-overlays.bin", image.Header.Arm9OverlayTable).ConfigureAwait(true);
        await AddAsync("arm7-overlays.bin", image.Header.Arm7OverlayTable).ConfigureAwait(true);
        await AddAsync("header.bin", new(0, Math.Min(0x200, image.Header.RawData.Length))).ConfigureAwait(true);
        await AddAsync("logo.bin", new(0xC0, 156)).ConfigureAwait(true);
        if (image.Banner is not null)
        {
            await AddAsync("banner.bin", new(image.Header.BannerOffset, Math.Min(0x840, image.Banner.RawData.Length))).ConfigureAwait(true);
        }

        if (image.Header.Arm9i is not null)
        {
            await AddAsync("arm9i.bin", image.Header.Arm9i.Data).ConfigureAwait(true);
        }

        if (image.Header.Arm7i is not null)
        {
            await AddAsync("arm7i.bin", image.Header.Arm7i.Data).ConfigureAwait(true);
        }

        foreach (NdsFile file in image.FileSystem.Files)
        {
            await AddAsync(NormalizeLegacyHostPath("data/" + file.FullPath.TrimStart('/')), file.Data).ConfigureAwait(true);
        }

        foreach (NdsOverlay overlay in image.Arm9Overlays.Concat(image.Arm7Overlays))
        {
            if (overlay.Id < image.FileSystem.Allocations.Count)
            {
                NdsRegion region = image.FileSystem.Allocations[checked((int)overlay.Id)].Data;
                await AddAsync(FormattableString.Invariant($"overlays/overlay_{overlay.Id:D4}.bin"), region).ConfigureAwait(true);
            }
        }

        return artifacts;

        async Task AddAsync(string artifactPath, NdsRegion region)
        {
            if (!hashes.TryGetValue(region, out string? hash))
            {
                using Stream stream = image.OpenRead(region);
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(true));
                hashes.Add(region, hash);
            }

            artifacts[artifactPath] = new(artifactPath, region.Length, hash);
        }
    }

    /// <summary>Models Windows ndstool 1.50.3's ANSI display glyphs while NdsForge retains their original eight-bit values.</summary>
    private static string NormalizeLegacyHostPath(string path) => new(path.Select(static character => character > 0x7F ? '?' : character).ToArray());
}
