using System.Buffers;

namespace NdsForge;

/// <summary>Confines portable recipe paths to an explicitly selected host directory without following detected links.</summary>
internal static class NdsWorkspacePaths
{
    /// <summary>Rejects punctuation that cannot be represented consistently by supported host filesystems.</summary>
    private static readonly SearchValues<char> InvalidCharacters = SearchValues.Create("<>:\"\\|?*");

    /// <summary>Requires a canonical slash-delimited relative name before any path is combined with a host root.</summary>
    internal static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.AsSpan().ContainsAny(InvalidCharacters) ||
            path.Any(char.IsControl))
        {
            throw new InvalidDataException("Workspace paths must be bounded portable relative paths.");
        }
        foreach (string segment in path.Split('/'))
        {
            string stem = segment.Split('.')[0];
            bool device = stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³') &&
                (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
            if (segment.Length is 0 or > 255 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') || device)
            {
                throw new InvalidDataException("Workspace paths cannot contain traversal, empty segments, reserved device names, or ambiguous suffixes.");
            }
        }
    }

    /// <summary>Resolves a validated asset below a root and rejects any existing reparse-point path component.</summary>
    internal static string Resolve(string root, string relative)
    {
        ValidateRelative(relative);
        string normalizedRoot = System.IO.Path.GetFullPath(root);
        string output = System.IO.Path.GetFullPath(System.IO.Path.Combine(normalizedRoot, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        string prefix = normalizedRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!output.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidDataException("A workspace asset resolves outside its root.");
        }
        CheckParents(System.IO.Path.GetDirectoryName(output)!);
        var file = new FileInfo(output);
        if (file.LinkTarget is not null || (file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint)) || Directory.Exists(output))
        {
            throw new IOException("A workspace asset must be a regular file, not a link or directory.");
        }
        return output;
    }

    /// <summary>Checks the selected root as well as its existing ancestors before reads or directory creation.</summary>
    internal static void CheckParents(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            if (current.LinkTarget is not null || (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                throw new IOException("Workspace access cannot traverse a reparse-point directory.");
            }
        }
    }
}
