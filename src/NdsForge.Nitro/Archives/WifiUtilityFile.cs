namespace NdsForge.Nitro.Archives;

/// <summary>Exposes one utility FAT allocation, whether or not the filename table names it.</summary>
public sealed class WifiUtilityFile
{
    /// <summary>Captures a validated allocation backed by the archive's private byte copy.</summary>
    internal WifiUtilityFile(int id, int offset, ReadOnlyMemory<byte> data)
    {
        Id = id;
        Offset = offset;
        Data = data;
    }

    /// <summary>Gets the zero-based allocation identity, independent from a filename.</summary>
    public int Id { get; }
    /// <summary>Gets the absolute byte offset in the original archive, including for empty entries.</summary>
    public int Offset { get; }
    /// <summary>Gets the complete stored payload; embedded images and compression envelopes remain opaque.</summary>
    public ReadOnlyMemory<byte> Data { get; }
    /// <summary>Gets a lossless Latin-1 filename projection, or null for an unnamed allocation.</summary>
    public string? Name { get; private set; }
    /// <summary>Gets the case-sensitive slash-prefixed path, or null for an unnamed allocation.</summary>
    public string? FullPath { get; private set; }
    /// <summary>Gets the containing directory's native identifier, or null for an unnamed allocation.</summary>
    public ushort? ParentId { get; private set; }

    /// <summary>Assigns a single validated name relationship before publication.</summary>
    internal void SetName(string name, string fullPath, ushort parentId)
    {
        Name = name;
        FullPath = fullPath;
        ParentId = parentId;
    }
}
