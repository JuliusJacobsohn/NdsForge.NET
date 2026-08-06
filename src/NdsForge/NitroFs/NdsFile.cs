namespace NdsForge;

/// <summary>Connects a human-readable FNT path and stable FAT identifier to lazily read cartridge bytes.</summary>
public sealed class NdsFile
{
    /// <summary>Retains the shared image source so opening a file never requires copying its payload during parsing.</summary>
    private readonly IImageDataSource _source;

    /// <summary>Creates one named entry after the parser has reconciled its directory record with a FAT allocation.</summary>
    /// <param name="source">Shared source owned by the containing image.</param>
    /// <param name="id">Zero-based FAT index incremented by FNT file entries.</param>
    /// <param name="name">Single decoded entry segment.</param>
    /// <param name="fullPath">Canonical path assembled from validated ancestors.</param>
    /// <param name="data">Physical payload interval taken from FAT start/end offsets.</param>
    /// <param name="parent">Directory whose subtable declared the entry.</param>
    internal NdsFile(
        IImageDataSource source,
        int id,
        string name,
        string fullPath,
        NdsRegion data,
        NdsDirectory parent)
    {
        _source = source;
        Id = id;
        Name = name;
        FullPath = fullPath;
        Data = data;
        Parent = parent;
    }

    /// <summary>Indexes the FAT allocation and is also the identifier referenced by overlay table entries.</summary>
    public int Id { get; }

    /// <summary>Preserves the case-sensitive ASCII segment stored in the parent directory's FNT subtable.</summary>
    public string Name { get; }

    /// <summary>Identifies the entry with ordinal, slash-delimited semantics such as <c>/data/maps/map.bin</c>.</summary>
    public string FullPath { get; }

    /// <summary>Locates the uncompressed payload through the FAT's half-open start/end offsets.</summary>
    public NdsRegion Data { get; }

    /// <summary>Links back to the directory that assigned this file's name and starting file ID.</summary>
    public NdsDirectory Parent { get; }

    /// <summary>Opens a bounded, seekable stream over the file contents.</summary>
    /// <returns>A read-only stream.</returns>
    public Stream OpenRead() => new ImageSliceStream(_source, Data);

    /// <summary>Reads all file contents into memory.</summary>
    /// <param name="cancellationToken">A token used to cancel reading.</param>
    /// <returns>The complete file contents.</returns>
    public async ValueTask<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
    {
        if (Data.Length > Array.MaxLength)
        {
            throw new IOException($"NitroFS file {FullPath} is too large to materialize as a byte array.");
        }

        byte[] data = new byte[Data.Length];
        await _source.ReadExactlyAsync(Data.Offset, data, cancellationToken).ConfigureAwait(false);
        return data;
    }
}
