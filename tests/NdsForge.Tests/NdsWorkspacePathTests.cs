namespace NdsForge.Tests;

public sealed class NdsWorkspacePathTests
{
    [Theory]
    [InlineData("../outside")]
    [InlineData("/absolute")]
    [InlineData("C:/outside")]
    [InlineData("a\\b")]
    [InlineData("a//b")]
    [InlineData("a/./b")]
    [InlineData("a/../b")]
    [InlineData("a/CON")]
    [InlineData("nul.txt")]
    [InlineData("LPT1.bin")]
    [InlineData("COM¹.bin")]
    [InlineData("aux/x.bin")]
    [InlineData("a. ")]
    [InlineData("a.")]
    [InlineData("a?b")]
    [InlineData("a:b")]
    [InlineData("a\nb")]
    [InlineData(" ")]
    public void NonportablePathsAreRejected(string path) => Assert.Throws<InvalidDataException>(() => NdsWorkspacePaths.ValidateRelative(path));

    [Fact]
    public async Task PathsCannotCollideWithEachOtherRecipeOrParentFiles()
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync();
        foreach (string path in new[] { "NDSFORGE-WORKSPACE.JSON", recipe.SourceImagePath.ToUpperInvariant(),
            "preservation", recipe.SourceImagePath + "/child.bin", recipe.Assets[1].Path.ToUpperInvariant(), new string('a', 256) })
        {
            var invalid = new NdsWorkspaceRecipe
            {
                SourceInventory = recipe.SourceInventory,
                SourceImagePath = recipe.SourceImagePath,
                Assets = recipe.Assets.Select(static asset => asset).ToArray(),
            };
            ((NdsWorkspaceAsset[])invalid.Assets)[0] = recipe.Assets[0] with { Path = path };
            Assert.Throws<InvalidDataException>(() => invalid.ToJson());
        }
    }

    [Fact]
    public async Task FileDirectoryAndDanglingLinksAreRejectedOnInputAndOutput()
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync();
        string directoryLink = Path.Combine(fixture.Root, "redirect");
        string target = Path.Combine(fixture.Root, "keep.bin");
        string dangling = Path.Combine(fixture.Root, "dangling");
        await File.WriteAllBytesAsync(target, [9, 8, 7], TestContext.Current.CancellationToken);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(directoryLink, fixture.Workspace);
                File.CreateSymbolicLink(fixture.Output, target);
                Directory.CreateSymbolicLink(dangling, Path.Combine(fixture.Root, "absent"));
            }
            catch (UnauthorizedAccessException) { Assert.Skip("Creating symbolic links requires host permission for this filesystem test."); }
            await Assert.ThrowsAsync<IOException>(async () => await NdsImageWorkspace.ReadRecipeAsync(directoryLink,
                TestContext.Current.CancellationToken).ConfigureAwait(true));
            await Assert.ThrowsAsync<IOException>(async () => await NdsImageWorkspace.PackFileAsync(fixture.Workspace, fixture.Output,
                true, TestContext.Current.CancellationToken).ConfigureAwait(true));
            using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
            await Assert.ThrowsAsync<IOException>(async () => await NdsImageWorkspace.ExportAsync(image, dangling,
                TestContext.Current.CancellationToken).ConfigureAwait(true));
            File.Delete(fixture.Output);
            foreach (string relative in new[] { NdsWorkspaceRecipe.FileName, recipe.SourceImagePath, recipe.Assets[0].Path })
            {
                string asset = Path.Combine(fixture.Workspace, relative);
                string saved = asset + ".original";
                File.Move(asset, saved);
                try
                {
                    File.CreateSymbolicLink(asset, saved);
                    await Assert.ThrowsAsync<IOException>(async () => await NdsImageWorkspace.PackFileAsync(fixture.Workspace, fixture.Output,
                        cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
                }
                finally { File.Delete(asset); File.Move(saved, asset); }
            }
            Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(fixture.Output));
        }
        finally
        {
            File.Delete(fixture.Output);
            if (new DirectoryInfo(directoryLink).LinkTarget is not null) { Directory.Delete(directoryLink); }
            if (new DirectoryInfo(dangling).LinkTarget is not null)
            {
                // Unix links have no persistent directory-target type when their target is absent.
                if (OperatingSystem.IsWindows()) { Directory.Delete(dangling); }
                else { File.Delete(dangling); }
            }
            Assert.Null(new DirectoryInfo(dangling).LinkTarget);
            Assert.True(Directory.Exists(fixture.Workspace));
        }
    }
}
