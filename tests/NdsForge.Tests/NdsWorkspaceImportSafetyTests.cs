using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsWorkspaceImportSafetyTests
{
    [Theory]
    [InlineData(NdsWorkspaceAssetKind.Header)]
    [InlineData(NdsWorkspaceAssetKind.FileNameTable)]
    [InlineData(NdsWorkspaceAssetKind.FileAllocationTable)]
    [InlineData(NdsWorkspaceAssetKind.Arm9OverlayTable)]
    [InlineData(NdsWorkspaceAssetKind.Arm7OverlayTable)]
    [InlineData(NdsWorkspaceAssetKind.SectorHashTable)]
    [InlineData(NdsWorkspaceAssetKind.BlockHashTable)]
    public async Task LayoutOwnedAssetChangesAreRejected(NdsWorkspaceAssetKind kind)
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync(await NdsWorkspaceImportTests.CreateBuilder(true)
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        await NdsWorkspaceImportTests.ChangeAsync(fixture, recipe, kind, [1]);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageWorkspace.ImportAsync(
            fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Contains("does not accept edits", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("original-limit")]
    [InlineData("edited-limit")]
    [InlineData("aggregate-limit")]
    [InlineData("missing")]
    [InlineData("snapshot")]
    [InlineData("link")]
    public async Task InvalidInputsCannotReturnAPartialBuilder(string mode)
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync();
        NdsWorkspaceAsset allocation = recipe.Assets.Single(asset => asset.Kind == NdsWorkspaceAssetKind.Allocation);
        string path = Path.Combine(fixture.Workspace, allocation.Path);
        var options = new NdsWorkspaceImportOptions();
        switch (mode)
        {
            case "original-limit": options = options with { MaximumAssetBytes = 1 }; break;
            case "edited-limit":
                await File.WriteAllBytesAsync(path, new byte[9000], TestContext.Current.CancellationToken);
                options = options with { MaximumAssetBytes = 8192 };
                break;
            case "aggregate-limit": options = options with { MaximumTotalAssetBytes = recipe.Assets.Sum(asset => asset.OriginalLength) - 1 }; break;
            case "missing": File.Delete(path); break;
            case "snapshot":
                byte[] original = SyntheticImage.CreateWithBanner();
                original[^1] ^= 1;
                await File.WriteAllBytesAsync(Path.Combine(fixture.Workspace, recipe.SourceImagePath), original, TestContext.Current.CancellationToken);
                break;
            case "link":
                string other = Path.Combine(fixture.Root, "other.bin");
                File.Move(path, other);
                File.CreateSymbolicLink(path, other);
                break;
        }
        if (mode is "missing" or "link")
        {
            await Assert.ThrowsAnyAsync<IOException>(async () => await NdsImageWorkspace.ImportAsync(
                fixture.Workspace, options, TestContext.Current.CancellationToken).ConfigureAwait(true));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageWorkspace.ImportAsync(
                fixture.Workspace, options, TestContext.Current.CancellationToken).ConfigureAwait(true));
        }
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(-1, 1000)]
    [InlineData(int.MaxValue, 1000)]
    [InlineData(1000, 0)]
    [InlineData(1000, -1)]
    public async Task InvalidLimitsFailBeforeWorkspaceAccess(int assetBytes, long totalBytes)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await NdsImageWorkspace.ImportAsync("does-not-exist",
            new() { MaximumAssetBytes = assetBytes, MaximumTotalAssetBytes = totalBytes },
            TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task ExactLimitsSucceedAndPreCancellationDoesNotReadInputs()
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync();
        _ = await NdsImageWorkspace.ImportAsync(fixture.Workspace, new()
        {
            MaximumAssetBytes = checked((int)recipe.Assets.Max(asset => asset.OriginalLength)),
            MaximumTotalAssetBytes = recipe.Assets.Sum(asset => asset.OriginalLength),
        }, TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await NdsImageWorkspace.ImportAsync(
            "does-not-exist", cancellationToken: cancelled.Token).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrepresentedAllocationRelationshipsAreRejectedButRemainExactlyPackable(bool sharedPrivate)
    {
        byte[] bytes;
        if (sharedPrivate)
        {
            NdsImageBuilder builder = NdsWorkspaceImportTests.CreateBuilder();
            builder.AddOverlay(new(NdsProcessor.Arm9, 0, [1], 0x02002000, 1));
            builder.AddOverlay(new(NdsProcessor.Arm9, 1, [2], 0x02003000, 1));
            bytes = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
            int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x50)));
            bytes.AsSpan(offset + 24, 4).CopyTo(bytes.AsSpan(offset + 56));
        }
        else
        {
            bytes = SyntheticImage.CreateHeaderOnly();
            bytes.AsSpan(0x220, 8).CopyTo(bytes.AsSpan(0x300));
            bytes.AsSpan(0x220, 8).CopyTo(bytes.AsSpan(0x308));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x48), 0x300);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x4C), 16);
        }
        using var fixture = new WorkspaceFixture();
        await fixture.ExportAsync(bytes);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageWorkspace.ImportAsync(
            fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Contains("exact packing preserves", error.Message, StringComparison.Ordinal);
        await NdsImageWorkspace.PackFileAsync(fixture.Workspace, fixture.Output, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MalformedCarrierCannotBeImportedStructurally()
    {
        using var fixture = new WorkspaceFixture();
        await fixture.ExportAsync(SyntheticImage.CreateDsiEnhanced());
        await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageWorkspace.ImportAsync(
            fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NdsWorkspaceAssetKind.PostHeader, 0)]
    [InlineData(NdsWorkspaceAssetKind.PostHeader, 12)]
    [InlineData(NdsWorkspaceAssetKind.TwlReservation, 0)]
    [InlineData(NdsWorkspaceAssetKind.TwlReservation, 12)]
    public async Task CarrierReservationTruncationNeverImplicitlyRequestsGeneratedBytes(NdsWorkspaceAssetKind kind, int length)
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync(await NdsWorkspaceImportTests.CreateBuilder(true)
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        await NdsWorkspaceImportTests.ChangeAsync(fixture, recipe, kind, new byte[length]);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageWorkspace.ImportAsync(
            fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Contains("reserved length", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TruncatedArm9FooterIsRejected()
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync(SyntheticImage.CreateWithArm9Footer());
        await NdsWorkspaceImportTests.ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.Arm9, [1]);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageWorkspace.ImportAsync(
            fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Contains("shorter", error.Message, StringComparison.Ordinal);
    }
}
