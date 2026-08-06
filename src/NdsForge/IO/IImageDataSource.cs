namespace NdsForge;

/// <summary>
/// Abstracts offset-based reads so parsers never depend on or mutate a shared stream position.
/// </summary>
/// <remarks>Implementations may wrap files, memory, or caller streams and define ownership through disposal.</remarks>
internal interface IImageDataSource : IDisposable, IAsyncDisposable
{
    /// <summary>Reports the physical byte length used to validate every header-declared region before reading.</summary>
    long Length { get; }

    /// <summary>Reads up to the requested bytes from an absolute image offset without an implicit cursor.</summary>
    /// <param name="offset">Zero-based source offset; callers validate it before dispatch.</param>
    /// <param name="destination">Buffer receiving bytes until it is full or the source ends.</param>
    /// <returns>The number of bytes transferred, which may be shorter than the destination.</returns>
    int Read(long offset, Span<byte> destination);

    /// <summary>Performs the offset-based read asynchronously while preserving the same short-read contract.</summary>
    /// <param name="offset">Zero-based source offset independent of previous calls.</param>
    /// <param name="destination">Buffer receiving bytes until it is full or the source ends.</param>
    /// <param name="cancellationToken">Cancels pending I/O without changing parser state.</param>
    /// <returns>The number of bytes transferred, or zero at end of source.</returns>
    ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken);
}
