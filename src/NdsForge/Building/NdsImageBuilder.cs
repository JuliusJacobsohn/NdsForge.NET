namespace NdsForge;

/// <summary>
/// Assembles a deterministic Nintendo DS Image from typed Programs, metadata, Banner, logo, and NitroFS tree.
/// </summary>
/// <remarks>
/// This builder describes a new image rather than editing an existing one. Every byte-bearing setter copies
/// caller data, and repeated writes from unchanged state use identical ordering, offsets, padding, and checksums.
/// DSi recipes require both DSi-mode Programs and explicit extended metadata so a unit-code change can never
/// silently emit a partially configured image. Their integrity policy distinguishes homebrew compatibility
/// hashes from absent retail authentication.
/// </remarks>
public sealed class NdsImageBuilder
{
    /// <summary>Stores a validated caller-supplied logo copy; an absent logo remains zeroed for synthetic images.</summary>
    private byte[]? _nintendoLogo;

    /// <summary>Retains ARM9 definitions in caller insertion order, which becomes deterministic table order.</summary>
    private readonly List<NdsOverlayDefinition> _arm9Overlays = [];

    /// <summary>Retains ARM7 definitions in caller insertion order after separating the processor namespaces.</summary>
    private readonly List<NdsOverlayDefinition> _arm7Overlays = [];

    /// <summary>Establishes deterministic identity defaults and an explicit empty NitroFS root for a new Image.</summary>
    public NdsImageBuilder()
    {
        FileSystem = new NdsFileSystemBuilder();
    }

    /// <summary>Selects DS, DSi-enhanced, or DSi-exclusive header and execution semantics for the complete recipe.</summary>
    public NdsImageKind Kind { get; set; } = NdsImageKind.NintendoDs;

    /// <summary>Controls the padded 12-byte printable-ASCII label written at the beginning of the header.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Controls the exact four-character printable-ASCII product code required by the cartridge header.</summary>
    public string GameCode { get; set; } = "####";

    /// <summary>Controls the exact two-character printable-ASCII publisher identifier.</summary>
    public string MakerCode { get; set; } = "00";

    /// <summary>Controls the publisher-defined software revision byte, independently from format versions.</summary>
    public byte Version { get; set; }

    /// <summary>Preserves the raw cartridge encryption seed-selection byte used by secure-area protocols.</summary>
    public byte EncryptionSeedSelect { get; set; }

    /// <summary>Controls the hardware-dependent region byte without interpreting reserved bits.</summary>
    public byte RegionCode { get; set; }

    /// <summary>Controls the complete boot-policy byte at header offset <c>0x1F</c>.</summary>
    public byte AutoStart { get; set; }

    /// <summary>Preserves ROM-control timing and flags used for ordinary cartridge transfers.</summary>
    public uint NormalCardControl { get; set; }

    /// <summary>Preserves ROM-control timing and flags used during secure cartridge transfers.</summary>
    public uint SecureCardControl { get; set; }

    /// <summary>Preserves the timeout applied to secure-area transfers.</summary>
    public ushort SecureTransferTimeout { get; set; }

    /// <summary>Preserves the ARM9 SDK autoload-list address used during runtime initialization.</summary>
    public uint Arm9AutoLoad { get; set; }

    /// <summary>Preserves the ARM7 SDK autoload-list address used during runtime initialization.</summary>
    public uint Arm7AutoLoad { get; set; }

    /// <summary>Preserves the raw 64-bit secure-area disable token across structural rebuilds.</summary>
    public ulong SecureDisable { get; set; }

    /// <summary>Supplies the required primary processor payload and its runtime addresses.</summary>
    public NdsProgramDefinition? Arm9 { get; set; }

    /// <summary>Supplies the required secondary processor payload and its runtime addresses.</summary>
    public NdsProgramDefinition? Arm7 { get; set; }

    /// <summary>Supplies the required ARM9i payload for a DSi recipe; its single header address serves as load and entry.</summary>
    public NdsProgramDefinition? Arm9i { get; set; }

    /// <summary>Supplies the required ARM7i payload for a DSi recipe; its single header address serves as load and entry.</summary>
    public NdsProgramDefinition? Arm7i { get; set; }

    /// <summary>
    /// Supplies DSi service, title, storage, memory-bank, modcrypt, and integrity policy. It must be present exactly
    /// when <see cref="Kind"/> selects an extended image.
    /// </summary>
    public NdsDsiBuildMetadata? DsiMetadata { get; set; }

    /// <summary>Provides structural NitroFS operations whose stable snapshot becomes the generated FNT and FAT.</summary>
    public NdsFileSystemBuilder FileSystem { get; }

    /// <summary>Supplies optional pre-checksummed menu metadata; static and animated supported versions remain lossless.</summary>
    public NdsBanner? Banner { get; set; }

