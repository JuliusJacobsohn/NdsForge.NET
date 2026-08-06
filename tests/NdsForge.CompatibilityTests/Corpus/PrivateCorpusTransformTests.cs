using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Replays whole-image transformations whose byte identity can be compared without retaining output ROMs.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusTransformTests
{
    /// <summary>Proves object-graph reconstruction retains semantics and, for original DS images, ndstool's used-image extent.</summary>
    [Theory]
    [MemberData(nameof(CorpusExpectations.Cases), MemberType = typeof(CorpusExpectations))]
    [Trait("CorpusTier", "Full")]
    public async Task StructuralBinaryRebuildPreservesSemanticsAndNdstoolSize(CorpusExpectationIndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CorpusExpectation expectation = CorpusExpectations.Read(entry);
        string path = CorpusExpectations.Resolve(entry);
        ExpectedArtifact oracle = expectation.Operations.Single(static item => item.Name == "create-binary")
            .Artifacts.Single(static item => item.Path == "rebuilt-binary.nds");
        string output = Path.Combine(Path.GetTempPath(), $"ndsforge-corpus-build-{Guid.NewGuid():N}.nds");
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            using NdsImage image = await NdsImage.OpenAsync(path, cancellationToken: cancellationToken).ConfigureAwait(true);
            NdsValidationResult sourceValidation = image.Validate();
            NdsImageManifest sourceManifest = await image.CreateManifestAsync(cancellationToken).ConfigureAwait(true);
            NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(image, cancellationToken).ConfigureAwait(true);
            Assert.Equal(image.Header.NormalCardControl, builder.NormalCardControl);
            Assert.Equal(image.Header.SecureCardControl, builder.SecureCardControl);
            NdsImageBuildProfile profile = expectation.Rom.Kind == NdsImageKind.NintendoDs
                ? NdsImageBuildProfile.Ndstool1503
                : NdsImageBuildProfile.Deterministic;
            await builder.WriteAsync(
                output,
                new NdsImageBuildOptions { Profile = profile, VerifyOutput = sourceValidation.IsValid },
                cancellationToken).ConfigureAwait(true);
            await using FileStream stream = File.OpenRead(output);
            if (profile == NdsImageBuildProfile.Ndstool1503)
            {
                Assert.Equal(oracle.Length, stream.Length);
            }
            using NdsImage rebuilt = await NdsImage.OpenAsync(stream, leaveOpen: true, cancellationToken: cancellationToken).ConfigureAwait(true);
            AssertIntroducesNoValidationErrors(sourceValidation, rebuilt.Validate());
            NdsImageManifest rebuiltManifest = await rebuilt.CreateManifestAsync(cancellationToken).ConfigureAwait(true);
            AssertSemanticManifestEquality(sourceManifest, rebuiltManifest, profile);
        }
        finally
        {
            File.Delete(output);
        }
    }

    /// <summary>Verifies the legacy ARM7 trainer insertion against ndstool for every exact input image.</summary>
    [Theory]
    [MemberData(nameof(CorpusExpectations.Cases), MemberType = typeof(CorpusExpectations))]
    [Trait("CorpusTier", "Full")]
    public async Task Arm7HookOutputIsByteEqualToNdstool(CorpusExpectationIndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CorpusExpectation expectation = CorpusExpectations.Read(entry);
        string path = CorpusExpectations.Resolve(entry);
        ExpectedOperation operation = expectation.Operations.Single(static item => item.Name == "hook-arm7");
        ExpectedArtifact hookArtifact = operation.Artifacts.Single(static item => item.Path == "hook.bin");
        ExpectedArtifact imageArtifact = operation.Artifacts.Single(static item => item.Path == "hooked.nds");
        byte[] hook = [0x00, 0x00, 0xA0, 0xE1];
        Assert.Equal(hookArtifact.Sha256, Convert.ToHexString(SHA256.HashData(hook)), ignoreCase: true);

        byte[] source = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
        NdsLegacyArm7HookResult result = NdsLegacyArm7Hook.Apply(source, hook);
        Assert.Equal(imageArtifact.Length, result.Image.Length);
        Assert.Equal(imageArtifact.Sha256, Convert.ToHexString(SHA256.HashData(result.Image.Span)), ignoreCase: true);
    }

    /// <summary>Replays the oracle's one-byte CRC corruption and proves NdsForge restores the exact original image.</summary>
    [Theory]
    [MemberData(nameof(CorpusExpectations.Cases), MemberType = typeof(CorpusExpectations))]
    [Trait("CorpusTier", "Full")]
    public async Task HeaderRepairPreservesValidImageUnlikeNdstool1503(CorpusExpectationIndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CorpusExpectation expectation = CorpusExpectations.Read(entry);
        string path = CorpusExpectations.Resolve(entry);
        ExpectedArtifact ndstoolOutput = expectation.Operations
            .Single(static item => item.Name == "repair-header-crc")
            .Artifacts.Single(static item => item.Path == "repaired-header.nds");
        Assert.False(expectation.Rom.Sha256.Equals(ndstoolOutput.Sha256, StringComparison.OrdinalIgnoreCase));

        string corrupted = Path.Combine(Path.GetTempPath(), $"ndsforge-corpus-corrupt-{Guid.NewGuid():N}.nds");
        string output = Path.Combine(Path.GetTempPath(), $"ndsforge-corpus-repair-{Guid.NewGuid():N}.nds");
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            byte[] source = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(true);
            using NdsImage original = NdsImage.Load(source);
            bool verifyOutput = original.Validate().IsValid;
            source[0x15E] ^= 0xFF;
            await File.WriteAllBytesAsync(corrupted, source, cancellationToken).ConfigureAwait(true);
            Assert.False(expectation.Rom.Sha256.Equals(
                Convert.ToHexString(SHA256.HashData(source)),
                StringComparison.OrdinalIgnoreCase));

            using NdsImage image = await NdsImage.OpenAsync(corrupted, cancellationToken: cancellationToken).ConfigureAwait(true);
            Assert.Contains(image.Validate().Diagnostics, static diagnostic => diagnostic.Code == "NDS1001");
            await image.Edit().RepairHeaderCrc().SaveAsync(
                output,
                new NdsWriteOptions { VerifyOutput = verifyOutput },
                cancellationToken).ConfigureAwait(true);
            await using FileStream stream = File.OpenRead(output);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(true));
            Assert.Equal(expectation.Rom.Sha256, actual, ignoreCase: true);
        }
        finally
        {
            File.Delete(corrupted);
            File.Delete(output);
        }
    }

    /// <summary>Compares every logical field while excluding only physical offsets and capacity padding chosen by the build profile.</summary>
    private static void AssertSemanticManifestEquality(
        NdsImageManifest source,
        NdsImageManifest rebuilt,
        NdsImageBuildProfile profile)
    {
        Assert.Equal(
            (source.Header.Title, source.Header.GameCode, source.Header.MakerCode, source.Header.Kind,
                source.Header.Version, source.Header.RegionCode, source.Header.AutoStart),
            (rebuilt.Header.Title, rebuilt.Header.GameCode, rebuilt.Header.MakerCode, rebuilt.Header.Kind,
                rebuilt.Header.Version, rebuilt.Header.RegionCode, rebuilt.Header.AutoStart));
        if (profile != NdsImageBuildProfile.Ndstool1503)
        {
            Assert.Equal(
                (source.Header.NormalCardControl, source.Header.SecureCardControl),
                (rebuilt.Header.NormalCardControl, rebuilt.Header.SecureCardControl));
        }
        Assert.Equal(source.Directories, rebuilt.Directories);
        Assert.Equal(
            source.Programs.Select(static item => (item.Processor, item.Length, item.LoadAddress, item.EntryAddress, item.Sha256)),
            rebuilt.Programs.Select(static item => (item.Processor, item.Length, item.LoadAddress, item.EntryAddress, item.Sha256)));
        Assert.Equal(
            source.Files.Select(static item => (item.Path, item.Length, item.Sha256)),
            rebuilt.Files.Select(static item => (item.Path, item.Length, item.Sha256)));
        Assert.Equal(
            source.Allocations.Select(static item => (item.Length, item.Sha256)).Order(),
            rebuilt.Allocations.Select(static item => (item.Length, item.Sha256)).Order());
        Assert.Equal(
            source.Overlays.Select(static item => (item.Processor, item.OverlayId, item.FilePath,
                item.Length, item.LoadAddress, item.RamSize, item.BssSize, item.StaticInitializerStart,
                item.StaticInitializerEnd, item.CompressedSize, item.Flags, item.Sha256)),
            rebuilt.Overlays.Select(static item => (item.Processor, item.OverlayId, item.FilePath,
                item.Length, item.LoadAddress, item.RamSize, item.BssSize, item.StaticInitializerStart,
                item.StaticInitializerEnd, item.CompressedSize, item.Flags, item.Sha256)));
        AssertFileIdRelationshipsPreserved(source, rebuilt);
        Assert.Equal(source.Banner is null, rebuilt.Banner is null);
        if (source.Banner is not null && rebuilt.Banner is not null)
        {
            Assert.Equal(
                (source.Banner.Length, source.Banner.Version, source.Banner.IsAnimated, source.Banner.Sha256,
                    string.Join('\n', source.Banner.Titles.OrderBy(static item => item.Key))),
                (rebuilt.Banner.Length, rebuilt.Banner.Version, rebuilt.Banner.IsAnimated, rebuilt.Banner.Sha256,
                    string.Join('\n', rebuilt.Banner.Titles.OrderBy(static item => item.Key))));
        }

        Assert.Equal(source.Dsi is null, rebuilt.Dsi is null);
        if (source.Dsi is not null && rebuilt.Dsi is not null)
        {
            Assert.Equal(
                (source.Dsi.TitleId, source.Dsi.RegionFlags, source.Dsi.AccessControl,
                    source.Dsi.HasModcryptAreas, source.Dsi.UsesInsecureModcryptKey,
                    source.Dsi.ModcryptArea1.Length, source.Dsi.ModcryptArea2.Length),
                (rebuilt.Dsi.TitleId, rebuilt.Dsi.RegionFlags, rebuilt.Dsi.AccessControl,
                    rebuilt.Dsi.HasModcryptAreas, rebuilt.Dsi.UsesInsecureModcryptKey,
                    rebuilt.Dsi.ModcryptArea1.Length, rebuilt.Dsi.ModcryptArea2.Length));
        }
    }

    /// <summary>Allows deterministic ID reassignment only when every named and Overlay reference follows its payload.</summary>
    private static void AssertFileIdRelationshipsPreserved(NdsImageManifest source, NdsImageManifest rebuilt)
    {
        Dictionary<string, NdsManifestFile> sourceFiles = source.Files.ToDictionary(
            static file => file.Path,
            StringComparer.Ordinal);
        Dictionary<string, NdsManifestFile> rebuiltFiles = rebuilt.Files.ToDictionary(
            static file => file.Path,
            StringComparer.Ordinal);
        foreach ((NdsManifestOverlay sourceOverlay, NdsManifestOverlay rebuiltOverlay) in source.Overlays.Zip(rebuilt.Overlays))
        {
            if (sourceOverlay.FilePath is null)
            {
                Assert.Null(rebuiltOverlay.FilePath);
                continue;
            }

            Assert.Equal(sourceFiles[sourceOverlay.FilePath].FileId, checked((int)sourceOverlay.FileId));
            Assert.Equal(rebuiltFiles[sourceOverlay.FilePath].FileId, checked((int)rebuiltOverlay.FileId));
        }
    }

    /// <summary>Allows known source defects while rejecting any new error category introduced by reconstruction.</summary>
    private static void AssertIntroducesNoValidationErrors(NdsValidationResult source, NdsValidationResult rebuilt)
    {
        string[] permitted = source.Diagnostics
            .Where(static diagnostic => diagnostic.Severity == NdsDiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.All(
            rebuilt.Diagnostics.Where(static diagnostic => diagnostic.Severity == NdsDiagnosticSeverity.Error),
            diagnostic => Assert.Contains(diagnostic.Code, permitted));
    }
}
