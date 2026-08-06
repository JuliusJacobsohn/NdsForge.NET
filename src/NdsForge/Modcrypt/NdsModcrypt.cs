using System.Buffers;

namespace NdsForge;

/// <summary>
/// Applies the DSi AES-CTR transform with little-endian counter advancement. Encryption and decryption are the
/// same operation; overloads support isolated buffers, arbitrary slices, and bounded caller-owned streams.
/// </summary>
public static class NdsModcrypt
{
    /// <summary>Defines the AES-128 key, counter, and keystream block width imposed by the cartridge format.</summary>
    public const int BlockSize = 16;
    /// <summary>Balances asynchronous throughput against bounded working memory for large program regions.</summary>
    private const int StreamBufferSize = 64 * 1024;

    /// <summary>Transforms an independent buffer using one area from an immutable header-derived context.</summary>
    /// <param name="data">Ciphertext or plaintext bytes beginning at <paramref name="byteOffset"/>.</param>
    /// <param name="context">Resolved key material and HMAC-derived counters.</param>
    /// <param name="area">First or second modcrypt area.</param>
    /// <param name="byteOffset">Logical byte offset within that area, allowing correct unaligned slice processing.</param>
    /// <returns>A new array; the input and context remain unchanged.</returns>
    public static byte[] Transform(
        ReadOnlySpan<byte> data,
        NdsModcryptContext context,
        NdsModcryptArea area,
        long byteOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Transform(data, context.Key.Span, context.GetCounter(area).Span, byteOffset);
    }

    /// <summary>Transforms an independent buffer using explicit AES normal-key and initial-counter bytes.</summary>
    /// <param name="data">Ciphertext or plaintext bytes beginning at <paramref name="byteOffset"/>.</param>
    /// <param name="key">Exactly sixteen bytes containing a normal AES key.</param>
    /// <param name="initialCounter">Exactly sixteen bytes in DSi counter order.</param>
    /// <param name="byteOffset">Logical byte offset from the beginning of the encrypted area.</param>
    /// <returns>A transformed copy suitable for encryption or decryption.</returns>
    public static byte[] Transform(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> initialCounter,
        long byteOffset = 0)
    {
        ValidateArguments(key, initialCounter, byteOffset);
        byte[] output = new byte[data.Length];
        using var transform = new NdsModcryptTransform(key, initialCounter, byteOffset);
        transform.Transform(data, output);
        return output;
    }

    /// <summary>
    /// Transforms exactly <paramref name="length"/> bytes without closing either stream. If a late source
    /// truncation occurs, already completed chunks remain in the destination so callers can diagnose progress.
    /// </summary>
    /// <param name="source">Readable stream positioned at the requested area slice.</param>
    /// <param name="destination">Writable stream receiving the same number of transformed bytes.</param>
    /// <param name="length">Exact number of bytes to consume; trailing source bytes are left unread.</param>
    /// <param name="context">Resolved key material and both initial counters.</param>
    /// <param name="area">First or second modcrypt area.</param>
    /// <param name="byteOffset">Logical slice offset within the selected area.</param>
    /// <param name="cancellationToken">Cancels reads, cryptographic chunk processing, or writes.</param>
    public static async ValueTask TransformAsync(
        Stream source,
        Stream destination,
        long length,
        NdsModcryptContext context,
        NdsModcryptArea area,
        long byteOffset = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await TransformAsync(
            source,
            destination,
            length,
            context.Key,
            context.GetCounter(area),
            byteOffset,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Transforms an exact stream interval with explicit normal-key and counter material. This overload is useful
    /// for tooling that obtains counters outside a parsed image while preserving bounded, allocation-stable I/O.
    /// </summary>
    /// <param name="source">Readable stream positioned at the requested encrypted or plaintext slice.</param>
    /// <param name="destination">Distinct writable stream receiving exactly <paramref name="length"/> bytes.</param>
    /// <param name="length">Exact byte count; a shorter source raises <see cref="EndOfStreamException"/>.</param>
    /// <param name="key">Exactly sixteen bytes containing a normal AES key.</param>
    /// <param name="initialCounter">Exactly sixteen initial counter bytes.</param>
    /// <param name="byteOffset">Logical source position relative to the area's first byte.</param>
    /// <param name="cancellationToken">Cancels reads, cryptographic chunk processing, or writes.</param>
    public static async ValueTask TransformAsync(
        Stream source,
        Stream destination,
        long length,
        ReadOnlyMemory<byte> key,
        ReadOnlyMemory<byte> initialCounter,
        long byteOffset = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!source.CanRead)
        {
            throw new ArgumentException("The modcrypt source must be readable.", nameof(source));
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The modcrypt destination must be writable.", nameof(destination));
        }

        if (ReferenceEquals(source, destination))
        {
            throw new ArgumentException("Streaming modcrypt requires distinct source and destination streams.", nameof(destination));
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The transform length cannot be negative.");
        }

        ValidateArguments(key.Span, initialCounter.Span, byteOffset);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        try
        {
            using var transform = new NdsModcryptTransform(key.Span, initialCounter.Span, byteOffset);
            long remaining = length;
            while (remaining != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = (int)Math.Min(remaining, StreamBufferSize);
                await source.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                transform.Transform(buffer.AsSpan(0, count), buffer.AsSpan(0, count));
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                remaining -= count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Rejects ambiguous key widths and negative logical positions before constructing AES state.</summary>
    /// <param name="key">Candidate normal-key bytes.</param>
    /// <param name="initialCounter">Candidate counter bytes.</param>
    /// <param name="byteOffset">Candidate position within the area.</param>
    private static void ValidateArguments(ReadOnlySpan<byte> key, ReadOnlySpan<byte> initialCounter, long byteOffset)
    {
        if (key.Length != BlockSize)
        {
            throw new ArgumentException("A modcrypt AES key must contain exactly sixteen bytes.", nameof(key));
        }

        if (initialCounter.Length != BlockSize)
        {
            throw new ArgumentException("A modcrypt initial counter must contain exactly sixteen bytes.", nameof(initialCounter));
        }

        if (byteOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset), "A modcrypt slice offset cannot be negative.");
        }
    }
}