    /// <summary>Exposes ARM9 Overlay definitions in the exact order used by the generated table.</summary>
    public IReadOnlyList<NdsOverlayDefinition> Arm9Overlays => _arm9Overlays;

    /// <summary>Exposes ARM7 Overlay definitions in the exact order used by the generated table.</summary>
    public IReadOnlyList<NdsOverlayDefinition> Arm7Overlays => _arm7Overlays;

    /// <summary>Adds an Overlay whose private Allocation receives a File ID after all named NitroFS files.</summary>
    /// <param name="overlay">Immutable definition whose payload is already independent from caller buffers.</param>
    /// <returns>The same builder for fluent recipe construction.</returns>
    public NdsImageBuilder AddOverlay(NdsOverlayDefinition overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        (overlay.Processor == NdsProcessor.Arm9 ? _arm9Overlays : _arm7Overlays).Add(overlay);
        return this;
    }

    /// <summary>Copies a parsed DS or DSi Image into a detached Build Recipe suitable for structural filesystem changes.</summary>
    /// <remarks>
    /// All Programs, files, private Overlay payloads, and footer bytes are materialized. The returned builder
    /// no longer depends on the source Image and remains usable after that Image is disposed.
    /// </remarks>
    /// <param name="image">Live Image whose logical components and relationships are imported.</param>
    /// <param name="cancellationToken">Cancels potentially large payload reads before a partial recipe is returned.</param>
    /// <returns>A deterministic builder initialized from source semantics rather than source physical Layout.</returns>
    public static ValueTask<NdsImageBuilder> FromImageAsync(
        NdsImage image,
        CancellationToken cancellationToken = default) =>
        NdsImageBuildImporter.ImportAsync(image, cancellationToken);

    /// <summary>Copies the 156-byte encoded cartridge logo block without embedding or sourcing proprietary assets.</summary>
    /// <param name="data">Exactly the native bytes stored at header offsets <c>0xC0</c>-<c>0x15B</c>.</param>
    /// <returns>The same builder for fluent recipe construction.</returns>
    /// <exception cref="ArgumentException">The encoded logo is not exactly 156 bytes.</exception>
    public NdsImageBuilder SetNintendoLogo(ReadOnlySpan<byte> data)
    {
        if (data.Length != 156)
        {
            throw new ArgumentException("The encoded Nintendo DS logo must contain exactly 156 bytes.", nameof(data));
        }

        _nintendoLogo = data.ToArray();
        return this;
    }

    /// <summary>Writes the complete recipe to a caller-owned random-access stream and optionally verifies it by reopening.</summary>
    /// <param name="destination">Readable, writable, seekable stream truncated to the generated image and left open.</param>
    /// <param name="options">Deterministic Layout settings, or <see langword="null"/> for defaults.</param>
    /// <param name="cancellationToken">Cancels component writes or verification before a successful result is returned.</param>
    /// <returns>Final Regions, sizes, and File ID count.</returns>
    public ValueTask<NdsImageBuildResult> WriteAsync(
        Stream destination,
        NdsImageBuildOptions? options = null,
        CancellationToken cancellationToken = default) =>
        NdsImageBuildWriter.WriteAsync(this, destination, options ?? NdsImageBuildOptions.Default, cancellationToken);

    /// <summary>Builds beside a host destination and moves the verified temporary image into place only after success.</summary>
    /// <param name="path">Output path normalized once before any directory or temporary-file operation.</param>
    /// <param name="options">Layout, verification, and explicit existing-destination policy.</param>
    /// <param name="cancellationToken">Cancels writing or verification while leaving an existing destination untouched.</param>
    /// <returns>Final Regions, sizes, and File ID count.</returns>
    public async ValueTask<NdsImageBuildResult> WriteAsync(
        string path,
        NdsImageBuildOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= NdsImageBuildOptions.Default;
        options.Validate();
        string output = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(output) && !options.OverwriteDestination)
        {
            throw new IOException($"Destination already exists: {output}");
        }

        string temporary = output + ".ndsforge-" + Guid.NewGuid().ToString("N");
        try
        {
            NdsImageBuildResult result;
            var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (stream.ConfigureAwait(false))
            {
                result = await WriteAsync(stream, options, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, output, options.OverwriteDestination);
            return result;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Materializes a complete deterministic image for tests, small tools, or APIs that require one contiguous buffer.</summary>
    /// <param name="options">Deterministic Layout settings, or <see langword="null"/> for defaults.</param>
    /// <param name="cancellationToken">Cancels writing or reopen verification.</param>
    /// <returns>Every generated Image byte including alignment padding.</returns>
    public async ValueTask<byte[]> BuildAsync(
        NdsImageBuildOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        await WriteAsync(stream, options, cancellationToken).ConfigureAwait(false);
        return stream.ToArray();
    }

    /// <summary>Exposes the private logo copy to the internal header serializer without allowing external mutation.</summary>
    internal ReadOnlyMemory<byte> NintendoLogo => _nintendoLogo;
}
