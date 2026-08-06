using System.Collections.ObjectModel;
namespace NdsForge;

/// <summary>
/// Models structural NitroFS changes before ROM offsets and file identifiers are assigned.
/// </summary>
/// <remarks>
/// NitroFS stores names in a directory table but stores payload locations in a separate FAT. This
/// builder deliberately works in paths and bytes so callers can add, remove, move, or rename entries
/// without managing either binary table. <see cref="BuildSnapshot"/> assigns identifiers in a stable,
/// ordinal order, making repeated builds from the same logical tree reproducible.
/// </remarks>
public sealed class NdsFileSystemBuilder
{
    /// <summary>
    /// Tracks directories separately so empty directories survive serialization; NitroFS cannot infer
    /// an empty directory from file paths.
    /// </summary>
    private readonly SortedSet<string> _directories = new(StringComparer.Ordinal) { "/" };

    /// <summary>
    /// Maps canonical paths to builder-owned payloads and enforces the one-entry-per-path invariant.
    /// </summary>
    private readonly Dictionary<string, NdsBuildFile> _files = new(StringComparer.Ordinal);

    /// <summary>
    /// Provides a stable view of every directory that will appear in the FNT, including empty ones.
    /// </summary>
    /// <remarks>The root is represented as <c>/</c>; all other values are absolute NitroFS paths.</remarks>
    public IReadOnlyCollection<string> Directories => new ReadOnlyCollection<string>(_directories.ToArray());

    /// <summary>
    /// Provides a path-sorted snapshot of payloads currently destined for the image.
    /// </summary>
    /// <remarks>
    /// Enumeration order is useful for deterministic tooling but is not the final FAT identifier
    /// order; NitroFS requires files to be grouped by their parent directory during serialization.
    /// </remarks>
    public IReadOnlyCollection<NdsBuildFile> Files => new ReadOnlyCollection<NdsBuildFile>(
        _files.Values.OrderBy(static file => file.Path, StringComparer.Ordinal).ToArray());

    /// <summary>Resolves a builder-owned payload so other recipe components can retain its identity across path moves.</summary>
    /// <param name="path">Canonical or root-relative NitroFS file path.</param>
    /// <returns>The stable payload object whose <see cref="NdsBuildFile.Path"/> follows later move operations.</returns>
    /// <exception cref="FileNotFoundException">No file exists at the normalized path.</exception>
    public NdsBuildFile GetFile(string path)
    {
        string normalized = NormalizePath(path, allowRoot: false);
        return _files.TryGetValue(normalized, out NdsBuildFile? file)
            ? file
            : throw new FileNotFoundException($"NitroFS file was not found: {normalized}", normalized);
    }

    /// <summary>
    /// Declares a directory, retaining it even when no descendant files are added.
    /// </summary>
    /// <param name="path">
    /// An absolute or root-relative NitroFS path. Separators are normalized to <c>/</c>; each segment
    /// must be 1-127 ASCII characters and may not be <c>.</c> or <c>..</c>.
    /// </param>
    /// <returns>The same builder, allowing several tree edits to be chained.</returns>
    /// <exception cref="IOException">A file occupies the directory or one of its parent paths.</exception>
    public NdsFileSystemBuilder CreateDirectory(string path)
    {
        string normalized = NormalizePath(path, allowRoot: true);
        EnsureParents(normalized);
        _directories.Add(normalized);
        return this;
    }

    /// <summary>
    /// Adds a payload that must not already exist, creating its parent directories as needed.
    /// </summary>
    /// <param name="path">An absolute or root-relative path subject to NitroFS ASCII name limits.</param>
    /// <param name="contents">
    /// Exact uncompressed payload bytes. They are copied before this method returns and may therefore
    /// come from stack memory, pooled storage, or a mutable caller buffer.
    /// </param>
    /// <returns>The same builder, allowing several tree edits to be chained.</returns>
    /// <exception cref="IOException">The path is occupied or a parent component is a file.</exception>
    public NdsFileSystemBuilder AddFile(string path, ReadOnlySpan<byte> contents)
    {
        string normalized = NormalizePath(path, allowRoot: false);
        if (_files.ContainsKey(normalized) || _directories.Contains(normalized))
        {
            throw new IOException($"NitroFS entry already exists: {normalized}");
        }

        string parent = GetParent(normalized);
        EnsureParents(parent);
        _directories.Add(parent);
        _files.Add(normalized, new(normalized, contents.ToArray()));
        return this;
    }

