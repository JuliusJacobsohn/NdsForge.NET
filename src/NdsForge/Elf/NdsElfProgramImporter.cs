using System.Buffers;

namespace NdsForge;

/// <summary>
/// Imports executable ARM ELF32 files into builder-ready Program and Overlay definitions. Parsing is deliberately
/// bounded and transactional: callers receive a complete result only after every selected range is validated.
/// </summary>
public static class NdsElfProgramImporter
{
    /// <summary>Imports an ELF already available in memory without retaining or mutating the caller's buffer.</summary>
    /// <param name="data">Complete ELF32 input.</param>
    /// <param name="processor">ARM9, ARM7, ARM9i, or ARM7i segment classification to select.</param>
    /// <param name="options">Optional resource and Overlay policy.</param>
    /// <returns>A detached Program and any decoded Overlay definitions.</returns>
    public static NdsElfImportResult Import(
        ReadOnlyMemory<byte> data,
        NdsProcessor processor,
        NdsElfImportOptions? options = null)
    {
        NdsElfImportOptions effectiveOptions = Snapshot(options);
        ValidateProcessor(processor);
        if (data.Length > effectiveOptions.MaxInputBytes)
        {
            throw new InvalidDataException("The ELF input exceeds the configured allocation limit.");
        }

        NdsElfFile elf = NdsElfParser.Parse(data, effectiveOptions);
        return NdsElfAssembler.Assemble(data, elf, processor, effectiveOptions);
    }

    /// <summary>Reads and imports a filesystem ELF after checking its length before allocation.</summary>
    /// <param name="path">Input file whose contents are consumed without modification.</param>
    /// <param name="processor">Processor and DS/DSi mode whose segments are selected.</param>
    /// <param name="options">Optional resource and Overlay policy.</param>
    /// <param name="cancellationToken">Cancels file reading before a result escapes.</param>
    /// <returns>A detached Program and Overlay result independent from the closed file.</returns>
    public static async ValueTask<NdsElfImportResult> ImportAsync(
        string path,
        NdsProcessor processor,
        NdsElfImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        NdsElfImportOptions effectiveOptions = Snapshot(options);
        ValidateProcessor(processor);
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            return await ImportAsync(stream, processor, leaveOpen: true, effectiveOptions, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Consumes ELF bytes from a stream's current position, including non-seekable sources, under a hard allocation
    /// bound. Stream ownership is explicit and independent from successful parsing.
    /// </summary>
    /// <param name="stream">Readable source positioned at ELF byte zero.</param>
    /// <param name="processor">Processor and DS/DSi mode whose segments are selected.</param>
    /// <param name="leaveOpen">Whether the source remains usable after success, cancellation, or format failure.</param>
    /// <param name="options">Optional resource and Overlay policy.</param>
    /// <param name="cancellationToken">Cancels bounded buffering before parsing.</param>
    /// <returns>A detached Program and Overlay result.</returns>
    public static async ValueTask<NdsElfImportResult> ImportAsync(
        Stream stream,
        NdsProcessor processor,
        bool leaveOpen = false,
        NdsElfImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        NdsElfImportOptions effectiveOptions = Snapshot(options);
        ValidateProcessor(processor);
        try
        {
            if (!stream.CanRead)
            {
                throw new ArgumentException("The ELF source stream must be readable.", nameof(stream));
            }

            byte[] data = await ReadBoundedAsync(stream, effectiveOptions.MaxInputBytes, cancellationToken).ConfigureAwait(false);
            return Import(data, processor, effectiveOptions);
        }
        finally
        {
            if (!leaveOpen)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Buffers a possibly short-reading source while rejecting the first byte beyond the configured limit.</summary>
    /// <param name="source">Validated readable stream.</param>
    /// <param name="limit">Maximum complete input bytes.</param>
    /// <param name="cancellationToken">Cancels every source read.</param>
    /// <returns>An exactly sized independent input array.</returns>
    private static async ValueTask<byte[]> ReadBoundedAsync(
        Stream source,
        long limit,
        CancellationToken cancellationToken)
    {
        if (source.CanSeek && source.Length - source.Position > limit)
        {
            throw new InvalidDataException("The ELF input exceeds the configured allocation limit.");
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using var output = new MemoryStream();
            while (true)
            {
                int count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    return output.ToArray();
                }

                if (output.Length > limit - count)
                {
                    throw new InvalidDataException("The ELF input exceeds the configured allocation limit.");
                }

                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Copies mutable caller policy so concurrent changes cannot alter an import already in progress.</summary>
    /// <param name="options">Optional caller policy, or <see langword="null"/> for fresh defaults.</param>
    /// <returns>A validated operation-local policy snapshot.</returns>
    private static NdsElfImportOptions Snapshot(NdsElfImportOptions? options)
    {
        options ??= NdsElfImportOptions.Default;
        var snapshot = new NdsElfImportOptions
        {
            MaxInputBytes = options.MaxInputBytes,
            MaxProgramBytes = options.MaxProgramBytes,
            MaxProgramHeaders = options.MaxProgramHeaders,
            MaxOverlays = options.MaxOverlays,
            ImportOverlays = options.ImportOverlays,
        };
        snapshot.Validate();
        return snapshot;
    }

    /// <summary>Rejects undefined enum values before their DS/DSi classification can silently select a segment set.</summary>
    /// <param name="processor">Candidate public processor identity.</param>
    private static void ValidateProcessor(NdsProcessor processor)
    {
        if (!Enum.IsDefined(processor))
        {
            throw new ArgumentOutOfRangeException(nameof(processor), processor, "Unknown NDS processor identity.");
        }
    }
}
