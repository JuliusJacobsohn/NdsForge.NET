namespace NdsForge;

/// <summary>Presents one validated image region as an independent, seekable, read-only stream.</summary>
/// <remarks>Each instance owns a logical cursor but shares the image source and does not dispose it.</remarks>
internal sealed class ImageSliceStream : Stream
{
    /// <summary>Provides offset reads and remains owned by the containing <see cref="NdsImage"/>.</summary>
    private readonly IImageDataSource _source;
    /// <summary>Translates the stream's zero-based cursor to the corresponding absolute image offsets.</summary>
    private readonly NdsRegion _region;
    /// <summary>Tracks this slice's cursor independently from other streams over the same source.</summary>
    private long _position;

    /// <summary>Binds an independently seekable cursor to a region already proven to lie within the source.</summary>
    /// <param name="source">Shared source whose lifetime must exceed this stream's use.</param>
    /// <param name="region">Half-open byte interval exposed as offsets zero through <c>Length - 1</c>.</param>
    public ImageSliceStream(IImageDataSource source, NdsRegion region)
    {
        _source = source;
        _region = region;
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => _region.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => _position;
        set => _position = ValidatePosition(value);
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        int requested = (int)Math.Min(buffer.Length, Length - _position);
        int count = _source.Read(_region.Offset + _position, buffer[..requested]);
        _position += count;
        return count;
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int requested = (int)Math.Min(buffer.Length, Length - _position);
        int count = await _source.ReadAsync(
            _region.Offset + _position,
            buffer[..requested],
            cancellationToken).ConfigureAwait(false);
        _position += count;
        return count;
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        long position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        return _position = ValidatePosition(position);
    }

    /// <summary>Performs no work because the slice is read-only and has no buffered writes.</summary>
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>Enforces bounded-stream semantics while still allowing the conventional one-past-end position.</summary>
    /// <param name="value">Proposed cursor relative to the slice, not the underlying image.</param>
    /// <returns>The accepted cursor for direct assignment.</returns>
    private long ValidatePosition(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (value > Length)
        {
            throw new IOException("Cannot seek beyond the end of an image region.");
        }

        return value;
    }
}
