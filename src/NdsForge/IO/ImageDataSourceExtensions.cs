namespace NdsForge;

internal static class ImageDataSourceExtensions
{
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

