namespace NdsForge;

internal static class NdsOverlayParser
{
    private const int EntryLength = 32;

    public static IReadOnlyList<NdsOverlay> Parse(
        IImageDataSource source,
        NdsRegion table,
        NdsProcessor processor,
        NdsFileSystem fileSystem,
        NdsReadOptions options)
    {
        int length = ValidateLength(table, options);
        byte[] data = new byte[length];
        source.ReadExactly(table.Offset, data);
        return ParseEntries(data, processor, fileSystem);
    }

    public static async ValueTask<IReadOnlyList<NdsOverlay>> ParseAsync(
        IImageDataSource source,
        NdsRegion table,
        NdsProcessor processor,
        NdsFileSystem fileSystem,
        NdsReadOptions options,
        CancellationToken cancellationToken)
    {
        int length = ValidateLength(table, options);
        byte[] data = new byte[length];
        await source.ReadExactlyAsync(table.Offset, data, cancellationToken).ConfigureAwait(false);
        return ParseEntries(data, processor, fileSystem);
    }

    private static NdsOverlay[] ParseEntries(
        ReadOnlySpan<byte> data,
        NdsProcessor processor,
        NdsFileSystem fileSystem)
    {
        var overlays = new NdsOverlay[data.Length / EntryLength];
        for (int index = 0; index < overlays.Length; index++)
        {
            overlays[index] = new(processor, data.Slice(index * EntryLength, EntryLength), fileSystem);
        }

        return overlays;
    }

    private static int ValidateLength(NdsRegion table, NdsReadOptions options)
    {
        if (table.Length % EntryLength != 0 ||
            table.Length / EntryLength > options.MaximumOverlayCount ||
            table.Length > Array.MaxLength)
        {
            throw new InvalidDataException("An overlay table has an invalid or excessive length.");
        }

        return checked((int)table.Length);
    }
}
