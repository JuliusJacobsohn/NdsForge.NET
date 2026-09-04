namespace NdsForge;

/// <summary>
/// Assembles a deterministic Nintendo DS Image from typed Programs, metadata, Banner, logo, and NitroFS tree.
/// </summary>
/// <remarks>
/// This builder describes a new image rather than editing an existing one. Every byte-bearing setter copies
/// caller data, and repeated writes from unchanged state use identical ordering, offsets, padding, and checksums.
/// DSi recipes require both DSi-mode tuples and explicit extended metadata so a unit-code change can never
/// silently emit a partially configured image. Their integrity policy distinguishes homebrew compatibility
/// hashes from absent retail authentication. Digital tuples may explicitly contain no executable bytes when
/// their matching load and entry addresses are nonzero.
/// </remarks>
public sealed class NdsImageBuilder
{
    /// <summary>Stores a validated caller-supplied logo copy; an absent logo remains zeroed for synthetic images.</summary>
    private byte[]? _nintendoLogo;
    /// <summary>Retains a detached copy of carrier-only bytes between the extended header and program storage.</summary>
    private byte[] _postHeaderData = [];

    /// <summary>Stores an explicit TWL reservation; absent bytes request deterministic mirror generation.</summary>
    private byte[] _twlReservedData = [];

    /// <summary>Retains an imported digital title's informational capacity byte independently of output file length.</summary>
    internal byte? ImportedDigitalCapacity { get; set; }

    /// <summary>Retains an imported digital title's unused cartridge checksum when no full checksum interval exists.</summary>
    internal ushort ImportedDigitalSecureCrc { get; set; }

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

    /// <summary>Selects cartridge or digital-SRL storage independently from processor execution mode.</summary>
    public NdsImageCarrier Carrier { get; set; } = NdsImageCarrier.Cartridge;

    /// <summary>Retains opaque carrier material beginning at 0x1000 without interpreting it as program bytes.</summary>
    public ReadOnlyMemory<byte> PostHeaderData => _postHeaderData;

    /// <summary>Supplies exactly 12 KiB of opaque TWL reservation data; empty means generate three copies of image bytes 0x8000–0x8FFF.</summary>
    public ReadOnlyMemory<byte> TwlReservedData => _twlReservedData;

    /// <summary>Copies an explicit TWL reservation for DSi cartridges, or selects generated mirror bytes when empty.</summary>
    /// <param name="data">Exactly 0x3000 bytes, or empty to select deterministic mirror generation.</param>
    /// <returns>This recipe with independently owned reservation bytes.</returns>
    public NdsImageBuilder SetTwlReservedData(ReadOnlySpan<byte> data)
    {
        if (data.Length is not (0 or 0x3000)) { throw new ArgumentException("A TWL reservation must contain exactly 0x3000 bytes or be empty.", nameof(data)); }
        _twlReservedData = data.ToArray();
        return this;
    }

    /// <summary>Copies at most 0x3000 opaque bytes into the independently reserved post-header region.</summary>
    /// <param name="data">Carrier material; an empty span requests a zero-filled reservation.</param>
    /// <returns>This recipe with independently owned carrier bytes.</returns>
    public NdsImageBuilder SetPostHeaderData(ReadOnlySpan<byte> data)
    {
        if (data.Length > 0x3000) { throw new ArgumentException("The post-header reservation is at most 0x3000 bytes.", nameof(data)); }
        _postHeaderData = data.ToArray();
        return this;
    }

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

    /// <summary>Interprets the complete raw <see cref="RegionCode"/> byte using original-DS territory values.</summary>
    public NdsLegacyRegion LegacyRegion
    {
        get => new(RegionCode);
        set => RegionCode = value.RawValue;
    }

    /// <summary>Gets or sets DSi launch-policy bits through the complete raw <see cref="RegionCode"/> byte.</summary>
    public NdsDsiLaunchPolicy DsiLaunchPolicy
    {
        get => (NdsDsiLaunchPolicy)RegionCode;
        set => RegionCode = (byte)((RegionCode & 0xFC) | ((int)value & 0x03));
    }

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

    /// <summary>Preserves or explicitly sets the NAND ROM end in 128 KiB DS or 512 KiB DSi units; zero leaves the boundary unspecified.</summary>
    public ushort NandRomEndUnits { get; set; }

    /// <summary>Preserves or explicitly sets the NAND writable start in mode-dependent units without relocating any saved data.</summary>
    public ushort NandWritableStartUnits { get; set; }

    /// <summary>Supplies an optional debug executable whose physical offset is assigned by the final layout.</summary>
    public NdsDebugProgramDefinition? DebugProgram { get; set; }

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

    /// <summary>Preserves late-generation DS extension bytes, relative SDK pointers, and an explicit authentication write policy.</summary>
    public NdsDsBuildMetadata? DsMetadata { get; set; }

