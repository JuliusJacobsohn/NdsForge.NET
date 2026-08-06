namespace NdsForge;

/// <summary>
/// Coordinates header, filesystem, overlay, and banner parsers while preserving data-source ownership rules.
/// </summary>
internal static class NdsImageLoader
{
    /// <summary>The original DS header ends at 0x200 even when later image data is aligned farther out.</summary>
    private const int BaseHeaderLength = 0x200;

    /// <summary>DSi-enhanced images extend the parseable header through offset 0x1000.</summary>
    private const int DsiHeaderLength = 0x1000;

    /// <summary>Opens a file-backed source and releases its handle if any parsing stage fails.</summary>
    /// <param name="path">Host-filesystem path to the cartridge image.</param>
    /// <param name="options">Resource limits, or <see langword="null"/> to use safe defaults.</param>
    /// <param name="cancellationToken">Cancels reads without returning a partially initialized image.</param>
    /// <returns>An image that assumes ownership of the file handle on success.</returns>
    public static async ValueTask<NdsImage> OpenPathAsync(
        string path,
        NdsReadOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var source = new FileImageDataSource(path);
        try
        {
            return await LoadAsync(source, options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await source.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Parses a seekable stream while making the returned image responsible for its configured lifetime.</summary>
    /// <param name="stream">Readable, seekable stream whose logical offset zero begins the NDS image.</param>
    /// <param name="leaveOpen">Preserves the caller's stream after image disposal when <see langword="true"/>.</param>
    /// <param name="options">Resource limits, or <see langword="null"/> to use safe defaults.</param>
    /// <returns>A fully parsed random-access image.</returns>
    public static NdsImage OpenStream(Stream stream, bool leaveOpen, NdsReadOptions? options)
    {
        var source = new StreamImageDataSource(stream, leaveOpen);
        try
        {
            return Load(source, options);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>Asynchronously parses a seekable stream without leaving a live wrapper after failure or cancellation.</summary>
    /// <param name="stream">Readable, seekable stream whose logical offset zero begins the NDS image.</param>
    /// <param name="leaveOpen">Preserves the caller's stream after image disposal when <see langword="true"/>.</param>
    /// <param name="options">Resource limits, or <see langword="null"/> to use safe defaults.</param>
    /// <param name="cancellationToken">Cancels every data-source read used during parsing.</param>
    /// <returns>A fully parsed random-access image.</returns>
    public static async ValueTask<NdsImage> OpenStreamAsync(
        Stream stream,
        bool leaveOpen,
        NdsReadOptions? options,
        CancellationToken cancellationToken)
    {
        var source = new StreamImageDataSource(stream, leaveOpen);
        try
        {
            return await LoadAsync(source, options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await source.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Wraps caller-owned bytes without an eager whole-image copy, then parses all structural metadata.</summary>
    /// <param name="data">Complete image memory that must remain unchanged until the image is disposed.</param>
    /// <param name="options">Resource limits, or <see langword="null"/> to use safe defaults.</param>
    /// <returns>An image whose file reads continue to reference <paramref name="data"/>.</returns>
    public static NdsImage LoadMemory(ReadOnlyMemory<byte> data, NdsReadOptions? options)
    {
        var source = new MemoryImageDataSource(data);
        try
        {
            return Load(source, options);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>Runs synchronous component parsers in dependency order against one validated source.</summary>
    /// <param name="source">The source retained by every region and NitroFS file in the resulting object graph.</param>
    /// <param name="options">Limits protecting table-driven allocation and recursion.</param>
    /// <returns>A complete image model sharing the supplied random-access source.</returns>
    private static NdsImage Load(IImageDataSource source, NdsReadOptions? options)
    {
        options ??= NdsReadOptions.Default;
        options.Validate();
        NdsHeader header = ReadHeader(source);
        DetectArm9Footer(source, header);
        NdsFileSystem fileSystem = NitroFileSystemParser.Parse(source, header, options);
        IReadOnlyList<NdsOverlay> arm9Overlays = NdsOverlayParser.Parse(
            source, header.Arm9OverlayTable, NdsProcessor.Arm9, fileSystem, options);
        IReadOnlyList<NdsOverlay> arm7Overlays = NdsOverlayParser.Parse(
            source, header.Arm7OverlayTable, NdsProcessor.Arm7, fileSystem, options);
        NdsBanner? banner = NdsBannerParser.Parse(source, header.BannerOffset, options);
        return new NdsImage(source, header, fileSystem, arm9Overlays, arm7Overlays, banner);
    }

    /// <summary>Runs asynchronous component parsers in dependency order against one validated source.</summary>
    /// <param name="source">The source retained by every region and NitroFS file in the resulting object graph.</param>
    /// <param name="options">Limits protecting table-driven allocation and recursion.</param>
    /// <param name="cancellationToken">Cancels parsing between or during random-access reads.</param>
    /// <returns>A complete image model sharing the supplied random-access source.</returns>
    private static async ValueTask<NdsImage> LoadAsync(
        IImageDataSource source,
        NdsReadOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= NdsReadOptions.Default;
        options.Validate();
        NdsHeader header = await ReadHeaderAsync(source, cancellationToken).ConfigureAwait(false);
        await DetectArm9FooterAsync(source, header, cancellationToken).ConfigureAwait(false);
        NdsFileSystem fileSystem = await NitroFileSystemParser.ParseAsync(
            source, header, options, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<NdsOverlay> arm9Overlays = await NdsOverlayParser.ParseAsync(
            source, header.Arm9OverlayTable, NdsProcessor.Arm9, fileSystem, options, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<NdsOverlay> arm7Overlays = await NdsOverlayParser.ParseAsync(
            source, header.Arm7OverlayTable, NdsProcessor.Arm7, fileSystem, options, cancellationToken)
            .ConfigureAwait(false);
        NdsBanner? banner = await NdsBannerParser.ParseAsync(
            source, header.BannerOffset, options, cancellationToken).ConfigureAwait(false);
        return new NdsImage(source, header, fileSystem, arm9Overlays, arm7Overlays, banner);
    }

    /// <summary>Reads either the 0x200-byte DS header or the 0x1000-byte DSi extension selected at offset 0x12.</summary>
    /// <param name="source">Random-access bytes beginning at cartridge offset zero.</param>
    /// <returns>A parsed header retaining a lossless copy of every header byte read.</returns>
    private static NdsHeader ReadHeader(IImageDataSource source)
    {
        byte[] baseHeader = new byte[BaseHeaderLength];
        source.ReadExactly(0, baseHeader);
        int length = (baseHeader[0x12] & 2) == 0 ? BaseHeaderLength : DsiHeaderLength;
        if (length == BaseHeaderLength)
        {
            return new NdsHeader(baseHeader);
        }

        byte[] fullHeader = new byte[length];
        baseHeader.CopyTo(fullHeader, 0);
        source.ReadExactly(BaseHeaderLength, fullHeader.AsSpan(BaseHeaderLength));
        return new NdsHeader(fullHeader);
    }

    /// <summary>Performs the two-stage header read needed to discover the DSi extension without speculative I/O.</summary>
    /// <param name="source">Random-access bytes beginning at cartridge offset zero.</param>
    /// <param name="cancellationToken">Cancels either stage of the header read.</param>
    /// <returns>A parsed DS or DSi header retaining the bytes used for checksums and lossless editing.</returns>
    private static async ValueTask<NdsHeader> ReadHeaderAsync(
        IImageDataSource source,
        CancellationToken cancellationToken)
    {
        byte[] baseHeader = new byte[BaseHeaderLength];
        await source.ReadExactlyAsync(0, baseHeader, cancellationToken).ConfigureAwait(false);
        int length = (baseHeader[0x12] & 2) == 0 ? BaseHeaderLength : DsiHeaderLength;
        if (length == BaseHeaderLength)
        {
            return new NdsHeader(baseHeader);
        }

        byte[] fullHeader = new byte[length];
        baseHeader.CopyTo(fullHeader, 0);
        await source.ReadExactlyAsync(
            BaseHeaderLength,
            fullHeader.AsMemory(BaseHeaderLength),
            cancellationToken).ConfigureAwait(false);
        return new NdsHeader(fullHeader);
    }

    /// <summary>Recognizes the optional 12-byte SDK footer immediately after ARM9 without treating arbitrary trailing data as one.</summary>
    /// <typeparam name="TSource">Concrete source type, avoiding interface dispatch in this small hot path.</typeparam>
    /// <param name="source">Image bytes used to inspect the footer magic.</param>
    /// <param name="header">Parsed header whose ARM9 model receives the discovered region.</param>
    private static void DetectArm9Footer<TSource>(TSource source, NdsHeader header)
        where TSource : IImageDataSource
    {
        if (header.Arm9.Data.End > source.Length - 12)
        {
            return;
        }

        Span<byte> marker = stackalloc byte[4];
        source.ReadExactly(header.Arm9.Data.End, marker);
        if (NdsBinary.ReadUInt32(marker, 0) == 0xDEC00621)
        {
            header.Arm9.Footer = new(header.Arm9.Data.End, 12);
        }
    }

    /// <summary>Recognizes the optional ARM9 SDK footer using cancellable random-access I/O.</summary>
    /// <typeparam name="TSource">Concrete source type, avoiding interface dispatch in this small hot path.</typeparam>
    /// <param name="source">Image bytes used to inspect the footer magic.</param>
    /// <param name="header">Parsed header whose ARM9 model receives the discovered region.</param>
    /// <param name="cancellationToken">Cancels the marker read before the header is modified.</param>
    private static async ValueTask DetectArm9FooterAsync<TSource>(
        TSource source,
        NdsHeader header,
        CancellationToken cancellationToken)
        where TSource : IImageDataSource
    {
        if (header.Arm9.Data.End > source.Length - 12)
        {
            return;
        }

        byte[] marker = new byte[4];
        await source.ReadExactlyAsync(header.Arm9.Data.End, marker, cancellationToken).ConfigureAwait(false);
        if (NdsBinary.ReadUInt32(marker, 0) == 0xDEC00621)
        {
            header.Arm9.Footer = new(header.Arm9.Data.End, 12);
        }
    }
}
