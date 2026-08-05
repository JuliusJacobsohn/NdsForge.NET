namespace NdsForge;

/// <summary>Provides structured, random-access inspection of a Nintendo DS-family image.</summary>
public sealed class NdsImage : IDisposable, IAsyncDisposable
{
    private const int BaseHeaderLength = 0x200;
    private const int DsiHeaderLength = 0x1000;
    private readonly IImageDataSource _source;
    private bool _disposed;

    private NdsImage(IImageDataSource source, NdsHeader header, NdsFileSystem fileSystem)
    {
        _source = source;
        Header = header;
        FileSystem = fileSystem;
    }

    /// <summary>Gets the parsed image header.</summary>
    public NdsHeader Header { get; }

    /// <summary>Gets the parsed NitroFS tree and allocations.</summary>
    public NdsFileSystem FileSystem { get; }

    /// <summary>Gets the total length of the source image in bytes.</summary>
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var source = new FileImageDataSource(path);
        try
        {
            options ??= NdsReadOptions.Default;
            options.Validate();
            NdsHeader header = await ReadHeaderAsync(source, cancellationToken).ConfigureAwait(false);
            NdsFileSystem fileSystem = await NitroFileSystemParser.ParseAsync(
                source,
                header,
                options,
                cancellationToken).ConfigureAwait(false);
            return new NdsImage(source, header, fileSystem);
        }
        catch
        {
            await source.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Loads an image from caller-owned memory.</summary>
    /// <remarks>The memory must not be mutated while the returned image is in use.</remarks>
    /// <param name="data">The complete image data.</param>
    /// <param name="options">Optional parser resource limits.</param>
    /// <returns>The loaded image. The caller must dispose it.</returns>
    public static NdsImage Load(ReadOnlyMemory<byte> data, NdsReadOptions? options = null)
    {
        var source = new MemoryImageDataSource(data);
        try
        {
            options ??= NdsReadOptions.Default;
            options.Validate();
            NdsHeader header = ReadHeader(source);
            NdsFileSystem fileSystem = NitroFileSystemParser.Parse(source, header, options);
            return new NdsImage(source, header, fileSystem);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>Opens a read-only stream over a validated image region.</summary>
    /// <param name="region">The region to read.</param>
    /// <returns>A seekable stream bounded to the region.</returns>
    public Stream OpenRead(NdsRegion region)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateRegion(region, Length);
        return new ImageSliceStream(_source, region);
    }

    /// <summary>Validates header checksums and top-level region bounds.</summary>
    /// <returns>All detected diagnostics.</returns>
    public NdsValidationResult Validate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var diagnostics = new List<NdsDiagnostic>();
        ValidateChecksum(diagnostics, "NDS1001", "header", Header.HeaderCrc, Header.RawData.Span[..0x15E], new(0, 0x160));
        ValidateChecksum(diagnostics, "NDS1002", "Nintendo logo", Header.NintendoLogoCrc, Header.RawData.Span.Slice(0xC0, 156), new(0xC0, 156));

        ValidateRegion(diagnostics, "NDS1101", "ARM9 program", Header.Arm9.Data);
        ValidateRegion(diagnostics, "NDS1102", "ARM7 program", Header.Arm7.Data);
        ValidateRegion(diagnostics, "NDS1103", "filename table", Header.FileNameTable);
        ValidateRegion(diagnostics, "NDS1104", "file allocation table", Header.FileAllocationTable);
        ValidateRegion(diagnostics, "NDS1105", "ARM9 overlay table", Header.Arm9OverlayTable);
        ValidateRegion(diagnostics, "NDS1106", "ARM7 overlay table", Header.Arm7OverlayTable);
        if (Header.Arm9i is not null)
        {
            ValidateRegion(diagnostics, "NDS1107", "ARM9i program", Header.Arm9i.Data);
        }

        if (Header.Arm7i is not null)
        {
            ValidateRegion(diagnostics, "NDS1108", "ARM7i program", Header.Arm7i.Data);
        }

        return new NdsValidationResult(diagnostics);
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

    private static void ValidateRegion(NdsRegion region, long imageLength)
    {
        if (region.Offset < 0 || region.Length < 0 || region.Offset > imageLength - region.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "The region is outside the image.");
        }
    }

    private static void ValidateChecksum(
        List<NdsDiagnostic> diagnostics,
        string code,
        string name,
        ushort stored,
        ReadOnlySpan<byte> data,
        NdsRegion region)
    {
        ushort calculated = NdsChecksums.ComputeCrc16(data);
        if (stored != calculated)
        {
            diagnostics.Add(new(
                code,
                NdsDiagnosticSeverity.Error,
                $"The stored {name} CRC is 0x{stored:X4}, but the calculated value is 0x{calculated:X4}.",
                region));
        }
    }

    private void ValidateRegion(
        List<NdsDiagnostic> diagnostics,
        string code,
        string name,
        NdsRegion region)
    {
        if (region.Offset < 0 || region.Length < 0 || region.Offset > Length - region.Length)
        {
            diagnostics.Add(new(
                code,
                NdsDiagnosticSeverity.Error,
                $"The {name} region at 0x{region.Offset:X} with length 0x{region.Length:X} is outside the 0x{Length:X}-byte image.",
                region));
        }
    }
}
