namespace NdsForge;

/// <summary>
/// Assembles a deterministic Nintendo DS Image from typed Programs, metadata, Banner, logo, and NitroFS tree.
/// </summary>
/// <remarks>
/// This builder describes a new image rather than editing an existing one. Every byte-bearing setter copies
/// caller data, and repeated writes from unchanged state use identical ordering, offsets, padding, and checksums.
/// DSi-specific Programs and extended metadata will use the same recipe but are rejected until their integrity
/// layout is fully configured rather than emitting a misleading partially-DSi image.
/// </remarks>
public sealed class NdsImageBuilder
{
    /// <summary>Stores a validated caller-supplied logo copy; an absent logo remains zeroed for synthetic images.</summary>
    private byte[]? _nintendoLogo;

    /// <summary>Establishes deterministic identity defaults and an explicit empty NitroFS root for a new Image.</summary>
    public NdsImageBuilder()
    {
        FileSystem = new NdsFileSystemBuilder();
    }

    /// <summary>Controls the padded 12-byte printable-ASCII label written at the beginning of the header.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Controls the exact four-character printable-ASCII product code required by the cartridge header.</summary>
    public string GameCode { get; set; } = "####";

    /// <summary>Controls the exact two-character printable-ASCII publisher identifier.</summary>
    public string MakerCode { get; set; } = "00";

    /// <summary>Controls the publisher-defined software revision byte, independently from format versions.</summary>
    public byte Version { get; set; }

    /// <summary>Supplies the required primary processor payload and its runtime addresses.</summary>
    public NdsProgramDefinition? Arm9 { get; set; }

    /// <summary>Supplies the required secondary processor payload and its runtime addresses.</summary>
    public NdsProgramDefinition? Arm7 { get; set; }

    /// <summary>Provides structural NitroFS operations whose stable snapshot becomes the generated FNT and FAT.</summary>
    public NdsFileSystemBuilder FileSystem { get; }

    /// <summary>Supplies optional pre-checksummed menu metadata; static and animated supported versions remain lossless.</summary>
    public NdsBanner? Banner { get; set; }

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
