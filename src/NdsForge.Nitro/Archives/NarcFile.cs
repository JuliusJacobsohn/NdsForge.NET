namespace NdsForge.Nitro.Archives;

/// <summary>Associates one stable NARC allocation identifier with payload bytes and optional FNT naming.</summary>
public sealed class NarcFile
{
    /// <summary>Creates an initially unnamed allocation.</summary>
    internal NarcFile(int id, byte[] data, int originalOffset)
    {
        Id = id;
        Data = data;
        OriginalOffset = originalOffset;
    }

    /// <summary>Gets the zero-based FAT index.</summary>
    public int Id { get; }

    /// <summary>Gets the optional one-byte-per-character filename from the archive FNT.</summary>
    public string? Name { get; private set; }

    /// <summary>Gets the optional slash-prefixed path, or <see langword="null"/> for an unnamed allocation.</summary>
    public string? FullPath { get; private set; }

    /// <summary>Gets an immutable view of the copied file payload.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the named parent directory, or <see langword="null"/> for an unnamed allocation.</summary>
    public NarcDirectory? Parent { get; private set; }

    /// <summary>Locates the payload in the parsed source for same-size preservation writes.</summary>
    internal int OriginalOffset { get; }

    /// <summary>Links optional FNT metadata after all allocation objects exist.</summary>
    internal void SetName(string name, string fullPath, NarcDirectory parent)
    {
        Name = name;
        FullPath = fullPath;
        Parent = parent;
    }
}
