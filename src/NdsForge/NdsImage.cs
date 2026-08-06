namespace NdsForge;

/// <summary>Provides structured, random-access inspection of a Nintendo DS-family image.</summary>
public sealed class NdsImage : IDisposable, IAsyncDisposable
{
    /// <summary>Backs every lazy file and region read; disposing the image closes it according to the open overload's ownership rules.</summary>
    private readonly IImageDataSource _source;
    /// <summary>Prevents use-after-close while making synchronous and asynchronous disposal idempotent.</summary>
    private bool _disposed;

    /// <summary>Publishes a complete object graph only after all required component parsers succeed.</summary>
    /// <param name="source">Random-access source transferred to this image for lifetime management.</param>
    /// <param name="header">Losslessly parsed DS or DSi header.</param>
    /// <param name="fileSystem">Validated FNT hierarchy and FAT allocations.</param>
    /// <param name="arm9Overlays">ARM9 table entries in encoded order.</param>
    /// <param name="arm7Overlays">ARM7 table entries in encoded order.</param>
    /// <param name="banner">Optional versioned menu metadata and icon.</param>
    internal NdsImage(
        IImageDataSource source,
        NdsHeader header,
        NdsFileSystem fileSystem,
        IReadOnlyList<NdsOverlay> arm9Overlays,
        IReadOnlyList<NdsOverlay> arm7Overlays,
        NdsBanner? banner)
    {
        _source = source;
        Header = header;
        FileSystem = fileSystem;
        Arm9Overlays = arm9Overlays;
        Arm7Overlays = arm7Overlays;
        Banner = banner;
    }

    /// <summary>Preserves both typed DS/DSi fields and the raw bytes required for checksums and lossless edits.</summary>
    public NdsHeader Header { get; }

    /// <summary>Connects navigable FNT paths with every FAT allocation, including unnamed overlay payloads.</summary>
    public NdsFileSystem FileSystem { get; }

    /// <summary>Gets ARM9 overlays in table order.</summary>
    public IReadOnlyList<NdsOverlay> Arm9Overlays { get; }

    /// <summary>Gets ARM7 overlays in table order.</summary>
    public IReadOnlyList<NdsOverlay> Arm7Overlays { get; }

    /// <summary>Gets the parsed menu banner, or <see langword="null"/> when absent.</summary>
    public NdsBanner? Banner { get; }

    /// <summary>Reports physical source bytes, which may exceed the header's used-ROM size because cartridges are capacity padded.</summary>
    public long Length => _source.Length;

    /// <summary>Opens an image from a filesystem path without loading the entire file into memory.</summary>
    /// <param name="path">The image path.</param>
    /// <param name="options">Optional parser resource limits.</param>
    /// <param name="cancellationToken">A token used to cancel header reading.</param>
    /// <returns>The opened image. The caller must dispose it.</returns>
    public static async ValueTask<NdsImage> OpenAsync(
        string path,
        NdsReadOptions? options = null,
        CancellationToken cancellationToken = default)
        => await NdsImageLoader.OpenPathAsync(path, options, cancellationToken).ConfigureAwait(false);

    /// <summary>Opens an image from a caller-supplied readable, seekable stream.</summary>
    /// <param name="stream">The stream positioned anywhere; offset zero is treated as the image start.</param>
    /// <param name="leaveOpen">Whether disposing the image leaves the source stream open.</param>
    /// <param name="options">Optional parser resource limits.</param>
    /// <returns>The opened image. The caller must dispose it.</returns>
    public static NdsImage Open(
        Stream stream,
        bool leaveOpen = false,
        NdsReadOptions? options = null)
        => NdsImageLoader.OpenStream(stream, leaveOpen, options);

