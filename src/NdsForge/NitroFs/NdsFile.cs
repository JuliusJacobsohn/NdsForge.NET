namespace NdsForge;

/// <summary>Represents a named NitroFS file.</summary>
public sealed class NdsFile
{
    private readonly IImageDataSource _source;

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

    /// <summary>Gets the stable FAT file ID.</summary>
    public int Id { get; }

    /// <summary>Gets the entry name.</summary>
    public string Name { get; }

    /// <summary>Gets the canonical absolute NitroFS path.</summary>
    public string FullPath { get; }

    /// <summary>Gets the physical data region.</summary>
    public NdsRegion Data { get; }

    /// <summary>Gets the containing directory.</summary>
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

