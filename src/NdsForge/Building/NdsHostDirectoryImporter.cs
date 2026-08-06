namespace NdsForge;

/// <summary>Stages a host tree in deterministic order and rejects unsafe or oversized input before builder mutation.</summary>
internal static class NdsHostDirectoryImporter
{
    /// <summary>Scans one source without following links and copies bounded files into a detached transaction snapshot.</summary>
    /// <param name="sourceDirectory">Existing host directory selected by the caller.</param>
    /// <param name="destinationDirectory">Normalized NitroFS path receiving source contents.</param>
    /// <param name="options">Validated link and allocation policy.</param>
    /// <param name="cancellationToken">Cancels enumeration or file reads.</param>
    /// <returns>All directories, payloads, total scanned bytes, and skipped-link count.</returns>
    public static async ValueTask<NdsHostDirectorySnapshot> StageAsync(
        string sourceDirectory,
        string destinationDirectory,
        NdsDirectoryImportOptions options,
        CancellationToken cancellationToken)
    {
        string sourceRoot = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"Host import directory was not found: {sourceRoot}");
        }

        if ((File.GetAttributes(sourceRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The host import root cannot itself be a link or reparse point.");
        }

        var directories = new List<string> { destinationDirectory };
        var files = new List<NdsHostFileSnapshot>();
        var pending = new Stack<(string Host, string Relative)>();
        pending.Push((sourceRoot, string.Empty));
        long totalBytes = 0;
        int skipped = 0;
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string hostDirectory, string relativeDirectory) = pending.Pop();
            string[] entries = Directory.EnumerateFileSystemEntries(hostDirectory)
                .Order(StringComparer.Ordinal)
                .ToArray();
            for (int index = entries.Length - 1; index >= 0; index--)
            {
                string entry = entries[index];
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if (options.LinkPolicy == NdsHostLinkPolicy.Reject)
                    {
                        throw new IOException($"Host import encountered a link or reparse point: {entry}");
                    }

                    skipped++;
                    continue;
                }

                string relative = string.IsNullOrEmpty(relativeDirectory)
                    ? Path.GetFileName(entry)
                    : relativeDirectory + "/" + Path.GetFileName(entry);
                string target = Combine(destinationDirectory, relative);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(target);
                    pending.Push((entry, relative));
                    continue;
                }

                if (files.Count >= options.MaximumFiles)
                {
                    throw new InvalidDataException("Host directory import exceeds the configured file-count limit.");
                }
                long length = new FileInfo(entry).Length;
                if (length > options.MaximumTotalBytes - totalBytes || length > Array.MaxLength)
                {
                    throw new InvalidDataException("Host directory import exceeds the configured byte allocation limit.");
                }

                byte[] contents = await File.ReadAllBytesAsync(entry, cancellationToken).ConfigureAwait(false);
                if (contents.LongLength > options.MaximumTotalBytes - totalBytes)
                {
                    throw new InvalidDataException("A host file changed size and exceeded the import allocation limit while being read.");
                }

                totalBytes = checked(totalBytes + contents.LongLength);
                files.Add(new(target, contents));
            }
        }

        return new(
            directories.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            files.OrderBy(static value => value.Path, StringComparer.Ordinal).ToArray(),
            totalBytes,
            skipped);
    }

    /// <summary>Combines a normalized NitroFS destination with a validated relative host path.</summary>
    private static string Combine(string destinationDirectory, string relativePath) =>
        NdsFileSystemBuilder.NormalizePath(
            destinationDirectory == "/" ? "/" + relativePath : destinationDirectory + "/" + relativePath,
            allowRoot: true);
}

/// <summary>Holds one copied host payload under its validated destination path.</summary>
/// <param name="Path">Canonical NitroFS destination.</param>
/// <param name="Contents">Detached bytes read under the import allocation limit.</param>
internal sealed record NdsHostFileSnapshot(string Path, byte[] Contents);

/// <summary>Holds a complete import transaction that is safe to validate against and then apply to a builder.</summary>
/// <param name="Directories">Canonical staged directory paths.</param>
/// <param name="Files">Copied staged payloads in ordinal path order.</param>
/// <param name="TotalBytes">Sum of all staged payload lengths.</param>
/// <param name="SkippedLinks">Host links omitted under skip policy.</param>
internal sealed record NdsHostDirectorySnapshot(
    string[] Directories,
    NdsHostFileSnapshot[] Files,
    long TotalBytes,
    int SkippedLinks);
