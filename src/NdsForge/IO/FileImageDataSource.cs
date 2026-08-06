using Microsoft.Win32.SafeHandles;

namespace NdsForge;

/// <summary>Uses operating-system positional I/O to keep large ROMs off the managed heap and permit concurrent reads.</summary>
internal sealed class FileImageDataSource : IImageDataSource
{
    /// <summary>Owns the asynchronous random-access handle for the lifetime of the parsed image.</summary>
    private readonly SafeFileHandle _handle;

    /// <summary>Opens an existing image read-only while allowing other readers to share the file.</summary>
    /// <param name="path">Host path retained only by the native file handle, not as mutable library state.</param>
    public FileImageDataSource(string path)
    {
        _handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        Length = RandomAccess.GetLength(_handle);
    }

    /// <summary>Captures the physical file length at open time for stable region validation.</summary>
    public long Length { get; }

    /// <inheritdoc />
    public int Read(long offset, Span<byte> destination) => RandomAccess.Read(_handle, destination, offset);

    /// <inheritdoc />
    public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken) =>
        RandomAccess.ReadAsync(_handle, destination, offset, cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _handle.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _handle.Dispose();
        return ValueTask.CompletedTask;
    }
}
