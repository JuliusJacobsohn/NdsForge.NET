namespace NdsForge;

/// <summary>Publishes resized output through a temporary sibling while rejecting detected host-path redirection.</summary>
internal static class NdsResizePathWriter
{
    /// <summary>Leaves the requested destination unchanged until resizing and verification succeed.</summary>
    internal static async ValueTask<NdsImageResizeResult> WriteAsync(
        NdsImage image, string path, NdsImageResizeOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        string output = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(output)!;
        CheckParents(directory);
        CheckDestination(output, options.OverwriteDestination);
        Directory.CreateDirectory(directory);
        string temporary = output + ".ndsforge-" + Guid.NewGuid().ToString("N");
        try
        {
            NdsImageResizeResult result;
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (stream.ConfigureAwait(false))
            {
                result = await NdsImageResizer.WriteAsync(image, stream, options, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            CheckParents(directory);
            CheckDestination(output, options.OverwriteDestination);
            File.Move(temporary, output, options.OverwriteDestination);
            return result;
        }
        finally { File.Delete(temporary); }
    }

    /// <summary>Checks all existing ancestors rather than following links into an unintended directory.</summary>
    private static void CheckParents(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            if (current.LinkTarget is not null || (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                throw new IOException("Resizing output cannot traverse a reparse-point directory.");
            }
        }
    }

    /// <summary>Restricts replacement to an explicitly authorized existing regular file.</summary>
    private static void CheckDestination(string output, bool overwrite)
    {
        var file = new FileInfo(output);
        if (file.LinkTarget is not null || Directory.Exists(output) || (file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint)))
        {
            throw new IOException("Resizing output must be a regular file, not a directory or reparse point.");
        }
        if (file.Exists && !overwrite) { throw new IOException($"Destination already exists: {output}"); }
    }
}
