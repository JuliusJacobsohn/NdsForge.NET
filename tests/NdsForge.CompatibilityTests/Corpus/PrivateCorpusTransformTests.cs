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
        ExpectedOperation creation = expectation.Operations.Single(static item => item.Name == "create-binary");
        ExpectedArtifact? oracle = creation.ExitCode == 0
            ? creation.Artifacts.Single(static item => item.Path == "rebuilt-binary.nds")
            : null;
        string output = Path.Combine(Path.GetTempPath(), $"ndsforge-corpus-build-{Guid.NewGuid():N}.nds");
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            using NdsImage image = await NdsImage.OpenAsync(path, cancellationToken: cancellationToken).ConfigureAwait(true);
            NdsValidationResult sourceValidation = image.Validate();
            NdsImageManifest sourceManifest = await image.CreateManifestAsync(cancellationToken).ConfigureAwait(true);
            NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(image, cancellationToken).ConfigureAwait(true);
            if (builder.DsMetadata is not null) { builder.DsMetadata.Integrity = NdsDsIntegrityOptions.PreserveStored; }
            Assert.Equal(image.Header.NormalCardControl, builder.NormalCardControl);
            Assert.Equal(image.Header.SecureCardControl, builder.SecureCardControl);
            NdsImageBuildProfile profile = expectation.Rom.Kind == NdsImageKind.NintendoDs
                ? NdsImageBuildProfile.Ndstool1503
                : NdsImageBuildProfile.Deterministic;
            if (image.Arm9OverlayAuthentication is { State: NdsOverlayAuthenticationTableState.MissingTablePointer })
            {
                Assert.Contains(sourceValidation.Diagnostics, static diagnostic => diagnostic.Code == "NDS1211");
                Assert.False(builder.Arm9OverlayAuthentication!.CanRegenerate);
                foreach (NdsImageBuildProfile rejectedProfile in new[] { profile, NdsImageBuildProfile.Deterministic })
                {
                    InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                        await builder.WriteAsync(output, new NdsImageBuildOptions { Profile = rejectedProfile }, cancellationToken)
                            .ConfigureAwait(true)).ConfigureAwait(true);
                    Assert.Contains("authentication", error.Message, StringComparison.OrdinalIgnoreCase);
                    Assert.False(File.Exists(output));
                }
                return;
            }

            await builder.WriteAsync(
                output,
                new NdsImageBuildOptions { Profile = profile, VerifyOutput = sourceValidation.IsValid },
                cancellationToken).ConfigureAwait(true);
            await using FileStream stream = File.OpenRead(output);
            using NdsImage rebuilt = await NdsImage.OpenAsync(stream, leaveOpen: true, cancellationToken: cancellationToken).ConfigureAwait(true);
            if (profile == NdsImageBuildProfile.Ndstool1503 && oracle is not null)
            {
                AssertCompatiblePhysicalExtent(oracle.Length, rebuilt);
            }
            AssertIntroducesNoValidationErrors(sourceValidation, rebuilt.Validate());
            Assert.Equal(image.CarrierLayout.Kind, rebuilt.CarrierLayout.Kind);
            Assert.Equal(image.CarrierLayout.PostHeaderData.ToArray(), rebuilt.CarrierLayout.PostHeaderData.ToArray());
            NdsImageManifest rebuiltManifest = await rebuilt.CreateManifestAsync(cancellationToken).ConfigureAwait(true);
            AssertSemanticManifestEquality(sourceManifest, rebuiltManifest, profile);
        }
        finally
        {
            File.Delete(output);
        }
    }

    /// <summary>Allows only the exact padding needed to keep a final empty FAT entry inside the output image.</summary>
    private static void AssertCompatiblePhysicalExtent(long expectedLength, NdsImage rebuilt)
    {
        long contentExtent = rebuilt.DownloadPlaySignatureRegion?.Offset ?? rebuilt.Length;
        if (rebuilt.DownloadPlaySignatureRegion is NdsRegion signature)
        {
            Assert.Equal(signature.End, rebuilt.Length);
        }

        if (expectedLength == contentExtent)
        {
            return;
        }

        long lastPayloadEnd = rebuilt.FileSystem.Allocations.Where(static item => item.Data.Length != 0)
            .Max(static item => item.Data.End);
        long lastEmptyOffset = rebuilt.FileSystem.Allocations.Where(static item => item.Data.Length == 0)
            .Max(static item => item.Data.Offset);
        Assert.Equal(expectedLength, lastPayloadEnd);
        Assert.Equal((expectedLength + 0x1FF) & ~0x1FFL, lastEmptyOffset);
        Assert.Equal(lastEmptyOffset, contentExtent);
        Assert.Equal(contentExtent, rebuilt.Header.UsedImageSize);
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
                new NdsWriteOptions { VerifyOutput = verifyOutput, DsIntegrity = image.Header.DsExtended is null ? null : NdsDsIntegrityOptions.PreserveStored },
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
                source.Header.Version, source.Header.RegionCode, source.Header.DsiFlags, source.Header.AutoStart,
                source.Header.DebugRomSize, source.Header.DebugLoadAddress, source.Header.DebugRomSha256),
            (rebuilt.Header.Title, rebuilt.Header.GameCode, rebuilt.Header.MakerCode, rebuilt.Header.Kind,
                rebuilt.Header.Version, rebuilt.Header.RegionCode, rebuilt.Header.DsiFlags, rebuilt.Header.AutoStart,
                rebuilt.Header.DebugRomSize, rebuilt.Header.DebugLoadAddress, rebuilt.Header.DebugRomSha256));
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
                    source.Dsi.ScfgExtMask, source.Dsi.ApplicationFlags, source.Dsi.EulaVersion,
                    source.Dsi.AgeRatingsUsage, source.Dsi.MemoryBankSettingsHex,
                    source.Dsi.SharedDataFileSizesHex, source.Dsi.AgeRatingsHex,
                    source.Dsi.HasModcryptAreas, source.Dsi.UsesInsecureModcryptKey,
                    source.Dsi.ModcryptArea1.Length, source.Dsi.ModcryptArea2.Length),
                (rebuilt.Dsi.TitleId, rebuilt.Dsi.RegionFlags, rebuilt.Dsi.AccessControl,
                    rebuilt.Dsi.ScfgExtMask, rebuilt.Dsi.ApplicationFlags, rebuilt.Dsi.EulaVersion,
                    rebuilt.Dsi.AgeRatingsUsage, rebuilt.Dsi.MemoryBankSettingsHex,
                    rebuilt.Dsi.SharedDataFileSizesHex, rebuilt.Dsi.AgeRatingsHex,
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
