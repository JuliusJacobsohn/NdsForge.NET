namespace NdsForge;

/// <summary>Materializes one FNT directory record as a navigable node while preserving its encoded child order.</summary>
public sealed class NdsDirectory
{
    /// <summary>Receives child directories only after recursive parsing proves the entire subtable is valid.</summary>
    private IReadOnlyList<NdsDirectory> _directories = Array.Empty<NdsDirectory>();
    /// <summary>Receives named files only after their sequential IDs resolve to valid FAT allocations.</summary>
    private IReadOnlyList<NdsFile> _files = Array.Empty<NdsFile>();

    /// <summary>Creates a node shell before its recursive children are known, allowing parent references to remain stable.</summary>
    /// <param name="id">Encoded ID from <c>0xF000</c>, where the low 12 bits index the FNT main table.</param>
    /// <param name="name">Single FNT segment, or empty text for the synthetic root name.</param>
    /// <param name="fullPath">Canonical absolute path assembled during traversal.</param>
    /// <param name="parent">Owning node, or <see langword="null"/> only for ID <c>0xF000</c>.</param>
    internal NdsDirectory(ushort id, string name, string fullPath, NdsDirectory? parent)
    {
        Id = id;
        Name = name;
        FullPath = fullPath;
        Parent = parent;
    }

    /// <summary>Combines the <c>0xF000</c> directory tag with the main-table record index in its low 12 bits.</summary>
    public ushort Id { get; }

    /// <summary>Gets the entry name, or an empty string for the root.</summary>
    public string Name { get; }

    /// <summary>Uses ordinal slash-separated semantics and represents the root with exactly <c>/</c>.</summary>
    public string FullPath { get; }

    /// <summary>Gets the parent, or <see langword="null"/> for the root.</summary>
    public NdsDirectory? Parent { get; }

    /// <summary>Gets immediate child directories in FNT order.</summary>
    public IReadOnlyList<NdsDirectory> Directories => _directories;

    /// <summary>Gets immediate child files in FNT order.</summary>
    public IReadOnlyList<NdsFile> Files => _files;

    /// <summary>Walks named payloads in encoded depth-first order without allocating a flattened collection.</summary>
    /// <param name="recursive">Whether descendants are included after this node's immediate files.</param>
    /// <returns>A lazy traversal that remains valid while the owning image is live.</returns>
    public IEnumerable<NdsFile> EnumerateFiles(bool recursive = true)
    {
        foreach (NdsFile file in _files)
        {
            yield return file;
        }

        if (recursive)
        {
            foreach (NdsDirectory directory in _directories)
            {
                foreach (NdsFile file in directory.EnumerateFiles())
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>Walks child directory nodes in encoded depth-first order while excluding the current node.</summary>
    /// <param name="recursive">Whether each child's descendants follow that child in the traversal.</param>
    /// <returns>A lazy sequence suitable for hierarchy-aware LINQ queries.</returns>
    public IEnumerable<NdsDirectory> EnumerateDirectories(bool recursive = true)
    {
        foreach (NdsDirectory directory in _directories)
        {
            yield return directory;
            if (recursive)
            {
                foreach (NdsDirectory descendant in directory.EnumerateDirectories())
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>Publishes parsed children atomically after a zero terminator closes this directory subtable.</summary>
    /// <param name="directories">Immediate directory entries in encoded FNT order.</param>
    /// <param name="files">Immediate file entries in encoded FNT order and consecutive file-ID order.</param>
    internal void SetChildren(IReadOnlyList<NdsDirectory> directories, IReadOnlyList<NdsFile> files)
    {
        _directories = directories;
        _files = files;
    }
}
