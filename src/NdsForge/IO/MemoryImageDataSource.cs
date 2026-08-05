namespace NdsForge;

internal sealed class MemoryImageDataSource : IImageDataSource
{
    private readonly ReadOnlyMemory<byte> _data;

    public MemoryImageDataSource(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    public long Length => _data.Length;

    public int Read(long offset, Span<byte> destination)
    {
        int count = GetReadLength(offset, destination.Length);
        _data.Span.Slice(checked((int)offset), count).CopyTo(destination);
        return count;
    }

    public ValueTask<int> ReadAsync(
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(offset, destination.Span));
    }

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private int GetReadLength(long offset, int requestedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset >= _data.Length)
        {
            return 0;
        }

        return (int)Math.Min(requestedLength, _data.Length - offset);
    }
}