    /// <summary>
    /// Defines the payload at a path, replacing an existing file while preserving directory validity.
    /// </summary>
    /// <param name="path">An absolute or root-relative path subject to NitroFS ASCII name limits.</param>
    /// <param name="contents">Exact uncompressed bytes, copied immediately into builder-owned memory.</param>
    /// <returns>The same builder, allowing several tree edits to be chained.</returns>
    /// <exception cref="IOException">The path names a directory or a parent component is a file.</exception>
    public NdsFileSystemBuilder SetFile(string path, ReadOnlySpan<byte> contents)
    {
        string normalized = NormalizePath(path, allowRoot: false);
        if (_directories.Contains(normalized))
        {
            throw new IOException($"A directory already exists at {normalized}.");
        }

        string parent = GetParent(normalized);
        EnsureParents(parent);
        _directories.Add(parent);
        _files[normalized] = new(normalized, contents.ToArray());
        return this;
    }

    /// <summary>
    /// Removes a payload while leaving its parent directories available for later files or empty output.
    /// </summary>
    /// <param name="path">The absolute or root-relative path of the existing file.</param>
    /// <returns>The same builder, allowing several tree edits to be chained.</returns>
    /// <exception cref="FileNotFoundException">No file exists at the normalized path.</exception>
    public NdsFileSystemBuilder RemoveFile(string path)
    {
        string normalized = NormalizePath(path, allowRoot: false);
        if (!_files.Remove(normalized))
        {
            throw new FileNotFoundException($"NitroFS file was not found: {normalized}", normalized);
        }

        return this;
    }

    /// <summary>
    /// Changes a file's NitroFS identity without copying or transforming its payload.
    /// </summary>
    /// <param name="sourcePath">The absolute or root-relative path of the existing payload.</param>
    /// <param name="destinationPath">A free destination whose parent chain contains only directories.</param>
    /// <returns>The same builder; existing references to the moved <see cref="NdsBuildFile"/> observe its new path.</returns>
    /// <exception cref="FileNotFoundException">The source does not identify a file.</exception>
    /// <exception cref="IOException">The destination or one of its parent paths is occupied incompatibly.</exception>
    public NdsFileSystemBuilder MoveFile(string sourcePath, string destinationPath)
    {
        string source = NormalizePath(sourcePath, allowRoot: false);
        string destination = NormalizePath(destinationPath, allowRoot: false);
        if (!_files.TryGetValue(source, out NdsBuildFile? file))
        {
            throw new FileNotFoundException($"NitroFS file was not found: {source}", source);
        }

        if (_files.ContainsKey(destination) || _directories.Contains(destination))
        {
            throw new IOException($"NitroFS entry already exists: {destination}");
        }

        string parent = GetParent(destination);
        EnsureParents(parent);
        _directories.Add(parent);
        _files.Remove(source);
        file.Path = destination;
        _files.Add(destination, file);
        return this;
    }

    /// <summary>
    /// Re-roots an entire directory subtree while preserving every payload byte and relative child path.
    /// </summary>
    /// <param name="sourcePath">An existing directory other than the immutable NitroFS root.</param>
    /// <param name="destinationPath">A free path outside the source subtree.</param>
    /// <returns>The same builder; file objects already obtained from <see cref="Files"/> reflect their rewritten paths.</returns>
    /// <exception cref="DirectoryNotFoundException">The source directory does not exist.</exception>
    /// <exception cref="IOException">The move would create a cycle or collide with an existing entry.</exception>
    public NdsFileSystemBuilder MoveDirectory(string sourcePath, string destinationPath)
    {
        string source = NormalizePath(sourcePath, allowRoot: false);
        string destination = NormalizePath(destinationPath, allowRoot: false);
        if (!_directories.Contains(source))
        {
            throw new DirectoryNotFoundException($"NitroFS directory was not found: {source}");
        }

        if (destination.StartsWith(source + "/", StringComparison.Ordinal))
        {
            throw new IOException("A directory cannot be moved into its own subtree.");
        }

        if (_directories.Contains(destination) || _files.ContainsKey(destination))
        {
            throw new IOException($"NitroFS entry already exists: {destination}");
        }

        EnsureParents(GetParent(destination));
        string[] affectedDirectories = _directories
            .Where(path => path == source || path.StartsWith(source + "/", StringComparison.Ordinal))
            .ToArray();
        NdsBuildFile[] affectedFiles = _files.Values
            .Where(file => file.Path.StartsWith(source + "/", StringComparison.Ordinal))
            .ToArray();
        foreach (string directory in affectedDirectories)
        {
            _directories.Remove(directory);
        }

        foreach (NdsBuildFile file in affectedFiles)
        {
            _files.Remove(file.Path);
        }

        foreach (string directory in affectedDirectories)
        {
            _directories.Add(destination + directory[source.Length..]);
        }

        foreach (NdsBuildFile file in affectedFiles)
        {
            file.Path = destination + file.Path[source.Length..];
            _files.Add(file.Path, file);
        }

        return this;
    }

