namespace NdsForge;

/// <summary>Checks every host asset and only materializes changes to explicitly editable native payload roles.</summary>
internal static class NdsWorkspaceImportInput
{
    /// <summary>Reads changed payloads through safe paths while retaining strict identity checks for layout-owned assets.</summary>
    internal static async ValueTask<IReadOnlyDictionary<NdsWorkspaceAsset, byte[]>> ReadChangesAsync(
        string root, NdsWorkspaceRecipe recipe, NdsWorkspaceImportOptions options, CancellationToken cancellationToken)
    {
        long total = 0;
        var changed = new Dictionary<NdsWorkspaceAsset, byte[]>();
        foreach (NdsWorkspaceAsset asset in recipe.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using FileStream stream = NdsWorkspaceInput.OpenRead(NdsWorkspacePaths.Resolve(root, asset.Path));
            long length = stream.Length;
            long boundedLength = Math.Max(length, asset.OriginalLength);
            if (boundedLength > options.MaximumAssetBytes || boundedLength > options.MaximumTotalAssetBytes - total)
            {
                throw new InvalidDataException($"Workspace input exceeds its configured component or aggregate materialization limit: {asset.Path}");
            }
            total += boundedLength;
            string hash = await NdsWorkspaceInput.HashAsync(stream, cancellationToken).ConfigureAwait(false);
            if (length == asset.OriginalLength && hash == asset.OriginalSha256) { continue; }
            if (!CanEdit(asset.Kind))
            {
                throw new InvalidDataException($"Structural import does not accept edits to the {asset.Kind} asset; use the returned builder's typed operations instead.");
            }
            stream.Position = 0;
            byte[] bytes = new byte[checked((int)length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (stream.ReadByte() != -1 || Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != hash)
            {
                throw new InvalidDataException($"Workspace component changed while it was being read: {asset.Path}");
            }
            changed.Add(asset, bytes);
        }
        return changed;
    }

    /// <summary>Separates stored payloads from source ordering, identity, and generated authentication tables.</summary>
    private static bool CanEdit(NdsWorkspaceAssetKind kind) => kind is
        NdsWorkspaceAssetKind.Arm9 or NdsWorkspaceAssetKind.Arm7 or NdsWorkspaceAssetKind.Arm9i or
        NdsWorkspaceAssetKind.Arm7i or NdsWorkspaceAssetKind.Allocation or NdsWorkspaceAssetKind.Banner or
        NdsWorkspaceAssetKind.DebugProgram or NdsWorkspaceAssetKind.PostHeader or NdsWorkspaceAssetKind.TwlReservation or
        NdsWorkspaceAssetKind.DownloadPlaySignature;
}
