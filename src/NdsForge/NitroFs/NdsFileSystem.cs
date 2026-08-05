using System.Diagnostics.CodeAnalysis;

namespace NdsForge;

/// <summary>Provides tree, path, and file-ID access to an image's NitroFS.</summary>
public sealed class NdsFileSystem
{
    private readonly Dictionary<string, NdsFile> _filesByPath;
    private readonly Dictionary<int, NdsFile> _filesById;

    internal NdsFileSystem(
        NdsDirectory root,
        IReadOnlyList<NdsDirectory> directories,
        IReadOnlyList<NdsFile> files,
        IReadOnlyList<NdsFileAllocation> allocations)
    {
        Root = root;
        Directories = directories;
        Files = files;
        Allocations = allocations;
        _filesByPath = files.ToDictionary(static file => file.FullPath, StringComparer.Ordinal);
        _filesById = files.ToDictionary(static file => file.Id);
    }

    /// <summary>Gets the root directory.</summary>
    public NdsDirectory Root { get; }

    /// <summary>Gets all reachable directories in directory-ID order.</summary>
    public IReadOnlyList<NdsDirectory> Directories { get; }

    /// <summary>Gets all named files in file-ID order.</summary>
    public IReadOnlyList<NdsFile> Files { get; }

    /// <summary>Gets every FAT allocation, including unnamed overlay payloads.</summary>
    public IReadOnlyList<NdsFileAllocation> Allocations { get; }

    /// <summary>Gets a named file by canonical or root-relative path.</summary>
    /// <param name="path">The NitroFS path.</param>
    /// <returns>The named file.</returns>
    public NdsFile GetFile(string path)
    {
        string normalized = NormalizePath(path);
        return _filesByPath.TryGetValue(normalized, out NdsFile? file)
            ? file
            : throw new FileNotFoundException($"NitroFS file '{normalized}' was not found.", normalized);
    }

    /// <summary>Attempts to find a named file by canonical or root-relative path.</summary>
    /// <param name="path">The NitroFS path.</param>
    /// <param name="file">Receives the named file when found.</param>
    /// <returns><see langword="true"/> when the file exists.</returns>
    public bool TryGetFile(string path, [NotNullWhen(true)] out NdsFile? file) =>
        _filesByPath.TryGetValue(NormalizePath(path), out file);

    /// <summary>Gets a named file by stable FAT file ID.</summary>
    /// <param name="fileId">The file ID.</param>
    /// <returns>The named file.</returns>
    public NdsFile GetFile(int fileId) => _filesById.TryGetValue(fileId, out NdsFile? file)
        ? file
        : throw new KeyNotFoundException($"No named NitroFS file has ID {fileId}.");

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        if (normalized.EndsWith('/') ||
            normalized.Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("NitroFS paths must identify a file and contain no empty segments.", nameof(path));
        }

        foreach (string segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
            {
                throw new ArgumentException("NitroFS paths cannot contain traversal segments.", nameof(path));
            }
        }

        return normalized;
    }
}
