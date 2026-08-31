namespace NdsForge.Nitro.Archives;

/// <summary>Exposes a bounded utility directory and its immediate file and directory identities.</summary>
public sealed class WifiUtilityDirectory
{
    /// <summary>Creates one graph node before its validated children are assigned.</summary>
    internal WifiUtilityDirectory(ushort id, ushort? parentId, string name, string fullPath, ushort firstFileId, int nameSubtableOffset)
    {
        Id = id;
        ParentId = parentId;
        Name = name;
        FullPath = fullPath;
        FirstFileId = firstFileId;
        NameSubtableOffset = nameSubtableOffset;
    }

    /// <summary>Gets the native identifier from 0xF000 through 0xFFFF.</summary>
    public ushort Id { get; }
    /// <summary>Gets the parent identifier, or null for the root.</summary>
    public ushort? ParentId { get; }
    /// <summary>Gets the lossless Latin-1 final path segment, empty for the root.</summary>
    public string Name { get; }
    /// <summary>Gets the case-sensitive slash-prefixed path; the root is '/'.</summary>
    public string FullPath { get; }
    /// <summary>Gets the first file identity declared by the directory record, even when the directory has no files.</summary>
    public ushort FirstFileId { get; }
    /// <summary>Gets the original subtable offset relative to the filename table.</summary>
    public int NameSubtableOffset { get; }
    /// <summary>Gets immediate file identities in encoded directory-entry order.</summary>
    public IReadOnlyList<int> FileIds { get; private set; } = [];
    /// <summary>Gets immediate subdirectory identities in encoded directory-entry order.</summary>
    public IReadOnlyList<ushort> ChildIds { get; private set; } = [];

    /// <summary>Publishes read-only child arrays after the terminating directory entry is found.</summary>
    internal void SetChildren(List<int> files, List<ushort> directories)
    {
        FileIds = files.AsReadOnly();
        ChildIds = directories.AsReadOnly();
    }
}
