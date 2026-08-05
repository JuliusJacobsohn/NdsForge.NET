namespace NdsForge;

internal sealed class ImageSliceStream : Stream
{
    private readonly IImageDataSource _source;
    private readonly NdsRegion _region;
    private long _position;

    public ImageSliceStream(IImageDataSource source, NdsRegion region)
    {
        _source = source;
        _region = region;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _region.Length;

    public override long Position
    {
        get => _position;
        set => _position = ValidatePosition(value);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int requested = (int)Math.Min(buffer.Length, Length - _position);
        int count = _source.Read(_region.Offset + _position, buffer[..requested]);
        _position += count;
        return count;
    }

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

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

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

