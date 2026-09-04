namespace NdsForge.Cli;

/// <summary>Publishes structural builds outside the workspace after verification, with explicit replacement authority.</summary>
internal static class CliBuildOutput
{
    internal static async ValueTask<NdsImageBuildResult> WriteAsync(
        NdsImageBuilder builder, string root, string output, NdsImageBuildOptions options, CancellationToken cancellationToken)
    {
        Check(root, output, options.OverwriteDestination);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = output + $".ndsforge-{Guid.NewGuid():N}";
        try
        {
            NdsImageBuildResult result;
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (stream.ConfigureAwait(false))
            {
                result = await builder.WriteAsync(stream, options, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Check(root, output, options.OverwriteDestination);
            File.Move(temporary, output, options.OverwriteDestination);
            return result;
        }
        finally { File.Delete(temporary); }
    }

    internal static void Check(string root, string output, bool overwrite)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        if (output.Equals(normalizedRoot, comparison) ||
            output.StartsWith(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
        {
            throw new IOException("Structural output must be outside the input workspace.");
        }
        for (DirectoryInfo? current = new(Path.GetDirectoryName(output)!); current is not null; current = current.Parent)
        {
            if (current.LinkTarget is not null || (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                throw new IOException("Structural output cannot traverse a reparse-point directory.");
            }
        }
        var file = new FileInfo(output);
        string stem = file.Name.Split('.')[0];
        bool device = stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³') &&
            (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
        if (file.Name.Contains(':', StringComparison.Ordinal) || file.Name.EndsWith('.') || file.Name.EndsWith(' ') || device)
        {
            throw new IOException("Structural output cannot use alternate streams, reserved device names, or ambiguous filename suffixes.");
        }
        if (file.LinkTarget is not null || Directory.Exists(output) || (file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint)))
        {
            throw new IOException("Structural output must be a regular file, not a link or directory.");
        }
        if (file.Exists && !overwrite) { throw new IOException($"Destination already exists: {output}"); }
    }
}