    /// <summary>Asynchronously opens an image from a caller-supplied readable, seekable stream.</summary>
    /// <param name="stream">The stream positioned anywhere; offset zero is treated as the image start.</param>
    /// <param name="leaveOpen">Whether disposing the image leaves the source stream open.</param>
    /// <param name="options">Optional parser resource limits.</param>
    /// <param name="cancellationToken">A token used to cancel parsing.</param>
    /// <returns>The opened image. The caller must dispose it.</returns>
    public static async ValueTask<NdsImage> OpenAsync(
        Stream stream,
        bool leaveOpen = false,
        NdsReadOptions? options = null,
        CancellationToken cancellationToken = default)
        => await NdsImageLoader.OpenStreamAsync(stream, leaveOpen, options, cancellationToken).ConfigureAwait(false);

    /// <summary>Loads an image from caller-owned memory.</summary>
    /// <remarks>The memory must not be mutated while the returned image is in use.</remarks>
    /// <param name="data">The complete image data.</param>
    /// <param name="options">Optional parser resource limits.</param>
    /// <returns>The loaded image. The caller must dispose it.</returns>
    public static NdsImage Load(ReadOnlyMemory<byte> data, NdsReadOptions? options = null)
        => NdsImageLoader.LoadMemory(data, options);

    /// <summary>Opens a read-only stream over a validated image region.</summary>
    /// <param name="region">The region to read.</param>
    /// <returns>A seekable stream bounded to the region.</returns>
    public Stream OpenRead(NdsRegion region)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateRegion(region, Length);
        return new ImageSliceStream(_source, region);
    }

    /// <summary>
    /// Reads one declared DSi modcrypt area and writes its symmetric AES-CTR transformation without loading the
    /// region into memory. The supplied context controls key provenance; neither caller-owned destination nor image
    /// is closed after completion.
    /// </summary>
    /// <param name="area">First or second extended-header interval.</param>
    /// <param name="destination">Writable stream positioned where transformed area bytes should begin.</param>
    /// <param name="context">Detached normal-key and HMAC-counter context.</param>
    /// <param name="cancellationToken">Cancels bounded image reads and destination writes.</param>
    public async ValueTask TransformModcryptAreaAsync(
        NdsModcryptArea area,
        Stream destination,
        NdsModcryptContext context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(context);
        NdsDsiHeader dsi = Header.Dsi ??
            throw new InvalidOperationException("A DS-only image does not declare modcrypt areas.");
        NdsRegion region = dsi.GetModcryptArea(area);

        using Stream source = OpenRead(region);
        await NdsModcrypt.TransformAsync(
            source,
            destination,
            region.Length,
            context,
            area,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Safely exports selected image components to a directory.</summary>
    /// <param name="destination">The destination directory.</param>
    /// <param name="options">Optional component, filtering, and overwrite policies.</param>
    /// <param name="cancellationToken">A token used to cancel extraction.</param>
    /// <returns>A summary of files and bytes written.</returns>
    public ValueTask<NdsExtractionResult> ExtractAsync(
        string destination,
        NdsExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        return new NdsImageExtractor(this, destination, options ?? NdsExtractionOptions.Default)
            .ExtractAsync(cancellationToken);
    }

    /// <summary>Begins an explicit, non-mutating edit session for this source image.</summary>
    /// <returns>A new editor with no pending changes.</returns>
    public NdsImageEditor Edit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new(this);
    }

    /// <summary>Validates checksums, component relationships, bounds, and optional DSi authentication fields.</summary>
    /// <param name="options">Optional external trust material; keyless validation never guesses cryptographic provenance.</param>
    /// <returns>All detected diagnostics.</returns>
    public NdsValidationResult Validate(NdsValidationOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        options ??= NdsValidationOptions.Default;
        options.Validate();
        return NdsImageValidator.Validate(this, options);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _source.Dispose();
        _disposed = true;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _source.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    /// <summary>Proves a caller-supplied slice is a non-negative, overflow-safe interval within the physical source.</summary>
    /// <param name="region">Proposed half-open interval in absolute image bytes.</param>
    /// <param name="imageLength">Physical source boundary used instead of mutable header claims.</param>
    private static void ValidateRegion(NdsRegion region, long imageLength)
    {
        if (region.Offset < 0 || region.Length < 0 || region.Offset > imageLength - region.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "The region is outside the image.");
        }
    }

}
