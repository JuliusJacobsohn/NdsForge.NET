namespace NdsForge;

/// <summary>Projects caller-owned memory as an allocation-free random-access source for small or already-loaded images.</summary>
internal sealed class MemoryImageDataSource : IImageDataSource
{
    /// <summary>Retains the caller's backing storage; the public load contract forbids mutation while in use.</summary>
    private readonly ReadOnlyMemory<byte> _data;

    /// <summary>Captures the memory view without copying the potentially large image payload.</summary>
    /// <param name="data">Complete image bytes whose owner controls the underlying storage lifetime.</param>
    public MemoryImageDataSource(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    /// <summary>Uses the memory view length as the authoritative physical image boundary.</summary>
    public long Length => _data.Length;

    /// <inheritdoc />
    public int Read(long offset, Span<byte> destination)
    {
        int count = GetReadLength(offset, destination.Length);
        _data.Span.Slice(checked((int)offset), count).CopyTo(destination);
        return count;
    }

    /// <inheritdoc />
    public ValueTask<int> ReadAsync(
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(offset, destination.Span));
    }

    /// <summary>Performs no cleanup because the source neither pins nor owns caller memory.</summary>
    public void Dispose()
    {
    }

    /// <summary>Completes immediately because the source neither pins nor owns caller memory.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Clamps a valid request to the remaining memory so interface reads retain normal short-read semantics.</summary>
    /// <param name="offset">Non-negative absolute offset that may equal or exceed the source length.</param>
    /// <param name="requestedLength">Destination capacity in bytes.</param>
    /// <returns>Zero at end of source; otherwise the smaller of capacity and remaining bytes.</returns>
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
