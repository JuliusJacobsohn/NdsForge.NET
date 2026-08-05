using Microsoft.Win32.SafeHandles;

namespace NdsForge;

internal sealed class FileImageDataSource : IImageDataSource
{
    private readonly SafeFileHandle _handle;

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

    public long Length { get; }

    public int Read(long offset, Span<byte> destination) => RandomAccess.Read(_handle, destination, offset);

    public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken) =>
        RandomAccess.ReadAsync(_handle, destination, offset, cancellationToken);

    public void Dispose() => _handle.Dispose();

    public ValueTask DisposeAsync()
    {
        _handle.Dispose();
        return ValueTask.CompletedTask;
    }
}

