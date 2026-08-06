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
            NdsImageManifest sourceManifest = await image.CreateManifestAsync(cancellationToken).ConfigureAwait(true);
            NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(image, cancellationToken).ConfigureAwait(true);
            NdsImageBuildProfile profile = expectation.Rom.Kind == NdsImageKind.NintendoDs
                ? NdsImageBuildProfile.Ndstool1503
                : NdsImageBuildProfile.Deterministic;
            await builder.WriteAsync(
                output,
                new NdsImageBuildOptions { Profile = profile, VerifyOutput = false },
                cancellationToken).ConfigureAwait(true);
            await using FileStream stream = File.OpenRead(output);
            if (profile == NdsImageBuildProfile.Ndstool1503)
            {
                Assert.Equal(oracle.Length, stream.Length);
            }
            using NdsImage rebuilt = await NdsImage.OpenAsync(stream, leaveOpen: true, cancellationToken: cancellationToken).ConfigureAwait(true);
            NdsImageManifest rebuiltManifest = await rebuilt.CreateManifestAsync(cancellationToken).ConfigureAwait(true);
            Assert.Equal(sourceManifest.Directories, rebuiltManifest.Directories);
            Assert.Equal(
                sourceManifest.Programs.Select(static item => (item.Processor, item.Length, item.LoadAddress, item.EntryAddress, item.Sha256)),
                rebuiltManifest.Programs.Select(static item => (item.Processor, item.Length, item.LoadAddress, item.EntryAddress, item.Sha256)));
            Assert.Equal(
                sourceManifest.Files.Select(static item => (item.Path, item.Length, item.Sha256)),
                rebuiltManifest.Files.Select(static item => (item.Path, item.Length, item.Sha256)));
            Assert.Equal(
                sourceManifest.Allocations.Select(static item => (item.Length, item.Sha256)).Order(),
                rebuiltManifest.Allocations.Select(static item => (item.Length, item.Sha256)).Order());
            Assert.Equal(
                sourceManifest.Overlays.Select(static item => (item.Processor, item.OverlayId, item.Sha256)),
                rebuiltManifest.Overlays.Select(static item => (item.Processor, item.OverlayId, item.Sha256)));
            Assert.Equal(sourceManifest.Banner?.Sha256, rebuiltManifest.Banner?.Sha256);
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

    /// <summary>Confirms preservation repair is a no-op for valid images and records ndstool 1.50.3's destructive oversized-header write as a divergence.</summary>
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

        string output = Path.Combine(Path.GetTempPath(), $"ndsforge-corpus-repair-{Guid.NewGuid():N}.nds");
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            using NdsImage image = await NdsImage.OpenAsync(path, cancellationToken: cancellationToken).ConfigureAwait(true);
            await image.Edit().RepairHeaderCrc().SaveAsync(
                output,
                new NdsWriteOptions { VerifyOutput = false },
                cancellationToken).ConfigureAwait(true);
            await using FileStream stream = File.OpenRead(output);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(true));
            Assert.Equal(expectation.Rom.Sha256, actual, ignoreCase: true);
        }
        finally
        {
            File.Delete(output);
        }
    }
}
