namespace NdsForge;

internal interface IImageDataSource : IDisposable, IAsyncDisposable
{
    long Length { get; }

    int Read(long offset, Span<byte> destination);

    ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken);
}

