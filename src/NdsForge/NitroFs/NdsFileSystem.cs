using System.Diagnostics.CodeAnalysis;

namespace NdsForge;

/// <summary>Provides tree, path, and file-ID access to an image's NitroFS.</summary>
public sealed class NdsFileSystem
{
    /// <summary>Accelerates case-sensitive canonical path lookup without repeatedly walking directory nodes.</summary>
    private readonly Dictionary<string, NdsFile> _filesByPath;
    /// <summary>Resolves FAT identifiers used by overlays while excluding allocations that have no FNT name.</summary>
    private readonly Dictionary<int, NdsFile> _filesById;

    /// <summary>Freezes a successfully traversed FNT together with the complete FAT allocation list.</summary>
    /// <param name="root">Directory ID <c>0xF000</c> from which every named entry is reachable.</param>
    /// <param name="directories">Reachable nodes ordered by numeric directory ID.</param>
    /// <param name="files">Named entries ordered by numeric file ID.</param>
    /// <param name="allocations">Every FAT record, including payloads referenced only by overlays.</param>
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

    /// <summary>Anchors hierarchical traversal at directory ID <c>0xF000</c> and canonical path <c>/</c>.</summary>
    public NdsDirectory Root { get; }

    /// <summary>Gets all reachable directories in directory-ID order.</summary>
    public IReadOnlyList<NdsDirectory> Directories { get; }

    /// <summary>Gets all named files in file-ID order.</summary>
    public IReadOnlyList<NdsFile> Files { get; }

    /// <summary>Gets every FAT allocation, including unnamed overlay payloads.</summary>
    public IReadOnlyList<NdsFileAllocation> Allocations { get; }

    /// <summary>Resolves a case-sensitive logical path without applying host-filesystem normalization.</summary>
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

    /// <summary>Resolves a FAT identifier only when the FNT assigns that allocation a visible name.</summary>
    /// <param name="fileId">The file ID.</param>
    /// <returns>The named file.</returns>
    public NdsFile GetFile(int fileId) => _filesById.TryGetValue(fileId, out NdsFile? file)
        ? file
        : throw new KeyNotFoundException($"No named NitroFS file has ID {fileId}.");

    /// <summary>Allows overlay parsing to attach a named file when its FAT ID also appears in the FNT.</summary>
    /// <param name="fileId">Zero-based FAT index stored in an overlay table entry.</param>
    /// <param name="file">Receives the named file, or <see langword="null"/> for overlay-only allocations.</param>
    /// <returns><see langword="true"/> only when a visible FNT entry uses the ID.</returns>
    internal bool TryGetFile(int fileId, [NotNullWhen(true)] out NdsFile? file) =>
        _filesById.TryGetValue(fileId, out file);

    /// <summary>Accepts convenient root-relative input while rejecting traversal and ambiguous empty segments.</summary>
    /// <param name="path">Logical NitroFS path; backslashes are accepted as input separators for .NET ergonomics.</param>
    /// <returns>A canonical absolute path suitable for ordinal dictionary lookup.</returns>
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
