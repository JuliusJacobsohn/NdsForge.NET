namespace NdsForge;

internal sealed class StreamImageDataSource : IImageDataSource
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public StreamImageDataSource(Stream stream, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("An image stream must be readable and seekable.", nameof(stream));
        }

        _stream = stream;
        _leaveOpen = leaveOpen;
        Length = stream.Length;
    }

    public long Length { get; }

    public int Read(long offset, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        try
        {
            _stream.Position = offset;
            return _stream.Read(destination);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<int> ReadAsync(
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _stream.Position = offset;
            return await _stream.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_leaveOpen)
        {
            _stream.Dispose();
        }

        _gate.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (!_leaveOpen)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
        _disposed = true;
    }
}

