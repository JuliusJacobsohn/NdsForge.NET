using NdsForge.Shared;

namespace NdsForge.Tests;

public sealed class NdsWorkspaceImportTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task UnchangedWorkspaceMatchesDirectStructuralImport(int kind)
    {
        byte[] bytes = kind switch
        {
            0 => SyntheticImage.CreateHeaderOnly(),
            1 => SyntheticImage.CreateWithBanner(),
            2 => SyntheticImage.CreateWithArm9Footer(),
            3 => SyntheticImage.CreateWithOverlayAuthentication(),
            4 => await CreateBuilder(true).BuildAsync(cancellationToken: TestContext.Current.CancellationToken),
            _ => SyntheticImage.CreateLateDsAuthenticated(),
        };
        using var fixture = new WorkspaceFixture();
        await fixture.ExportAsync(bytes);
        NdsImageBuilder imported = await NdsImageWorkspace.ImportAsync(fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage source = NdsImage.Load(bytes);
        NdsImageBuilder direct = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken);
        if (direct.DsMetadata is not null) { direct.DsMetadata.Integrity = imported.DsMetadata!.Integrity = NdsDsIntegrityOptions.Unauthenticated; }
        var options = new NdsImageBuildOptions { VerifyOutput = source.Validate().IsValid };
        Assert.Equal(await direct.BuildAsync(options, TestContext.Current.CancellationToken),
            await imported.BuildAsync(options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProgramFileBannerAndTypedMetadataEditsSurviveAfterWorkspaceRemoval()
    {
        NdsImageBuilder imported;
        using (var fixture = new WorkspaceFixture())
        {
            NdsWorkspaceRecipe recipe = await fixture.ExportAsync();
            await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.Arm9, [9, 8, 7, 6, 5]);
            await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.Arm7, [7, 6, 5]);
            await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.Allocation, "longer payload"u8.ToArray());
            await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.Banner,
                new NdsBannerBuilder().SetTitle(NdsBannerLanguage.English, "Edited workspace").Build().RawData.ToArray());
            imported = await NdsImageWorkspace.ImportAsync(fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageWorkspace.PackFileAsync(
                fixture.Workspace, fixture.Output, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        }
        imported.Title = "EDITED";
        imported.NandRomEndUnits = imported.NandWritableStartUnits = 2;
        imported.FileSystem.AddFile("/new.txt", "new"u8);
        using NdsImage output = NdsImage.Load(await imported.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("EDITED", output.Header.Title);
        Assert.Equal((ushort)2, output.Header.NandRomEndUnits);
        Assert.Equal([9, 8, 7, 6, 5], await ReadAsync(output, output.Header.Arm9.Data));
        Assert.Equal([7, 6, 5], await ReadAsync(output, output.Header.Arm7.Data));
        Assert.Equal("longer payload"u8.ToArray(), await output.FileSystem.GetFile("/hello.bin").ReadAllBytesAsync(TestContext.Current.CancellationToken));
        Assert.Equal("new"u8.ToArray(), await output.FileSystem.GetFile("/new.txt").ReadAllBytesAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Edited workspace", output.Banner!.Titles[NdsBannerLanguage.English]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Arm9FooterRemainsSeparateAndMustStayValid(bool malformed)
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync(SyntheticImage.CreateWithArm9Footer());
        NdsWorkspaceAsset asset = recipe.Assets.Single(item => item.Kind == NdsWorkspaceAssetKind.Arm9);
        byte[] original = await File.ReadAllBytesAsync(Path.Combine(fixture.Workspace, asset.Path), TestContext.Current.CancellationToken);
        byte[] replacement = new byte[32];
        original.AsSpan(original.Length - 12).CopyTo(replacement.AsSpan(20));
        if (malformed) { replacement[^12] ^= 1; }
        await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.Arm9, replacement);
        if (malformed)
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageWorkspace.ImportAsync(
                fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        }
        else
        {
            NdsImageBuilder builder = await NdsImageWorkspace.ImportAsync(fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(20, builder.Arm9!.Contents.Length);
            Assert.Equal(original[^12..], builder.Arm9.Footer.ToArray());
        }
    }

    [Fact]
    public async Task DebugAndRetainedTrailerPayloadChangesAreApplied()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.DebugProgram = new([1, 2, 3], 0x80001234);
        byte[] trailer = new byte[136];
        new byte[] { 0x61, 0x63, 1, 0 }.CopyTo(trailer, 0);
        builder.DownloadPlaySignature = NdsDownloadPlaySignature.Parse(trailer);
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.DebugProgram, [4, 5, 6, 7]);
        trailer[^1] = 0x99;
        await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.DownloadPlaySignature, trailer);
        NdsImageBuilder imported = await NdsImageWorkspace.ImportAsync(fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage output = NdsImage.Load(await imported.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal([4, 5, 6, 7], await ReadAsync(output, output.Header.DebugRom));
        Assert.Equal(0x80001234U, output.Header.DebugLoadAddress);
        Assert.Equal(trailer, output.DownloadPlaySignature!.RawData.ToArray());
    }

    [Fact]
    public async Task DsiProgramAndCarrierPayloadChangesRemainIndependent()
    {
        NdsImageBuilder builder = CreateBuilder(true);
        byte[] original = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync(original);
        await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.Arm9i, [5, 6, 7]);
        await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.Arm7i, [8, 9]);
        byte[] postHeader = Enumerable.Repeat((byte)0x19, 0x3000).ToArray();
        byte[] reservation = Enumerable.Repeat((byte)0x29, 0x3000).ToArray();
        await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.PostHeader, postHeader);
        await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.TwlReservation, reservation);
        NdsImageBuilder imported = await NdsImageWorkspace.ImportAsync(fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage output = NdsImage.Load(await imported.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal([5, 6, 7], await ReadAsync(output, output.Header.Arm9i!.Data));
        Assert.Equal([8, 9], await ReadAsync(output, output.Header.Arm7i!.Data));
        Assert.Equal(postHeader, output.CarrierLayout.PostHeaderData.ToArray());
        Assert.Equal(reservation, Assert.IsType<NdsCartridgeLayout>(output.CarrierLayout).TwlReservedData.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlayPayloadEditsUpdatePrivateAndNamedReferences(bool named)
    {
        NdsImageBuilder builder = CreateBuilder();
        if (named)
        {
            builder.FileSystem.AddFile("/overlay", new byte[16]);
            builder.AddOverlay(NdsOverlayDefinition.LinkToFile(NdsProcessor.Arm9, 3, "/overlay", 0x02002000, 16));
        }
        else { builder.AddOverlay(new(NdsProcessor.Arm9, 3, new byte[16], 0x02002000, 16)); }
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.Allocation, new byte[24]);
        NdsImageBuilder imported = await NdsImageWorkspace.ImportAsync(fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage output = NdsImage.Load(await imported.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        NdsOverlay overlay = Assert.Single(output.Arm9Overlays);
        Assert.Equal(24U, overlay.RamSize);
        Assert.Equal(24, overlay.Data!.Value.Length);
        Assert.Equal(named, overlay.File is not null);
        Assert.Single(output.FileSystem.Allocations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CompressedOverlayEditsRequireValidStorageAndUnchangedDecodedSize(int mode)
    {
        Assert.True(BlzEngine.TryCompress(new byte[1024], out byte[] stored, 0));
        NdsImageBuilder builder = CreateBuilder();
        builder.AddOverlay(new(NdsProcessor.Arm9, 3, stored, 0x02002000, 1024, compressedSize: (uint)stored.Length, flags: 1));
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(BlzEngine.TryCompress(Enumerable.Repeat((byte)1, mode == 1 ? 2048 : 1024).ToArray(), out byte[] replacement, 0));
        if (mode == 2) { replacement = [1, 2, 3]; }
        await ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.Allocation, replacement);
        if (mode != 0)
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageWorkspace.ImportAsync(
                fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        }
        else
        {
            NdsImageBuilder imported = await NdsImageWorkspace.ImportAsync(fixture.Workspace, cancellationToken: TestContext.Current.CancellationToken);
            using NdsImage output = NdsImage.Load(await imported.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(replacement, await ReadAsync(output, Assert.Single(output.Arm9Overlays).Data!.Value));
            Assert.Equal((uint)replacement.Length, Assert.Single(output.Arm9Overlays).CompressedSize);
        }
    }

    internal static async Task ChangeAsync(WorkspaceFixture fixture, NdsWorkspaceRecipe recipe, NdsWorkspaceAssetKind kind, byte[] bytes)
    {
        string path = recipe.Assets.Single(asset => asset.Kind == kind).Path;
        await File.WriteAllBytesAsync(Path.Combine(fixture.Workspace, path), bytes, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    internal static NdsImageBuilder CreateBuilder(bool dsi = false) => new()
    {
        Arm9 = new(NdsProcessor.Arm9, [1], 0x02000000, 0x02000000),
        Arm7 = new(NdsProcessor.Arm7, [2], 0x02380000, 0x02380000),
        Kind = dsi ? NdsImageKind.NintendoDsiEnhanced : NdsImageKind.NintendoDs,
        DsiMetadata = dsi ? new() : null,
        Arm9i = dsi ? new(NdsProcessor.Arm9i, [3], 0x02400000, 0x02400000) : null,
        Arm7i = dsi ? new(NdsProcessor.Arm7i, [4], 0x02E80000, 0x02E80000) : null,
    };

    private static async Task<byte[]> ReadAsync(NdsImage image, NdsRegion region)
    {
        using Stream stream = image.OpenRead(region);
        using var contents = new MemoryStream();
        await stream.CopyToAsync(contents, TestContext.Current.CancellationToken).ConfigureAwait(true);
        return contents.ToArray();
    }
}
