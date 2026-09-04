namespace NdsForge.Nitro.Archives;

/// <summary>Represents one linked directory in a NARC filename table.</summary>
public sealed class NarcDirectory
{
    private IReadOnlyList<NarcDirectory> _directories = [];
    private IReadOnlyList<NarcFile> _files = [];

    /// <summary>Creates a directory before its recursively parsed children are published.</summary>
    internal NarcDirectory(ushort id, string name, string fullPath, NarcDirectory? parent)
    {
        Id = id;
        Name = name;
        FullPath = fullPath;
        Parent = parent;
    }

    /// <summary>Gets the encoded directory identifier in the <c>0xF000</c> range.</summary>
    public ushort Id { get; }

    /// <summary>Gets the final path segment, or an empty string for the root.</summary>
    public string Name { get; }

    /// <summary>Gets the canonical slash-delimited archive path.</summary>
    public string FullPath { get; }

    /// <summary>Gets the parent, or <see langword="null"/> for the root.</summary>
    public NarcDirectory? Parent { get; }

    /// <summary>Gets immediate child directories in encoded FNT order.</summary>
    public IReadOnlyList<NarcDirectory> Directories => _directories;

    /// <summary>Gets immediate named files in encoded FNT order.</summary>
    public IReadOnlyList<NarcFile> Files => _files;

    /// <summary>Publishes complete children only after a subtable terminator has been verified.</summary>
    internal void SetChildren(IReadOnlyList<NarcDirectory> directories, IReadOnlyList<NarcFile> files)
    {
        _directories = directories;
        _files = files;
    }
}