    /// <summary>
    /// Omits an explicitly declared directory after proving that no descendants would become orphaned.
    /// </summary>
    /// <param name="path">An existing non-root directory path.</param>
    /// <returns>The same builder, allowing several tree edits to be chained.</returns>
    /// <exception cref="DirectoryNotFoundException">The normalized path is not a directory.</exception>
    /// <exception cref="IOException">The directory still contains a file or child directory.</exception>
    public NdsFileSystemBuilder RemoveDirectory(string path)
    {
        string normalized = NormalizePath(path, allowRoot: false);
        if (!_directories.Contains(normalized))
        {
            throw new DirectoryNotFoundException($"NitroFS directory was not found: {normalized}");
        }

        string prefix = normalized + "/";
        if (_directories.Any(value => value.StartsWith(prefix, StringComparison.Ordinal)) ||
            _files.Keys.Any(value => value.StartsWith(prefix, StringComparison.Ordinal)))
        {
            throw new IOException($"NitroFS directory is not empty: {normalized}");
        }

        _directories.Remove(normalized);
        return this;
    }

    /// <summary>
    /// Freezes the logical tree into an FNT and the exact payload order required by the corresponding FAT.
    /// </summary>
    /// <param name="firstFileId">FAT identifier reserved for the first named file after any profile-owned hidden allocations.</param>
    /// <returns>Immutable build inputs whose IDs and byte representation are deterministic.</returns>
    /// <exception cref="InvalidDataException">The tree exceeds the directory or 16-bit file-ID space.</exception>
    internal NdsFileSystemBuildSnapshot BuildSnapshot(int firstFileId = 0)
    {
        string[] directories = _directories.Order(StringComparer.Ordinal).ToArray();
        if (directories.Length > 4096 || firstFileId < 0 || firstFileId + _files.Count > ushort.MaxValue + 1)
        {
            throw new InvalidDataException("NitroFS exceeds its 12-bit directory or 16-bit file-ID space.");
        }

        return NdsFileNameTableWriter.Write(directories, _files.Values.ToArray(), firstFileId);
    }

    /// <summary>
    /// Materializes a validated parent chain without permitting a path to be both file and directory.
    /// </summary>
    /// <param name="path">A normalized absolute directory path.</param>
    /// <exception cref="IOException">A payload occupies any requested directory component.</exception>
    private void EnsureParents(string path)
    {
        if (path == "/")
        {
            return;
        }

        if (_files.ContainsKey(path))
        {
            throw new IOException($"A file already occupies required directory path {path}.");
        }

        string parent = GetParent(path);
        EnsureParents(parent);
        _directories.Add(parent);
    }

    /// <summary>
    /// Converts caller-facing paths to the single representation used for collision and ordering rules.
    /// </summary>
    /// <param name="path">A root-relative or absolute logical path.</param>
    /// <param name="allowRoot">Whether <c>/</c> is a meaningful value for the requested operation.</param>
    /// <returns>A slash-delimited absolute path containing only valid NitroFS name bytes.</returns>
    /// <exception cref="ArgumentException">The path is empty, ambiguous, traversing, non-ASCII, or exceeds a segment limit.</exception>
    internal static string NormalizePath(string path, bool allowRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        if ((!allowRoot && normalized == "/") ||
            (normalized.Length > 1 && normalized.EndsWith('/')) ||
            normalized.Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("NitroFS path is empty or ambiguous.", nameof(path));
        }

        foreach (string segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or ".." || segment.Length > 127 ||
                segment.Any(static value => value > 0x7F || value is '/' or '\\'))
            {
                throw new ArgumentException("NitroFS path contains an invalid ASCII name.", nameof(path));
            }
        }

        return normalized;
    }

    /// <summary>Finds the canonical parent without applying host-platform path semantics.</summary>
    /// <param name="path">A normalized absolute NitroFS path.</param>
    /// <returns><c>/</c> for a root child; otherwise the prefix preceding the final slash.</returns>
    private static string GetParent(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator == 0 ? "/" : path[..separator];
    }
}
