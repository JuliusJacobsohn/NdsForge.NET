namespace NdsForge;

/// <summary>Connects a byte-preserving FNT path and stable FAT identifier to lazily read cartridge bytes.</summary>
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

    /// <summary>
    /// Projects each case-sensitive FNT name byte directly to the same-valued Unicode code point. Conventional ASCII
    /// remains readable, while values <c>0x80</c>-<c>0xFF</c> can be recovered losslessly with Latin-1 encoding.
    /// </summary>
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

    /// <summary>Streams this allocation into a caller-owned destination without buffering the complete file in memory.</summary>
    /// <param name="destination">Writable stream positioned where the first payload byte should be copied.</param>
    /// <param name="cancellationToken">Cancels reads and writes while leaving the destination open.</param>
    /// <returns>A task-like value that completes after the bounded allocation has been copied.</returns>
    public async ValueTask CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The NitroFS file destination must be writable.", nameof(destination));
        }

        using Stream source = OpenRead();
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

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
