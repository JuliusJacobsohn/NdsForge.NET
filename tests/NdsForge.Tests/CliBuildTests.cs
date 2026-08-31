using NdsForge.Cli;

namespace NdsForge.Tests;

public sealed class CliBuildTests
{
    [Fact]
    public async Task EditedPayloadBuildsDeterministicallyWithRequestedCapacityAndPadding()
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync();
        await NdsWorkspaceImportTests.ChangeAsync(fixture, recipe, NdsWorkspaceAssetKind.Allocation, "edited"u8.ToArray());
        string[] args = ["build", fixture.Workspace, fixture.Output, "--capacity", "0x80000", "--pad", "--padding-byte", "A5"];
        Assert.Equal(0, await CliApplication.RunAsync(args));
        byte[] first = await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken);
        using (NdsImage image = NdsImage.Load(first))
        {
            Assert.Equal(0x80000, image.Length);
            Assert.Equal(0x80000, image.Header.DeviceCapacityBytes);
            Assert.True(image.Header.UsedImageSize < image.Length);
            Assert.All(first[(int)image.Header.UsedImageSize..], value => Assert.Equal((byte)0xA5, value));
            Assert.Equal("edited"u8.ToArray(), await image.FileSystem.GetFile("/hello.bin").ReadAllBytesAsync(TestContext.Current.CancellationToken));
        }
        Assert.Equal(1, await CliApplication.RunAsync(args));
        Assert.Equal(0, await CliApplication.RunAsync([.. args, "--overwrite"]));
        Assert.Equal(first, await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken));
        Assert.Equal(1, await CliApplication.RunAsync(["pack", fixture.Workspace, fixture.Output, "--overwrite"]));
        Assert.Equal(first, await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("header-edit")]
    [InlineData("too-small")]
    public async Task RejectedImportOrBuildLeavesExistingOutputAndNoTemporaryFile(string failure)
    {
        using var fixture = new WorkspaceFixture();
        NdsImageBuilder builder = NdsWorkspaceImportTests.CreateBuilder();
        builder.FileSystem.AddFile("/large.bin", new byte[0x20000]);
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        NdsWorkspaceAsset asset = recipe.Assets.Single(item => item.Kind == NdsWorkspaceAssetKind.Header);
        string input = Path.Combine(fixture.Workspace, asset.Path);
        if (failure == "missing") { File.Delete(input); }
        if (failure == "header-edit") { await File.WriteAllBytesAsync(input, [0], TestContext.Current.CancellationToken); }
        await File.WriteAllBytesAsync(fixture.Output, [5, 6, 7], TestContext.Current.CancellationToken);
        Assert.Equal(1, await CliApplication.RunAsync(["build", fixture.Workspace, fixture.Output, "--overwrite", "--capacity", "131072"]));
        Assert.Equal([5, 6, 7], await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, "*.ndsforge-*"));
    }

    [Theory]
    [InlineData("inside")]
    [InlineData("root")]
    [InlineData("directory")]
    [InlineData("file-link")]
    [InlineData("directory-link")]
    public async Task UnsafeOutputCannotOverwriteWorkspaceOrFollowLinks(string kind)
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync();
        string output = kind switch
        {
            "inside" => Path.Combine(fixture.Workspace, recipe.SourceImagePath),
            "root" => fixture.Workspace,
            "directory" => fixture.Root,
            "directory-link" => Path.Combine(fixture.Root, "link", "new.nds"),
            _ => fixture.Output,
        };
        if (kind == "file-link") { File.CreateSymbolicLink(output, Path.Combine(fixture.Workspace, recipe.SourceImagePath)); }
        if (kind == "directory-link") { Directory.CreateSymbolicLink(Path.GetDirectoryName(output)!, fixture.Workspace); }
        Assert.Equal(1, await CliApplication.RunAsync(["build", fixture.Workspace, output, "--overwrite"]));
        if (kind == "file-link") { File.Delete(output); }
        if (kind == "directory-link") { Directory.Delete(Path.GetDirectoryName(output)!); }
        NdsWorkspaceRecipe packed = await NdsImageWorkspace.PackFileAsync(fixture.Workspace, fixture.Output,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(recipe.SourceInventory.ImageSha256, packed.SourceInventory.ImageSha256);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("homebrew")]
    public async Task DsiRequiresExplicitPolicyAndRetainsNativePrograms(string policy)
    {
        using var fixture = new WorkspaceFixture();
        await fixture.ExportAsync(await NdsWorkspaceImportTests.CreateBuilder(true).BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await CliApplication.RunAsync(["build", fixture.Workspace, fixture.Output]));
        Assert.Equal(1, await CliApplication.RunAsync(["build", fixture.Workspace, fixture.Output, "--ds-integrity", "clear"]));
        Assert.False(File.Exists(fixture.Output));
        Assert.Equal(0, await CliApplication.RunAsync(["build", fixture.Workspace, fixture.Output, "--dsi-integrity", policy]));
        using NdsImage output = await NdsImage.OpenAsync(fixture.Output, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(NdsImageKind.NintendoDsiEnhanced, output.Header.Kind);
        Assert.NotNull(output.Header.Arm9i);
        Assert.True(output.Validate().IsValid);
        Assert.Equal(policy == "clear", output.Header.RawData.Span[0xF80..0x1000].IndexOfAnyExcept((byte)0) < 0);
    }

    [Theory]
    [InlineData("preserve")]
    [InlineData("clear")]
    public async Task LateDsRequiresAnExplicitAuthenticationDecision(string policy)
    {
        NdsImageBuilder builder = NdsWorkspaceImportTests.CreateBuilder();
        builder.Banner = new NdsBannerBuilder().Build();
        builder.DsMetadata = new()
        {
            ProgramFeatures = NdsProgramFeatures.AuthenticatesBanner,
            Integrity = NdsDsIntegrityOptions.CreateHmacSha1([], [1, 2, 3]),
        };
        using var fixture = new WorkspaceFixture();
        byte[] source = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        await fixture.ExportAsync(source);
        Assert.Equal(1, await CliApplication.RunAsync(["build", fixture.Workspace, fixture.Output]));
        Assert.False(File.Exists(fixture.Output));
        Assert.Equal(0, await CliApplication.RunAsync(["build", fixture.Workspace, fixture.Output, "--ds-integrity", policy]));
        using NdsImage output = await NdsImage.OpenAsync(fixture.Output, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(policy == "preserve", output.Header.DsExtended?.ProgramFeatures.HasFlag(NdsProgramFeatures.AuthenticatesBanner) == true);
        byte[] built = await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken);
        Assert.Equal(policy == "preserve" ? source[0x33C..0x350] : new byte[20], built[0x33C..0x350]);
    }

    [Theory]
    [InlineData("--dsi-integrity")]
    [InlineData("--ds-integrity")]
    public async Task InapplicableIntegrityPolicyFailsBeforePublication(string option)
    {
        using var fixture = new WorkspaceFixture();
        await fixture.ExportAsync();
        Assert.Equal(1, await CliApplication.RunAsync(["build", fixture.Workspace, fixture.Output, option, "clear"]));
        Assert.False(File.Exists(fixture.Output));
    }

    [Fact]
    public async Task UsageFailuresAndCancellationDoNotCreateOutputs()
    {
        using var fixture = new WorkspaceFixture();
        Assert.Equal(2, await CliApplication.RunAsync(["build", fixture.Workspace]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await CliBuildCommand.RunAsync(
            ["build", fixture.Workspace, fixture.Output], new CancellationToken(true)).ConfigureAwait(true));
        Assert.False(Directory.Exists(fixture.Root));
    }

    [Theory]
    [InlineData("stream:part")]
    [InlineData("NUL.nds")]
    [InlineData("COM1.nds")]
    [InlineData("LPT².nds")]
    [InlineData("image.nds.")]
    [InlineData("image.nds ")]
    public void AmbiguousOutputNamesAreRejectedWithoutOpeningFiles(string name)
    {
        using var fixture = new WorkspaceFixture();
        Assert.Throws<IOException>(() => CliBuildOutput.Check(fixture.Workspace, Path.Combine(fixture.Root, name), true));
        Assert.False(Directory.Exists(fixture.Root));
    }

    [Theory]
    [InlineData("--capacity", "0x80000")]
    [InlineData("--pad", null)]
    public async Task DigitalBuildsRejectCartridgeOnlySizing(string option, string? value)
    {
        using var fixture = new WorkspaceFixture();
        NdsImageBuilder builder = NdsWorkspaceImportTests.CreateBuilder(true);
        builder.Carrier = NdsImageCarrier.DigitalSrl;
        builder.Kind = NdsImageKind.NintendoDsiExclusive;
        builder.DsiMetadata!.TitleId = 0x0003000454455354;
        await fixture.ExportAsync(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        string[] args = ["build", fixture.Workspace, fixture.Output, "--dsi-integrity", "clear", option];
        Assert.Equal(1, await CliApplication.RunAsync(value is null ? args : [.. args, value]));
        Assert.False(File.Exists(fixture.Output));
    }
}
