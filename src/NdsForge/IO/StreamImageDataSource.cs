namespace NdsForge;

/// <summary>Adapts a caller stream to positional reads while serializing access to its unavoidable shared cursor.</summary>
internal sealed class StreamImageDataSource : IImageDataSource
{
    /// <summary>References the validated readable, seekable stream supplied by the caller.</summary>
    private readonly Stream _stream;
    /// <summary>Determines whether image disposal also transfers disposal to the underlying stream.</summary>
    private readonly bool _leaveOpen;
    /// <summary>Protects each seek-and-read pair so concurrent file streams cannot race the shared position.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>Prevents reads after teardown and makes repeated synchronous or asynchronous disposal harmless.</summary>
    private bool _disposed;

    /// <summary>Validates random-access prerequisites and snapshots the stream length without changing its position.</summary>
    /// <param name="stream">Caller stream; its current position is deliberately ignored by image addressing.</param>
    /// <param name="leaveOpen">Whether the caller retains lifetime ownership after the image is disposed.</param>
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

    /// <summary>Captures the stream length at construction so later external position changes are irrelevant.</summary>
    public long Length { get; }

    /// <summary>Detects direct attempts to overwrite the same caller stream that supplies lazy image reads.</summary>
    internal bool UsesStream(Stream stream) => ReferenceEquals(_stream, stream);

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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
