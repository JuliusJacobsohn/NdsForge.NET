using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Checks lossless portable workspaces against complete content-addressed private image identities.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusWorkspaceTests
{
    [Theory]
    [MemberData(nameof(CorpusExpectations.Cases), MemberType = typeof(CorpusExpectations))]
    [Trait("CorpusTier", "Full")]
    public async Task WorkspacePackingRestoresEveryPhysicalByte(CorpusExpectationIndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await VerifyWorkspaceAsync(CorpusExpectations.Resolve(entry), entry.RomSha256).ConfigureAwait(true);
    }

    internal static async Task VerifyWorkspaceAsync(string sourcePath, string expectedIdentity)
    {
        string root = Path.Combine(Path.GetTempPath(), "NdsForgeWorkspaceTests", Guid.NewGuid().ToString("N"));
        string workspace = Path.Combine(root, "workspace");
        string packedPath = Path.Combine(root, "packed.nds");
        CancellationToken token = TestContext.Current.CancellationToken;
        try
        {
            using NdsImage source = await NdsImage.OpenAsync(sourcePath, cancellationToken: token).ConfigureAwait(true);
            NdsWorkspaceRecipe recipe = await NdsImageWorkspace.ExportAsync(source, workspace, token).ConfigureAwait(true);
            Assert.Equal(expectedIdentity, recipe.SourceInventory.ImageSha256, ignoreCase: true);
            Assert.Equal(source.FileSystem.Allocations.Count, recipe.Assets.Count(static asset => asset.Kind == NdsWorkspaceAssetKind.Allocation));
            Assert.Equal(source.FileSystem.Files.Count, recipe.SourceInventory.Files.Count);
            Assert.Equal(source.Arm9Overlays.Count + source.Arm7Overlays.Count, recipe.SourceInventory.Overlays.Count);
            Assert.Equal(recipe.Assets.Count + 2, Directory.GetFiles(workspace, "*", SearchOption.AllDirectories).Length);
            NdsWorkspaceRecipe packed = await NdsImageWorkspace.PackFileAsync(workspace, packedPath, cancellationToken: token).ConfigureAwait(true);
            Assert.Equal(recipe.ToJson(), packed.ToJson());
            using FileStream bytes = File.OpenRead(packedPath);
            Assert.Equal(source.Length, bytes.Length);
            Assert.Equal(expectedIdentity, Convert.ToHexString(await SHA256.HashDataAsync(bytes, token).ConfigureAwait(true)), ignoreCase: true);
            using NdsImage output = await NdsImage.OpenAsync(packedPath, cancellationToken: token).ConfigureAwait(true);
            Assert.Equal(source.Validate().Diagnostics, output.Validate().Diagnostics);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    }
}
