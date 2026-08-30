using System.Text;

namespace NdsForge;

/// <summary>Exports self-contained image workspaces and verifies byte-exact packing from unchanged native inputs.</summary>
/// <remarks>Inputs must not change during an operation. Detected links are rejected; hostile concurrent filesystem replacement is not sandboxed.</remarks>
public static class NdsImageWorkspace
{
    /// <summary>Exports every native component alongside a complete snapshot, retaining bytes outside declared structures.</summary>
    /// <remarks>The destination must not exist. A temporary sibling is published only after all files and the recipe succeed.
    /// Snapshot bytes preserve existing image diagnostics; export neither repairs nor authenticates the original image.</remarks>
    /// <param name="image">Live caller-owned input kept open for the complete snapshot copy.</param>
    /// <param name="directory">New host directory; ROM filenames are never used directly as host asset names.</param>
    /// <param name="cancellationToken">Cancels before publication and removes incomplete staging files.</param>
    /// <returns>The detached recipe written to <see cref="NdsWorkspaceRecipe.FileName"/>.</returns>
    public static async ValueTask<NdsWorkspaceRecipe> ExportAsync(
        NdsImage image, string directory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        cancellationToken.ThrowIfCancellationRequested();
        if (image.Length is < 0x200 or > 0x100000000L) { throw new InvalidDataException("Workspace source length must fit the supported image address space."); }
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        CheckNewDirectory(root);
        string parent = Path.GetDirectoryName(root) ?? throw new IOException("A workspace must have a parent directory.");
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, $".ndsforge-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            const string sourcePath = "preservation/source.nds";
            string snapshot = NdsWorkspacePaths.Resolve(staging, sourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            var snapshotOutput = new FileStream(snapshot, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (snapshotOutput.ConfigureAwait(false))
            {
                await image.CopyToAsync(new(0, image.Length), snapshotOutput, cancellationToken).ConfigureAwait(false);
            }
            NdsWorkspaceRecipe recipe;
            using (NdsImage preserved = await NdsImage.OpenAsync(snapshot, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                NdsImageManifest inventory = await preserved.CreateManifestAsync(cancellationToken).ConfigureAwait(false);
                recipe = new()
                {
                    SourceImagePath = sourcePath,
                    SourceInventory = inventory,
                    Assets = await NdsWorkspaceCatalog.CaptureAsync(preserved, inventory, cancellationToken).ConfigureAwait(false),
                };
                string json = recipe.ToJson();
                foreach (NdsWorkspaceAsset asset in recipe.Assets)
                {
                    string path = NdsWorkspacePaths.Resolve(staging, asset.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await ExportAssetAsync(preserved, asset, path, cancellationToken).ConfigureAwait(false);
                }
                await File.WriteAllTextAsync(Path.Combine(staging, NdsWorkspaceRecipe.FileName), json,
                    new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            CheckNewDirectory(root);
            Directory.Move(staging, root);
            return recipe;
        }
        finally { if (Directory.Exists(staging)) { Directory.Delete(staging, recursive: true); } }
    }

    /// <summary>Reads only the versioned recipe without opening or trusting its component files.</summary>
    /// <param name="directory">Workspace root containing the conventional recipe file.</param>
    /// <param name="cancellationToken">Cancels bounded recipe reading.</param>
    /// <returns>A validated description; packing additionally verifies every byte-bearing input.</returns>
    public static ValueTask<NdsWorkspaceRecipe> ReadRecipeAsync(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        cancellationToken.ThrowIfCancellationRequested();
        return NdsWorkspaceInput.ReadRecipeAsync(Path.GetFullPath(directory), cancellationToken);
    }

    /// <summary>Publishes a byte-exact image only when the snapshot, inventory, and every exported asset remain consistent and unchanged.</summary>
    /// <remarks>Edited assets are rejected, never silently ignored. Existing diagnostics and unknown bytes remain unchanged.
    /// Output must be outside the workspace. An existing destination is replaced only after all checks and copy verification succeed.</remarks>
    /// <param name="directory">Self-contained workspace with no missing or redirected inputs.</param>
    /// <param name="destination">Regular output file outside the workspace tree.</param>
    /// <param name="overwriteDestination">Explicit permission to atomically replace an existing regular file.</param>
    /// <param name="cancellationToken">Cancels before publication, leaving any prior destination untouched.</param>
    /// <returns>The validated recipe whose complete-image hash matches the published output.</returns>
    public static async ValueTask<NdsWorkspaceRecipe> PackFileAsync(
        string directory, string destination, bool overwriteDestination = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        cancellationToken.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(directory);
        string output = Path.GetFullPath(destination);
        CheckOutput(root, output, overwriteDestination);
        NdsWorkspaceRecipe recipe = await ReadRecipeAsync(root, cancellationToken).ConfigureAwait(false);
        string snapshot = NdsWorkspacePaths.Resolve(root, recipe.SourceImagePath);
        using FileStream source = NdsWorkspaceInput.OpenRead(snapshot);
        using NdsImage image = await NdsImage.OpenAsync(source, leaveOpen: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        await NdsWorkspaceInput.ValidateExactAsync(root, recipe, image, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = output + $".ndsforge-{Guid.NewGuid():N}";
        try
        {
            var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (target.ConfigureAwait(false))
            {
                await image.CopyToAsync(new(0, image.Length), target, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                target.Position = 0;
                if (target.Length != recipe.SourceInventory.PhysicalLength ||
                    await NdsWorkspaceInput.HashAsync(target, cancellationToken).ConfigureAwait(false) != recipe.SourceInventory.ImageSha256)
                {
                    throw new InvalidDataException("Packed output does not match the workspace's complete source identity.");
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            CheckOutput(root, output, overwriteDestination);
            File.Move(temporary, output, overwriteDestination);
            return recipe;
        }
        finally { File.Delete(temporary); }
    }

    /// <summary>Writes one complete component with asynchronous stream ownership scoped independently from enumeration.</summary>
    private static async ValueTask ExportAssetAsync(NdsImage image, NdsWorkspaceAsset asset, string path, CancellationToken cancellationToken)
    {
        var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (output.ConfigureAwait(false))
        {
            await image.CopyToAsync(new(asset.OriginalOffset, asset.OriginalLength), output, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Refuses any preexisting export target, including dangling directory links.</summary>
    private static void CheckNewDirectory(string root)
    {
        NdsWorkspacePaths.CheckParents(root);
        if (Directory.Exists(root) || File.Exists(root) || new FileInfo(root).LinkTarget is not null)
        {
            throw new IOException("Workspace export requires a new destination directory.");
        }
    }

    /// <summary>Confines output to a separate regular-file location with runtime-only overwrite authority.</summary>
    private static void CheckOutput(string root, string output, bool overwrite)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar);
        if (output.Equals(normalizedRoot, comparison) || output.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw new IOException("Packed output must be outside the input workspace.");
        }
        NdsWorkspacePaths.CheckParents(Path.GetDirectoryName(output)!);
        var file = new FileInfo(output);
        if (file.LinkTarget is not null || Directory.Exists(output) || (file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint)))
        {
            throw new IOException("Packed output must be a regular file, not a link or directory.");
        }
        if (file.Exists && !overwrite) { throw new IOException($"Destination already exists: {output}"); }
    }
}
