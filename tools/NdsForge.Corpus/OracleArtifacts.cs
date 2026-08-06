using System.Security.Cryptography;

namespace NdsForge.Corpus;

/// <summary>Reduces large disposable outputs to portable relative paths, lengths, and cryptographic identities.</summary>
internal static class OracleArtifacts
{
    /// <summary>Hashes selected files or directory trees in ordinal relative-path order.</summary>
    /// <param name="workingDirectory">Root removed from every recorded artifact path.</param>
    /// <param name="paths">Existing output files or directories belonging to one operation.</param>
    /// <returns>Deterministically ordered artifact records.</returns>
    public static async Task<IReadOnlyList<OracleArtifact>> CaptureAsync(
        string workingDirectory,
        IEnumerable<string> paths)
    {
        string root = Path.GetFullPath(workingDirectory);
        string[] files = paths.SelectMany(path => EnumerateFiles(root, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var artifacts = new List<OracleArtifact>(files.Length);
        foreach (string file in files)
        {
            var info = new FileInfo(file);
            await using var stream = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream).ConfigureAwait(false));
            artifacts.Add(new(Path.GetRelativePath(root, file).Replace('\\', '/'), info.Length, hash));
        }

        return artifacts;
    }

    /// <summary>Expands one workspace-relative file or tree while rejecting accidental escape from the disposable root.</summary>
    /// <param name="root">Resolved workspace root ending at a path boundary.</param>
    /// <param name="path">Relative or absolute candidate expected beneath the root.</param>
    /// <returns>Existing files reached without following an alternate root.</returns>
    private static IEnumerable<string> EnumerateFiles(string root, string path)
    {
        string candidate = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        string boundary = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!candidate.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Oracle artifact escaped its workspace: {candidate}");
        }

        if (File.Exists(candidate))
        {
            return [candidate];
        }

        return Directory.Exists(candidate)
            ? Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories)
            : [];
    }
}
