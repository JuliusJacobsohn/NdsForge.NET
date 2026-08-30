using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NdsForge.Tests;

public sealed class NdsWorkspaceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task CompleteWorkspacePacksExactlyAndRecipeIsDeterministic(int kind)
    {
        byte[] original = kind switch
        {
            0 => SyntheticImage.CreateHeaderOnly(),
            1 => SyntheticImage.CreateWithBanner(),
            2 => SyntheticImage.CreateWithArm9Footer(),
            3 => SyntheticImage.CreateWithOverlayAuthentication(),
            4 => SyntheticImage.CreateDsiEnhanced(),
            _ => SyntheticImage.CreateLateDsAuthenticated(),
        };
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe exported = await fixture.ExportAsync(original);
        NdsWorkspaceRecipe parsed = await NdsImageWorkspace.ReadRecipeAsync(fixture.Workspace, TestContext.Current.CancellationToken);
        Assert.Equal(exported.ToJson(), parsed.ToJson());
        Assert.Equal(exported.ToJson(indented: false), NdsWorkspaceRecipe.ParseJson(exported.ToJson()).ToJson(indented: false));
        Assert.Equal(exported.Assets.Count + 2, Directory.GetFiles(fixture.Workspace, "*", SearchOption.AllDirectories).Length);
        foreach (NdsWorkspaceAsset asset in exported.Assets)
        {
            byte[] actual = await File.ReadAllBytesAsync(Path.Combine(fixture.Workspace, asset.Path), TestContext.Current.CancellationToken);
            Assert.Equal(original.AsSpan(checked((int)asset.OriginalOffset), checked((int)asset.OriginalLength)).ToArray(), actual);
            Assert.Equal(asset.OriginalSha256, Convert.ToHexStringLower(SHA256.HashData(actual)));
        }
        NdsWorkspaceRecipe packed = await NdsImageWorkspace.PackFileAsync(fixture.Workspace, fixture.Output, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(exported.SourceInventory.ImageSha256, packed.SourceInventory.ImageSha256);
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken));
        using NdsImage reopened = await NdsImage.OpenAsync(fixture.Output, cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage input = NdsImage.Load(original);
        Assert.Equal(input.Validate().Diagnostics, reopened.Validate().Diagnostics);
        if (kind == 2) { Assert.Equal(16, exported.Assets.Single(static asset => asset.Kind == NdsWorkspaceAssetKind.Arm9).OriginalLength); }
        if (kind == 4) { Assert.Contains(exported.Assets, static asset => asset.Kind == NdsWorkspaceAssetKind.SectorHashTable); }
    }

    [Fact]
    public async Task UnnamedSharedEmptyAndNonportableNamedAllocationsRemainIndependentAssets()
    {
        byte[] original = SyntheticImage.CreateWithOverlay();
        "CON.x.bin"u8.CopyTo(original.AsSpan(0x211));
        original.AsSpan(0x220, 8).CopyTo(original.AsSpan(0x300));
        original.AsSpan(0x220, 8).CopyTo(original.AsSpan(0x308));
        Write(0x48, 0x300);
        Write(0x4C, 32);
        Write(0x310, 0x3F00);
        Write(0x314, 0x3F00);
        Write(0x318, 0x350);
        Write(0x31C, 0x355);
        "extra"u8.CopyTo(original.AsSpan(0x350));
        original[0x3999] = 0xA5;
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync(original);
        Assert.Equal("/CON.x.bin", Assert.Single(recipe.SourceInventory.Files).Path);
        NdsWorkspaceAsset[] allocations = recipe.Assets.Where(static asset => asset.Kind == NdsWorkspaceAssetKind.Allocation).ToArray();
        Assert.Equal(4, allocations.Length);
        Assert.Equal([0, 1, 2, 3], allocations.Select(static asset => asset.FileId!.Value));
        Assert.Equal(allocations[0].OriginalSha256, allocations[1].OriginalSha256);
        Assert.NotEqual(allocations[0].Path, allocations[1].Path);
        Assert.Equal(0, allocations[2].OriginalLength);
        Assert.Equal(0x3F00, allocations[2].OriginalOffset);
        Assert.Equal([0U], recipe.SourceInventory.Overlays.Select(static overlay => overlay.FileId));
        await NdsImageWorkspace.PackFileAsync(fixture.Workspace, fixture.Output, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken));

        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(offset), value);
    }

    [Fact]
    public async Task DebugTrailerAndOpaquePaddingAreCapturedWithoutRepairingOriginalFindings()
    {
        byte[] original = SyntheticImage.CreateHeaderOnly();
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(0x160), 0x500);
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(0x164), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(0x168), 0x80001234);
        "debug!!"u8.CopyTo(original.AsSpan(0x500));
        new byte[] { 0x61, 0x63, 1, 0 }.CopyTo(original, 0x22D);
        original.AsSpan(0x231, 132).Fill(0xA9);
        original[^1] = 0xA5;
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync(original);
        Assert.Contains(recipe.Assets, static asset => asset.Kind == NdsWorkspaceAssetKind.DebugProgram && asset.OriginalLength == 7);
        Assert.Contains(recipe.Assets, static asset => asset.Kind == NdsWorkspaceAssetKind.DownloadPlaySignature && asset.OriginalLength == 136);
        await NdsImageWorkspace.PackFileAsync(fixture.Workspace, fixture.Output, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RelocatedWorkspaceAndRenamedAssetsNeedNoOriginalHostPaths()
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe original = await fixture.ExportAsync();
        string sourcePath = original.SourceImagePath;
        string firstAsset = original.Assets[0].Path;
        File.Move(Path.Combine(fixture.Workspace, sourcePath), Path.Combine(fixture.Workspace, "baseline.srl"));
        File.Move(Path.Combine(fixture.Workspace, firstAsset), Path.Combine(fixture.Workspace, "renamed-header.bin"));
        await fixture.ModifyRecipeAsync(json =>
        {
            json["sourceImagePath"] = "baseline.srl";
            json["assets"]![0]!["path"] = "renamed-header.bin";
        });
        string relocated = Path.Combine(fixture.Root, "elsewhere");
        Directory.Move(fixture.Workspace, relocated);
        await NdsImageWorkspace.PackFileAsync(relocated, fixture.Output, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SyntheticImage.CreateWithBanner(), await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExportRefusesExistingTargetsAndFailedOrCancelledExportsLeaveNoStagingTree()
    {
        using var fixture = new WorkspaceFixture();
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        Directory.CreateDirectory(fixture.Workspace);
        await Assert.ThrowsAsync<IOException>(async () => await NdsImageWorkspace.ExportAsync(image, fixture.Workspace,
            TestContext.Current.CancellationToken).ConfigureAwait(true));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        string absent = Path.Combine(fixture.Root, "absent");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await NdsImageWorkspace.ExportAsync(image, absent, cancelled.Token).ConfigureAwait(true));
        await image.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await NdsImageWorkspace.ExportAsync(image, absent,
            TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([fixture.Workspace], Directory.GetFileSystemEntries(fixture.Root));
    }
}
