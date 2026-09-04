using System.Text.Json;

namespace NdsForge.Tests;

public sealed class NdsWorkspaceIntegrityTests
{
    [Theory]
    [InlineData(-1L, 0L)]
    [InlineData(0L, -1L)]
    [InlineData(long.MaxValue, 1L)]
    [InlineData(1L, long.MaxValue)]
    [InlineData(long.MinValue, long.MinValue)]
    [InlineData(0x4001L, 0L)]
    public async Task RecipeRejectsNegativeOverflowingAndUnboundedComponentRegions(long offset, long length)
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe valid = await fixture.ExportAsync();
        var invalid = new NdsWorkspaceRecipe
        {
            SourceInventory = valid.SourceInventory,
            Assets = valid.Assets.Select(static asset => asset).ToArray(),
        };
        ((NdsWorkspaceAsset[])invalid.Assets)[0] = valid.Assets[0] with { OriginalOffset = offset, OriginalLength = length };
        Assert.Throws<InvalidDataException>(() => invalid.ToJson());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ffffffff")]
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public async Task RecipeRejectsNoncanonicalComponentDigests(string digest)
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe valid = await fixture.ExportAsync();
        var invalid = new NdsWorkspaceRecipe
        {
            SourceInventory = valid.SourceInventory,
            Assets = valid.Assets.Select(static asset => asset).ToArray(),
        };
        ((NdsWorkspaceAsset[])invalid.Assets)[0] = valid.Assets[0] with { OriginalSha256 = digest };
        Assert.Throws<InvalidDataException>(() => invalid.ToJson());
    }

    [Fact]
    public async Task Utf8PreambleIsAcceptedWithoutChangingRecipeIdentity()
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe original = await fixture.ExportAsync();
        await File.WriteAllTextAsync(fixture.RecipePath, original.ToJson(), new System.Text.UTF8Encoding(true), TestContext.Current.CancellationToken);
        NdsWorkspaceRecipe parsed = await NdsImageWorkspace.ReadRecipeAsync(fixture.Workspace, TestContext.Current.CancellationToken);
        Assert.Equal(original.ToJson(), parsed.ToJson());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("longer")]
    [InlineData("snapshot")]
    [InlineData("snapshot-length")]
    [InlineData("metadata")]
    [InlineData("region")]
    [InlineData("identity")]
    [InlineData("omitted")]
    public async Task InconsistentInputsNeverReplaceExistingOutput(string modification)
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync();
        NdsWorkspaceAsset allocation = recipe.Assets.Single(static asset => asset.Kind == NdsWorkspaceAssetKind.Allocation);
        string path = Path.Combine(fixture.Workspace, allocation.Path);
        byte[] sentinel = [7, 8, 9];
        await File.WriteAllBytesAsync(fixture.Output, sentinel, TestContext.Current.CancellationToken);
        switch (modification)
        {
            case "missing": File.Delete(path); break;
            case "changed": await File.WriteAllBytesAsync(path, "jello"u8.ToArray(), TestContext.Current.CancellationToken); break;
            case "longer": await File.WriteAllBytesAsync(path, "hello!"u8.ToArray(), TestContext.Current.CancellationToken); break;
            case "snapshot":
            case "snapshot-length":
                path = Path.Combine(fixture.Workspace, recipe.SourceImagePath);
                byte[] data = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
                data[^1] ^= 0xFF;
                await File.WriteAllBytesAsync(path, modification == "snapshot" ? data : data[..^1], TestContext.Current.CancellationToken);
                break;
            case "metadata": await fixture.ModifyRecipeAsync(json => json["sourceInventory"]!["header"]!["title"] = "CHANGED"); break;
            case "region": await fixture.ModifyRecipeAsync(json => json["assets"]![0]!["originalOffset"] = 1); break;
            case "identity": await fixture.ModifyRecipeAsync(json => json["assets"]![0]!["originalSha256"] = new string('a', 64)); break;
            case "omitted": await fixture.ModifyRecipeAsync(json => json["assets"]!.AsArray().RemoveAt(recipe.Assets.Count - 1)); break;
        }
        Exception failure = await Assert.ThrowsAnyAsync<Exception>(async () => await NdsImageWorkspace.PackFileAsync(fixture.Workspace,
            fixture.Output, overwriteDestination: true, TestContext.Current.CancellationToken).ConfigureAwait(true));
        if (modification == "missing") { Assert.IsType<FileNotFoundException>(failure); }
        else { Assert.IsType<InvalidDataException>(failure); }
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken));
        Assert.Equal([fixture.Output], Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task OutputRequiresExplicitOverwriteAndCannotBeInsideWorkspace()
    {
        using var fixture = new WorkspaceFixture();
        await fixture.ExportAsync();
        await File.WriteAllBytesAsync(fixture.Output, [1, 2, 3], TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(async () => await NdsImageWorkspace.PackFileAsync(fixture.Workspace, fixture.Output,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        await NdsImageWorkspace.PackFileAsync(fixture.Workspace, fixture.Output, true, TestContext.Current.CancellationToken);
        Assert.Equal(SyntheticImage.CreateWithBanner(), await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken));
        foreach (string path in new[] { fixture.Workspace, fixture.RecipePath, Path.Combine(fixture.Workspace, "new.nds"), fixture.Root })
        {
            await Assert.ThrowsAsync<IOException>(async () => await NdsImageWorkspace.PackFileAsync(fixture.Workspace, path,
                true, TestContext.Current.CancellationToken).ConfigureAwait(true));
        }
    }

    [Fact]
    public async Task RecipeReaderRejectsInvalidUtf8OversizedFilesAndCancelledOperations()
    {
        using var fixture = new WorkspaceFixture();
        await fixture.ExportAsync();
        await File.WriteAllBytesAsync(fixture.RecipePath, [0xFF, 0xFE], TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageWorkspace.ReadRecipeAsync(fixture.Workspace,
            TestContext.Current.CancellationToken).ConfigureAwait(true));
        using (FileStream stream = File.OpenWrite(fixture.RecipePath)) { stream.SetLength(NdsWorkspaceRecipe.MaximumJsonBytes + 1L); }
        await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageWorkspace.ReadRecipeAsync(fixture.Workspace,
            TestContext.Current.CancellationToken).ConfigureAwait(true));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await NdsImageWorkspace.ReadRecipeAsync(fixture.Workspace, cancelled.Token).ConfigureAwait(true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await NdsImageWorkspace.PackFileAsync(fixture.Workspace, fixture.Output,
            cancellationToken: cancelled.Token).ConfigureAwait(true));
        Assert.False(File.Exists(fixture.Output));
    }

    [Fact]
    public async Task RecipeRejectsAmbiguityMissingVersionInvalidEnumsAndIncompleteIdentities()
    {
        using var fixture = new WorkspaceFixture();
        NdsWorkspaceRecipe recipe = await fixture.ExportAsync();
        string valid = recipe.ToJson(indented: false);
        Assert.Throws<InvalidDataException>(() => NdsWorkspaceRecipe.ParseJson(valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => NdsWorkspaceRecipe.ParseJson(valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => NdsWorkspaceRecipe.ParseJson(valid.Replace("\"schemaVersion\":1,", "", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => NdsWorkspaceRecipe.ParseJson(valid.Replace("\"kind\":\"header\"", "\"kind\":0", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => NdsWorkspaceRecipe.ParseJson(valid.Replace("\"kind\":\"header\"", "\"kind\":\"future\"", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => NdsWorkspaceRecipe.ParseJson(valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"overwrite\":true", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => new NdsWorkspaceRecipe().ToJson());
        Assert.Throws<InvalidDataException>(() => new NdsWorkspaceRecipe { SourceInventory = recipe.SourceInventory }.ToJson());
        Assert.Throws<InvalidDataException>(() => new NdsWorkspaceRecipe { SourceInventory = recipe.SourceInventory, Assets = [.. recipe.Assets, recipe.Assets[0]] }.ToJson());
        Assert.Throws<InvalidDataException>(() => new NdsWorkspaceRecipe
        {
            SourceInventory = recipe.SourceInventory,
            Assets = recipe.Assets.Select(static asset => asset.Kind == NdsWorkspaceAssetKind.Header ? asset with { FileId = 0 } : asset).ToArray()
        }.ToJson());
        Assert.Throws<InvalidDataException>(() => NdsWorkspaceRecipe.ParseJson(new string(' ', NdsWorkspaceRecipe.MaximumJsonBytes) + "{}"));
    }
}
