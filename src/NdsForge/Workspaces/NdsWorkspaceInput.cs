using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NdsForge;

/// <summary>Validates a workspace against its own immutable preservation baseline before any output is published.</summary>
internal static class NdsWorkspaceInput
{
    /// <summary>Reads bounded strict UTF-8 without accepting silently replaced invalid byte sequences.</summary>
    internal static async ValueTask<NdsWorkspaceRecipe> ReadRecipeAsync(string root, CancellationToken cancellationToken)
    {
        string path = NdsWorkspacePaths.Resolve(root, NdsWorkspaceRecipe.FileName);
        using FileStream stream = OpenRead(path);
        if (stream.Length > NdsWorkspaceRecipe.MaximumJsonBytes) { throw new InvalidDataException("Workspace recipe exceeds the JSON byte limit."); }
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (stream.ReadByte() != -1) { throw new InvalidDataException("Workspace recipe changed during reading."); }
        try
        {
            ReadOnlyMemory<byte> text = bytes.AsMemory();
            if (text.Span.StartsWith(Encoding.UTF8.Preamble)) { text = text[Encoding.UTF8.Preamble.Length..]; }
            return NdsWorkspaceRecipe.ParseJson(new UTF8Encoding(false, true).GetString(text.Span));
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or ArgumentException)
        {
            throw new InvalidDataException("Workspace recipe is not valid schema-compatible UTF-8 JSON.", exception);
        }
    }

    /// <summary>Proves inventory metadata and asset identities agree with the immutable source snapshot.</summary>
    internal static async ValueTask ValidateBaselineAsync(
        NdsWorkspaceRecipe recipe, NdsImage image, CancellationToken cancellationToken)
    {
        if (image.Length != recipe.SourceInventory.PhysicalLength) { throw new InvalidDataException("Workspace preservation snapshot length has changed."); }
        NdsImageManifest actual = await NdsImageManifestCapture.CaptureAsync(image, cancellationToken,
            includeNandBoundaries: recipe.SourceInventory.Header.NandRomEndUnits is not null).ConfigureAwait(false);
        using JsonDocument expectedJson = JsonDocument.Parse(recipe.SourceInventory.ToJson(indented: false));
        using JsonDocument actualJson = JsonDocument.Parse(actual.ToJson(indented: false));
        if (!JsonElement.DeepEquals(expectedJson.RootElement, actualJson.RootElement))
        {
            throw new InvalidDataException("Workspace source inventory does not match the preservation snapshot.");
        }
        IReadOnlyList<NdsWorkspaceAsset> catalog = await NdsWorkspaceCatalog.CaptureAsync(image, actual, cancellationToken).ConfigureAwait(false);
        if (catalog.Count != recipe.Assets.Count) { throw new InvalidDataException("Workspace does not declare every original component."); }
        var expected = catalog.ToDictionary(static asset => (asset.Kind, asset.FileId));
        foreach (NdsWorkspaceAsset asset in recipe.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!expected.TryGetValue((asset.Kind, asset.FileId), out NdsWorkspaceAsset? original) ||
                asset.OriginalOffset != original.OriginalOffset || asset.OriginalLength != original.OriginalLength ||
                asset.OriginalSha256 != original.OriginalSha256)
            {
                throw new InvalidDataException($"Workspace component identity does not match its original region: {asset.Path}");
            }
        }
    }

    /// <summary>Additionally checks every asset for byte-exact packing rather than structural import.</summary>
    internal static async ValueTask ValidateExactAsync(
        string root, NdsWorkspaceRecipe recipe, NdsImage image, CancellationToken cancellationToken)
    {
        await ValidateBaselineAsync(recipe, image, cancellationToken).ConfigureAwait(false);
        foreach (NdsWorkspaceAsset asset in recipe.Assets)
        {
            string path = NdsWorkspacePaths.Resolve(root, asset.Path);
            using FileStream input = OpenRead(path);
            if (input.Length != asset.OriginalLength || await HashAsync(input, cancellationToken).ConfigureAwait(false) != asset.OriginalSha256)
            {
                throw new InvalidDataException($"Exact packing requires an unchanged component: {asset.Path}");
            }
        }
    }

    /// <summary>Opens existing files without allowing ordinary concurrent write or delete access on enforcing hosts.</summary>
    internal static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
        64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    /// <summary>Produces the recipe's canonical lowercase streamed content identity.</summary>
    internal static async ValueTask<string> HashAsync(Stream stream, CancellationToken cancellationToken) =>
        Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
}
