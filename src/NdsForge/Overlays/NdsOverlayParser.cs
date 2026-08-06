namespace NdsForge;

/// <summary>Decodes processor-specific overlay tables and links each record to its actual FAT payload.</summary>
internal static class NdsOverlayParser
{
    /// <summary>Defines the fixed record width used by both ARM9 and ARM7 overlay tables.</summary>
    private const int EntryLength = 32;

    /// <summary>Materializes and parses one complete overlay table after enforcing alignment and entry limits.</summary>
    /// <param name="source">Random-access image containing the table.</param>
    /// <param name="table">Header-declared table interval; an empty interval produces an empty list.</param>
    /// <param name="processor">Namespace assigned to every decoded record.</param>
    /// <param name="fileSystem">FAT/FNT model used for payload resolution.</param>
    /// <param name="options">Maximum record count protecting allocation.</param>
    /// <returns>Entries in their original table order.</returns>
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

    /// <summary>Reads an overlay table asynchronously before applying the same deterministic in-memory decoder.</summary>
    /// <param name="source">Random-access image containing the table.</param>
    /// <param name="table">Header-declared table interval.</param>
    /// <param name="processor">Namespace assigned to every decoded record.</param>
    /// <param name="fileSystem">FAT/FNT model used for payload resolution.</param>
    /// <param name="options">Maximum record count protecting allocation.</param>
    /// <param name="cancellationToken">Cancels the exact table read.</param>
    /// <returns>Entries in their original table order.</returns>
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

    /// <summary>Partitions a previously validated byte buffer into fixed records without per-entry temporary arrays.</summary>
    /// <param name="data">Complete table whose length is divisible by 32.</param>
    /// <param name="processor">Processor identity copied to each model.</param>
    /// <param name="fileSystem">Allocation model used by record constructors.</param>
    /// <returns>A compact array matching encoded entry order.</returns>
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

    /// <summary>Proves the declared length is record-aligned, array-addressable, and below the configured entry ceiling.</summary>
    /// <param name="table">Header-declared overlay interval.</param>
    /// <param name="options">Resource limit applied independently to each processor table.</param>
    /// <returns>The checked 32-bit array length for allocation.</returns>
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
