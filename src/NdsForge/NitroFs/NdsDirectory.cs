namespace NdsForge;

/// <summary>Represents a NitroFS directory.</summary>
public sealed class NdsDirectory
{
    private IReadOnlyList<NdsDirectory> _directories = Array.Empty<NdsDirectory>();
    private IReadOnlyList<NdsFile> _files = Array.Empty<NdsFile>();

    internal NdsDirectory(ushort id, string name, string fullPath, NdsDirectory? parent)
    {
        Id = id;
        Name = name;
        FullPath = fullPath;
        Parent = parent;
    }

    /// <summary>Gets the directory ID in the range 0xF000 through 0xFFFF.</summary>
    public ushort Id { get; }

    /// <summary>Gets the entry name, or an empty string for the root.</summary>
    public string Name { get; }

    /// <summary>Gets the canonical absolute NitroFS path.</summary>
    public string FullPath { get; }

    /// <summary>Gets the parent, or <see langword="null"/> for the root.</summary>
    public NdsDirectory? Parent { get; }

    /// <summary>Gets immediate child directories in FNT order.</summary>
    public IReadOnlyList<NdsDirectory> Directories => _directories;

    /// <summary>Gets immediate child files in FNT order.</summary>
    public IReadOnlyList<NdsFile> Files => _files;

    internal void SetChildren(IReadOnlyList<NdsDirectory> directories, IReadOnlyList<NdsFile> files)
    {
        _directories = directories;
        _files = files;
    }
}