    /// <summary>Retains an opaque stored Download Play trailer at the rebuilt used-image boundary; null explicitly omits it.</summary>
    public NdsDownloadPlaySignature? DownloadPlaySignature { get; set; }

    /// <summary>
    /// Supplies classic-DS Download Play table repair settings. It is required when ARM9 overlay definitions retain
    /// their authentication bit and ignored for DSi images, whose digest hierarchy has separate semantics.
    /// </summary>
    public NdsOverlayAuthenticationBuildOptions? Arm9OverlayAuthentication { get; set; }

    /// <summary>Provides structural NitroFS operations whose stable snapshot becomes the generated FNT and FAT.</summary>
    public NdsFileSystemBuilder FileSystem { get; }

    /// <summary>Supplies optional pre-checksummed menu metadata; static and animated supported versions remain lossless.</summary>
    public NdsBanner? Banner { get; set; }

    /// <summary>Exposes ARM9 Overlay definitions in the exact order used by the generated table.</summary>
    public IReadOnlyList<NdsOverlayDefinition> Arm9Overlays => _arm9Overlays;

    /// <summary>Exposes ARM7 Overlay definitions in the exact order used by the generated table.</summary>
    public IReadOnlyList<NdsOverlayDefinition> Arm7Overlays => _arm7Overlays;

    /// <summary>Adds an Overlay with a private Allocation; the selected layout and authentication policy determine its File ID.</summary>
    /// <param name="overlay">Immutable definition whose payload is already independent from caller buffers.</param>
    /// <returns>The same builder for fluent recipe construction.</returns>
    public NdsImageBuilder AddOverlay(NdsOverlayDefinition overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        (overlay.Processor == NdsProcessor.Arm9 ? _arm9Overlays : _arm7Overlays).Add(overlay);
        return this;
    }

    /// <summary>Replaces one uniquely identified overlay and synchronizes compression flags, stored size, and RAM size.</summary>
    /// <param name="processor">ARM9 or ARM7 namespace containing the runtime identifier.</param>
    /// <param name="id">Runtime overlay identifier, independent from its generated FAT file ID.</param>
    /// <param name="contents">Stored bytes for preservation mode or decoded bytes for explicit compression modes.</param>
    /// <param name="compressionMode">Storage transformation applied before the next build.</param>
    /// <returns>The same builder for fluent recipe editing.</returns>
    public NdsImageBuilder ReplaceOverlay(
        NdsProcessor processor,
        uint id,
        ReadOnlySpan<byte> contents,
        NdsOverlayCompressionMode compressionMode = NdsOverlayCompressionMode.PreserveStorage)
    {
        List<NdsOverlayDefinition> overlays = processor switch
        {
            NdsProcessor.Arm9 => _arm9Overlays,
            NdsProcessor.Arm7 => _arm7Overlays,
            _ => throw new ArgumentOutOfRangeException(nameof(processor), "Only ARM9 and ARM7 have overlay tables."),
        };
        int index = overlays.FindIndex(overlay => overlay.Id == id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"{processor} overlay {id} does not exist in the build recipe.");
        }

        if (overlays.FindIndex(index + 1, overlay => overlay.Id == id) >= 0)
        {
            throw new InvalidDataException($"{processor} overlay ID {id} is ambiguous in the build recipe.");
        }

        NdsOverlayDefinition original = overlays[index];
        byte[] stored;
        uint ramSize;
        uint compressedSize;
        byte flags;
        switch (compressionMode)
        {
            case NdsOverlayCompressionMode.PreserveStorage:
                stored = contents.ToArray();
                ramSize = original.RamSize;
                compressedSize = original.IsCompressed ? checked((uint)stored.Length) : original.CompressedSize;
                flags = original.Flags;
                break;
            case NdsOverlayCompressionMode.Uncompressed:
                stored = contents.ToArray();
                ramSize = checked((uint)stored.Length);
                compressedSize = 0;
                flags = (byte)(original.Flags & ~0x01);
                break;
            case NdsOverlayCompressionMode.Blz:
                if (!NdsForge.Shared.BlzEngine.TryCompress(contents, out stored, uncompressedPrefixLength: 0))
                {
                    throw new InvalidDataException("The replacement overlay does not produce a smaller BLZ payload.");
                }

                ramSize = checked((uint)contents.Length);
                compressedSize = checked((uint)stored.Length);
                flags = (byte)(original.Flags | 0x01);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(compressionMode), compressionMode, "Unknown overlay compression mode.");
        }

        if (compressedSize > 0x00FF_FFFF)
        {
            throw new InvalidDataException("The replacement overlay stored size exceeds its 24-bit table field.");
        }

        if (!original.HasPrivateAllocation)
        {
            FileSystem.SetFile(original.EffectiveLinkedFilePath!, stored);
        }

        overlays[index] = original.WithStorage(stored, ramSize, compressedSize, flags);
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
