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
    /// <param name="arm9OverlayAuthentication">Decoded classic-DS Download Play records, or no declaration.</param>
    /// <param name="banner">Optional versioned menu metadata and icon.</param>
    /// <param name="downloadPlaySignature">Optional complete signature trailer at the declared used-image boundary.</param>
    /// <param name="truncatedDownloadPlaySignature">Records an exact identifier whose fixed payload extends past physical EOF.</param>
    internal NdsImage(
        IImageDataSource source,
        NdsHeader header,
        NdsFileSystem fileSystem,
        IReadOnlyList<NdsOverlay> arm9Overlays,
        IReadOnlyList<NdsOverlay> arm7Overlays,
        NdsOverlayAuthenticationTable? arm9OverlayAuthentication,
        NdsBanner? banner,
        NdsDownloadPlaySignature? downloadPlaySignature,
        bool truncatedDownloadPlaySignature)
    {
        _source = source;
        Header = header;
        FileSystem = fileSystem;
        Arm9Overlays = arm9Overlays;
        Arm7Overlays = arm7Overlays;
        Arm9OverlayAuthentication = arm9OverlayAuthentication;
        Banner = banner;
        DownloadPlaySignature = downloadPlaySignature;
        HasTruncatedDownloadPlaySignature = truncatedDownloadPlaySignature;
    }

    /// <summary>Preserves both typed DS/DSi fields and the raw bytes required for checksums and lossless edits.</summary>
    public NdsHeader Header { get; }

    /// <summary>Connects navigable FNT paths with every FAT allocation, including unnamed overlay payloads.</summary>
    public NdsFileSystem FileSystem { get; }

    /// <summary>Gets ARM9 overlays in table order.</summary>
    public IReadOnlyList<NdsOverlay> Arm9Overlays { get; }

    /// <summary>Gets ARM7 overlays in table order.</summary>
    public IReadOnlyList<NdsOverlay> Arm7Overlays { get; }

    /// <summary>Gets the classic-DS ARM9 Download Play authentication table, including malformed declaration state.</summary>
    public NdsOverlayAuthenticationTable? Arm9OverlayAuthentication { get; }

    /// <summary>Gets the parsed menu banner, or <see langword="null"/> when absent.</summary>
    public NdsBanner? Banner { get; }

    /// <summary>Retains a complete opaque signature trailer at <see cref="NdsHeader.UsedImageSize"/>, without claiming cryptographic trust.</summary>
    public NdsDownloadPlaySignature? DownloadPlaySignature { get; }

    /// <summary>Locates the complete post-used trailer without including following capacity padding.</summary>
    public NdsRegion? DownloadPlaySignatureRegion => DownloadPlaySignature is null ? null : new(Header.UsedImageSize, NdsDownloadPlaySignature.ByteLength);

    /// <summary>Allows validation and writers to distinguish a recognized truncated trailer from ordinary trailing bytes.</summary>
    internal bool HasTruncatedDownloadPlaySignature { get; }

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

    /// <summary>Copies an arbitrary validated image interval without materializing it as one managed array.</summary>
    /// <param name="region">Half-open physical interval, commonly obtained from a typed component or allocation.</param>
    /// <param name="destination">Writable caller-owned stream positioned at the desired output location.</param>
    /// <param name="cancellationToken">Cancels bounded source reads and destination writes without closing either owner.</param>
    /// <returns>A task-like value that completes after the requested region has been copied.</returns>
    public async ValueTask CopyToAsync(
        NdsRegion region,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The image-region destination must be writable.", nameof(destination));
        }

        using Stream source = OpenRead(region);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
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
    /// <returns>A task-like value that completes after the complete declared area has been transformed.</returns>
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

    /// <summary>Captures a detached, SHA-256-addressed automation manifest without transferring image ownership.</summary>
    /// <param name="cancellationToken">Cancels hashing of Programs, files, allocations, and the complete image.</param>
    /// <returns>A serialization-stable manifest containing no payload bytes.</returns>
    public ValueTask<NdsImageManifest> CreateManifestAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return NdsImageManifest.CaptureAsync(this, cancellationToken);
    }

    /// <summary>Compares this image with another live image at semantic, numeric-identity, and physical-layout levels.</summary>
    /// <param name="other">Target image that remains caller-owned and live through hashing.</param>
    /// <param name="cancellationToken">Cancels either manifest capture.</param>
    /// <returns>A deterministic structured diff rather than console text or an undifferentiated byte offset.</returns>
    public ValueTask<NdsImageDiff> CompareAsync(
        NdsImage other,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(other);
        return NdsImageComparer.CompareAsync(this, other, cancellationToken);
    }

    /// <summary>Synchronously releases the image source and prevents further payload access.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _source.Dispose();
        _disposed = true;
    }

    /// <summary>Asynchronously releases the image source and prevents further payload access.</summary>
    /// <returns>A task-like value that completes after an asynchronously disposable source has been released.</returns>
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
