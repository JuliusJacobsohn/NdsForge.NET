using System.Text.Json.Nodes;

namespace NdsForge.Tests;

/// <summary>Owns one isolated synthetic workspace and any output files produced by a test.</summary>
internal sealed class WorkspaceFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "NdsForgeTests", Guid.NewGuid().ToString("N"));
    public string Workspace => Path.Combine(Root, "workspace");
    public string Output => Path.Combine(Root, "packed.nds");
    public string RecipePath => Path.Combine(Workspace, NdsWorkspaceRecipe.FileName);

    public async Task<NdsWorkspaceRecipe> ExportAsync(byte[]? data = null)
    {
        using NdsImage image = NdsImage.Load(data ?? SyntheticImage.CreateWithBanner());
        return await NdsImageWorkspace.ExportAsync(image, Workspace, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    public async Task ModifyRecipeAsync(Action<JsonObject> edit)
    {
        JsonObject json = JsonNode.Parse(await File.ReadAllTextAsync(RecipePath, TestContext.Current.CancellationToken).ConfigureAwait(true))!.AsObject();
        edit(json);
        await File.WriteAllTextAsync(RecipePath, json.ToJsonString(), TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) { Directory.Delete(Root, recursive: true); }
    }
}
