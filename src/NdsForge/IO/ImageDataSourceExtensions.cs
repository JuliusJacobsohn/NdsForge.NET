namespace NdsForge;

/// <summary>Builds exact-region parser reads on top of data sources whose primitive operations may legally be short.</summary>
internal static class ImageDataSourceExtensions
{
    /// <summary>Fills an entire span from consecutive absolute offsets or rejects a truncated image.</summary>
    /// <param name="source">Random-access image source.</param>
    /// <param name="offset">Offset of the first requested byte.</param>
    /// <param name="destination">Buffer that must be filled completely for parsing to continue.</param>
    /// <exception cref="EndOfStreamException">The physical source ends before the declared region.</exception>
    public static void ReadExactly(this IImageDataSource source, long offset, Span<byte> destination)
    {
        int totalRead = 0;
        while (totalRead < destination.Length)
        {
            int count = source.Read(offset + totalRead, destination[totalRead..]);
            if (count == 0)
            {
                throw new EndOfStreamException("The image ended before the requested region was read.");
            }

            totalRead += count;
        }
    }

    /// <summary>Fills an entire memory region asynchronously while preserving truncation and cancellation semantics.</summary>
    /// <param name="source">Random-access image source.</param>
    /// <param name="offset">Offset of the first requested byte.</param>
    /// <param name="destination">Buffer that must be filled completely for parsing to continue.</param>
    /// <param name="cancellationToken">Cancels between or during short source reads.</param>
    /// <exception cref="EndOfStreamException">The physical source ends before the declared region.</exception>
    public static async ValueTask ReadExactlyAsync(
        this IImageDataSource source,
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < destination.Length)
        {
            int count = await source.ReadAsync(
                offset + totalRead,
                destination[totalRead..],
                cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                throw new EndOfStreamException("The image ended before the requested region was read.");
            }

            totalRead += count;
        }
    }
}
