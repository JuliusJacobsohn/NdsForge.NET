using System.Buffers.Binary;

namespace NdsForge;

/// <summary>Collects explicit image changes and saves them without mutating the source.</summary>
public sealed class NdsImageEditor
{
    /// <summary>Remains the immutable byte source and ownership anchor throughout the copy-on-write session.</summary>
    private readonly NdsImage _image;
    /// <summary>Stores caller-independent payload copies keyed by stable FAT ID, with later replacements winning.</summary>
    private readonly Dictionary<int, byte[]> _replacements = [];
    /// <summary>Holds a checksummed banner to overwrite in place or append when its layout grows.</summary>
    private NdsBanner? _bannerReplacement;
    /// <summary>Records named repairs independently from ordinary edits for plan review and precise metadata writes.</summary>
    private NdsRepairKind _repairs;
    /// <summary>Holds the key-derived encrypted-form checksum only after secure-area inspection succeeds.</summary>
    private ushort? _secureAreaCrc;

    /// <summary>Begins a non-mutating session and initializes the restricted header-edit projection from the source.</summary>
    /// <param name="image">Live source image retained for lazy copying and final verification.</param>
    internal NdsImageEditor(NdsImage image)
    {
        _image = image;
        Header = new(image.Header);
    }

    /// <summary>Gets editable identity and card-control header fields.</summary>
    public NdsHeaderEdit Header { get; }

    /// <summary>Gets pending changes in ascending FAT file-ID order.</summary>
    public IReadOnlyList<NdsFileChange> Changes => _replacements
        .OrderBy(static pair => pair.Key)
        .Select(pair => CreateChange(pair.Key, pair.Value))
        .ToArray();

    /// <summary>Snapshots all pending semantic changes and named repairs for review before a destination is opened.</summary>
    public NdsEditPlan Plan => new(Changes, Header.HasChanges, _bannerReplacement is not null, _repairs);

    /// <summary>Replaces a named NitroFS file.</summary>
    /// <param name="path">The canonical or root-relative NitroFS path.</param>
    /// <param name="contents">The replacement bytes, copied immediately.</param>
    /// <returns>This editor for fluent composition.</returns>
    public NdsImageEditor ReplaceFile(string path, ReadOnlySpan<byte> contents) =>
        ReplaceFile(_image.FileSystem.GetFile(path), contents);

    /// <summary>Replaces a named NitroFS file.</summary>
    /// <param name="file">A file belonging to the source image.</param>
    /// <param name="contents">The replacement bytes, copied immediately.</param>
    /// <returns>This editor for fluent composition.</returns>
    public NdsImageEditor ReplaceFile(NdsFile file, ReadOnlySpan<byte> contents)
    {
        ArgumentNullException.ThrowIfNull(file);
        NdsFile sourceFile = _image.FileSystem.GetFile(file.Id);
        if (!ReferenceEquals(sourceFile, file))
        {
            throw new ArgumentException("The file belongs to a different image.", nameof(file));
        }

        return ReplaceAllocation(file.Id, contents);
    }

    /// <summary>Replaces any FAT allocation, including an unnamed overlay payload.</summary>
    /// <param name="fileId">The stable FAT file ID.</param>
    /// <param name="contents">The replacement bytes, copied immediately.</param>
    /// <returns>This editor for fluent composition.</returns>
    public NdsImageEditor ReplaceAllocation(int fileId, ReadOnlySpan<byte> contents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileId);
        if (fileId >= _image.FileSystem.Allocations.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(fileId), "The FAT file ID does not exist.");
        }

        NdsOverlay? authenticated = _image.Arm9Overlays.FirstOrDefault(
            overlay => overlay.IsAuthenticated && overlay.FileId == fileId);
        if (authenticated is not null && _image.Header.Kind == NdsImageKind.NintendoDs)
        {
            throw new InvalidOperationException(
                $"ARM9 overlay {authenticated.Id} has a Download Play authentication record. " +
                "Use NdsImageBuilder.FromImageAsync and ReplaceOverlay so ARM9 recompression and HMAC repair are atomic.");
        }

        _replacements[fileId] = contents.ToArray();
        return this;
    }

    /// <summary>Removes a pending replacement while leaving the source allocation unchanged.</summary>
    /// <param name="fileId">The FAT file ID.</param>
    /// <returns>Whether a pending change was removed.</returns>
    public bool Revert(int fileId) => _replacements.Remove(fileId);

    /// <summary>Replaces or adds the menu banner.</summary>
    /// <param name="banner">The checksummed banner to write.</param>
    /// <returns>This editor.</returns>
    public NdsImageEditor ReplaceBanner(NdsBanner banner)
    {
        ArgumentNullException.ThrowIfNull(banner);
        _bannerReplacement = banner;
        return this;
    }

    /// <summary>Selects only the common header checksum for repair; other damaged checksums remain untouched.</summary>
    /// <returns>This editor.</returns>
    public NdsImageEditor RepairHeaderCrc()
    {
        _repairs |= NdsRepairKind.HeaderCrc;
        return this;
    }

    /// <summary>Selects the dedicated logo checksum and the dependent common header checksum for repair.</summary>
    /// <returns>This editor.</returns>
    public NdsImageEditor RepairNintendoLogoCrc()
    {
        _repairs |= NdsRepairKind.NintendoLogoCrc | NdsRepairKind.HeaderCrc;
        return this;
    }

    /// <summary>Replaces the current banner with a copy whose version-defined CRC slots are repaired in place.</summary>
    /// <returns>This editor.</returns>
    /// <exception cref="InvalidOperationException">The source contains no banner to repair.</exception>
    public NdsImageEditor RepairBannerCrcs()
    {
        NdsBanner banner = _bannerReplacement ?? _image.Banner ??
            throw new InvalidOperationException("The image has no banner checksum fields to repair.");
        _bannerReplacement = banner.WithRepairedCrcs();
        _repairs |= NdsRepairKind.BannerCrcs;
        return this;
    }

    /// <summary>
    /// Selects secure-area CRC repair after the explicit KEY1 table proves whether source bytes are encrypted or
    /// reconstructs the encrypted checksum representation from a decrypted dump.
    /// </summary>
    /// <param name="keyTable">Complete caller-authorized KEY1 seed schedule.</param>
    /// <returns>This editor.</returns>
    /// <exception cref="InvalidOperationException">The interval is absent, malformed, unrecognized, or not checksumable.</exception>
    public NdsImageEditor RepairSecureAreaCrc(NdsKey1KeyTable keyTable)
    {
        ArgumentNullException.ThrowIfNull(keyTable);
        NdsSecureAreaInspection inspection = NdsSecureArea.Inspect(_image, keyTable);
        if (!inspection.IsTransformable || inspection.CalculatedCrc is not ushort calculated)
        {
            throw new InvalidOperationException($"Secure-area state {inspection.State} cannot produce a verified CRC repair.");
        }

        _secureAreaCrc = calculated;
        _repairs |= NdsRepairKind.SecureAreaCrc | NdsRepairKind.HeaderCrc;
        return this;
    }

    /// <summary>Saves to a new or atomically replaced filesystem path.</summary>
    /// <param name="path">The destination image path.</param>
    /// <param name="options">Optional save policies.</param>
    /// <param name="cancellationToken">A token used to cancel copying or verification.</param>
    /// <returns>A summary of the completed save.</returns>
    public async ValueTask<NdsSaveResult> SaveAsync(
        string path,
        NdsWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= NdsWriteOptions.Default;
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
            NdsSaveResult result;
            var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (stream.ConfigureAwait(false))
            {
                result = await SaveAsync(stream, options, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, output, options.OverwriteDestination);
            return result;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Saves to a distinct caller-owned readable, writable, seekable stream.</summary>
    /// <param name="destination">The destination stream, which is left open.</param>
    /// <param name="options">Optional save policies.</param>
    /// <param name="cancellationToken">A token used to cancel copying or verification.</param>
    /// <returns>A summary of the completed save.</returns>
    public async ValueTask<NdsSaveResult> SaveAsync(
        Stream destination,
        NdsWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanRead || !destination.CanWrite || !destination.CanSeek)
        {
            throw new ArgumentException("The destination stream must be readable, writable, and seekable.", nameof(destination));
        }

        options ??= NdsWriteOptions.Default;
        options.Validate();
        NdsDsEditAuthentication.Validate(_image, options, Plan.HasChanges, Header.GameCode, _bannerReplacement is not null || _image.Banner is not null);
        if (Plan.HasChanges || options.DsIntegrity is not null) { NdsDownloadPlaySignatureWriter.ValidateSource(_image); }
        if (Plan.HasChanges || options.DsIntegrity is not null)
        {
            NdsEditRegionProtection.Validate(_image, Changes, _bannerReplacement, Header, options.RelocatedFileAlignment);
        }
        destination.Position = 0;
        destination.SetLength(0);
        using (Stream source = _image.OpenRead(new(0, _image.Length)))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        if (!Plan.HasChanges && options.DsIntegrity is null)
        {
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (options.VerifyOutput) { await VerifyAsync(destination, options, cancellationToken).ConfigureAwait(false); }
            destination.Position = _image.Length;
            return new(0, 0, _image.Header.UsedImageSize, _image.Length);
        }

        var allocations = _image.FileSystem.Allocations
            .Select(static allocation => allocation.Data)
            .ToArray();
        long usedSize = Math.Max(
            _image.Header.UsedImageSize,
            allocations.Length == 0 ? 0 : allocations.Max(static allocation => allocation.End));
        int relocated = 0;
        foreach ((int fileId, byte[] contents) in _replacements.OrderBy(static pair => pair.Key))
        {
            NdsRegion original = allocations[fileId];
            long offset = original.Offset;
            if (contents.LongLength > original.Length)
            {
                offset = Align(usedSize, options.RelocatedFileAlignment);
                await FillGapAsync(destination, offset, options.PaddingByte, cancellationToken).ConfigureAwait(false);
                usedSize = checked(offset + contents.LongLength);
                relocated++;
            }

            destination.Position = offset;
            await destination.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
            allocations[fileId] = new(offset, contents.LongLength);
        }

        uint bannerOffset = _image.Header.BannerOffset;
        if (_bannerReplacement is not null)
        {
            long originalLength = _image.Banner?.RawData.Length ?? 0;
            long offset = bannerOffset;
            if (bannerOffset == 0 || _bannerReplacement.RawData.Length > originalLength)
            {
                offset = Align(usedSize, options.RelocatedFileAlignment);
                await FillGapAsync(destination, offset, options.PaddingByte, cancellationToken).ConfigureAwait(false);
            }

            destination.Position = offset;
            await destination.WriteAsync(_bannerReplacement.RawData, cancellationToken).ConfigureAwait(false);
            usedSize = Math.Max(usedSize, offset + _bannerReplacement.RawData.Length);
            bannerOffset = checked((uint)offset);
        }

        usedSize = Math.Max(usedSize, _image.Header.UsedImageSize);
        long physicalSize = NdsDsEditAuthentication.CompletePhysicalSize(_image, options, allocations, Math.Max(_image.Length, usedSize));
        physicalSize = Math.Max(physicalSize, checked(usedSize + (_image.DownloadPlaySignature?.RawData.Length ?? 0)));
        await FillGapAsync(destination, physicalSize, options.PaddingByte, cancellationToken).ConfigureAwait(false);
        destination.SetLength(physicalSize);
        await NdsDownloadPlaySignatureWriter.WriteAsync(destination, _image.DownloadPlaySignature, usedSize, cancellationToken).ConfigureAwait(false);
        byte[] header = await WriteMetadataAsync(destination, allocations, usedSize, bannerOffset, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<NdsDiagnostic> diagnostics = _image.Header.DsExtended is null
            ? Array.Empty<NdsDiagnostic>()
            : await NdsDsHeaderWriter.FinalizeAsync(destination, header, options.DsIntegrity, cancellationToken).ConfigureAwait(false);
        diagnostics = NdsDownloadPlaySignatureWriter.AppendDiagnostic(diagnostics, _image.DownloadPlaySignature, usedSize);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (options.VerifyOutput)
        {
            await VerifyAsync(destination, options, cancellationToken).ConfigureAwait(false);
        }

        destination.Position = physicalSize;
        return new(_replacements.Count, relocated, usedSize, physicalSize) { Diagnostics = Array.AsReadOnly(diagnostics.ToArray()) };
    }

    /// <summary>Projects internal replacement bytes into public size, path, and relocation metadata without exposing mutable buffers.</summary>
    /// <param name="fileId">FAT index whose original allocation supplies name and length.</param>
    /// <param name="replacement">Private payload copy used only for its length.</param>
    /// <returns>A read-only pending-change description.</returns>
    private NdsFileChange CreateChange(int fileId, byte[] replacement)
    {
        NdsFileAllocation allocation = _image.FileSystem.Allocations[fileId];
        _image.FileSystem.TryGetFile(fileId, out NdsFile? file);
        return new(
            fileId,
            file?.FullPath,
            allocation.Data.Length,
            replacement.LongLength,
            replacement.LongLength > allocation.Data.Length);
    }

    /// <summary>Rewrites synchronized FAT ranges and common-header fields after all payload positions are final.</summary>
    /// <param name="destination">Output stream already containing copied and replaced payload bytes.</param>
    /// <param name="allocations">Final FAT intervals, including unchanged and relocated entries.</param>
    /// <param name="usedSize">Exclusive meaningful image end written to header offset <c>0x80</c>.</param>
    /// <param name="bannerOffset">Original or relocated banner address written to offset <c>0x68</c>.</param>
    /// <param name="cancellationToken">Cancels metadata writes before verification.</param>
    /// <returns>The finalized common header, ready for an explicitly selected authentication policy.</returns>
    private async ValueTask<byte[]> WriteMetadataAsync(
        Stream destination,
        NdsRegion[] allocations,
        long usedSize,
        uint bannerOffset,
        CancellationToken cancellationToken)
    {
        if (usedSize > uint.MaxValue)
        {
            throw new InvalidDataException("The rebuilt image exceeds the Nintendo DS 32-bit address space.");
        }

        byte[] fat = new byte[checked(allocations.Length * 8)];
        for (int fileId = 0; fileId < allocations.Length; fileId++)
        {
            NdsRegion allocation = allocations[fileId];
            BinaryPrimitives.WriteUInt32LittleEndian(fat.AsSpan(fileId * 8), checked((uint)allocation.Offset));
            BinaryPrimitives.WriteUInt32LittleEndian(fat.AsSpan((fileId * 8) + 4), checked((uint)allocation.End));
        }

        destination.Position = _image.Header.FileAllocationTable.Offset;
        await destination.WriteAsync(fat, cancellationToken).ConfigureAwait(false);
        byte[] header = _image.Header.RawData.ToArray();
        Header.Apply(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x68), bannerOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x80), checked((uint)usedSize));
        if (_secureAreaCrc is ushort secureAreaCrc)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x6C), secureAreaCrc);
        }

        if ((_repairs & NdsRepairKind.NintendoLogoCrc) != 0)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                header.AsSpan(0x15C),
                NdsChecksums.ComputeCrc16(header.AsSpan(0xC0, 156)));
        }

        byte capacity = _image.CarrierLayout.Kind == NdsImageCarrier.DigitalSrl
            ? _image.Header.DeviceCapacityExponent : CalculateDeviceCapacity(usedSize, _image.Header.DeviceCapacityExponent);
        header[0x14] = capacity;
        bool commonHeaderChanged = Header.HasChanges ||
            bannerOffset != _image.Header.BannerOffset ||
            usedSize != _image.Header.UsedImageSize ||
            capacity != _image.Header.DeviceCapacityExponent ||
            (_repairs & (NdsRepairKind.HeaderCrc | NdsRepairKind.NintendoLogoCrc | NdsRepairKind.SecureAreaCrc)) != 0;
        if (commonHeaderChanged)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                header.AsSpan(0x15E),
                NdsChecksums.ComputeCrc16(header.AsSpan(0, 0x15E)));
        }
        destination.Position = 0;
        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        return header;
    }

    /// <summary>Reopens the completed stream through production parsers, validates it, and byte-checks every requested change.</summary>
    /// <param name="destination">Readable output left open and repositioned by the verification loader.</param>
    /// <param name="options">Supplies exactly the authentication credentials used for this save.</param>
    /// <param name="cancellationToken">Cancels reparsing or payload comparisons.</param>
    private async ValueTask VerifyAsync(Stream destination, NdsWriteOptions options, CancellationToken cancellationToken)
    {
        destination.Position = 0;
        using NdsImage output = await NdsImage.OpenAsync(
            destination,
            leaveOpen: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var validationOptions = new NdsValidationOptions();
        options.DsIntegrity?.ApplyValidation(validationOptions);
        NdsValidationResult validation = output.Validate(validationOptions);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(
                $"Output verification failed: {string.Join("; ", validation.Diagnostics.Select(static value => value.Message))}");
        }

        foreach ((int fileId, byte[] expected) in _replacements)
        {
            NdsRegion region = output.FileSystem.Allocations[fileId].Data;
            using Stream actual = output.OpenRead(region);
            byte[] observed = new byte[expected.Length];
            await actual.ReadExactlyAsync(observed, cancellationToken).ConfigureAwait(false);
            if (!observed.AsSpan().SequenceEqual(expected))
            {
                throw new InvalidDataException($"Output verification failed for FAT file ID {fileId}.");
            }
        }

        if (_bannerReplacement is not null &&
            (output.Banner is null || !output.Banner.RawData.Span.SequenceEqual(_bannerReplacement.RawData.Span)))
        {
            throw new InvalidDataException("Output verification failed for the banner.");
        }
    }

    /// <summary>Materializes deterministic padding up to an aligned append offset without allocating the entire gap.</summary>
    /// <param name="destination">Output positioned internally at its current physical end.</param>
    /// <param name="targetOffset">Desired payload start; values within current length require no work.</param>
    /// <param name="paddingByte">Repeated byte used for every newly materialized position.</param>
    /// <param name="cancellationToken">Cancels chunked writes.</param>
    private static async ValueTask FillGapAsync(
        Stream destination,
        long targetOffset,
        byte paddingByte,
        CancellationToken cancellationToken)
    {
        if (targetOffset <= destination.Length)
        {
            return;
        }

        destination.Position = destination.Length;
        byte[] buffer = new byte[64 * 1024];
        buffer.AsSpan().Fill(paddingByte);
        long remaining = targetOffset - destination.Length;
        while (remaining > 0)
        {
            int count = (int)Math.Min(buffer.Length, remaining);
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            remaining -= count;
        }
    }

    /// <summary>Raises, but never shrinks, the encoded cartridge-capacity exponent until it contains all meaningful bytes.</summary>
    /// <param name="usedSize">Exclusive meaningful output end.</param>
    /// <param name="original">Source exponent preserved when its declared capacity remains sufficient.</param>
    /// <returns>The smallest non-decreasing exponent whose 128 KiB-scaled capacity covers the output.</returns>
    private static byte CalculateDeviceCapacity(long usedSize, byte original)
    {
        byte exponent = original;
        long capacity = 128L * 1024L << exponent;
        while (capacity < usedSize && exponent < 31)
        {
            exponent++;
            capacity = 128L * 1024L << exponent;
        }

        return exponent;
    }

    /// <summary>Rounds a non-negative image offset upward using the validated power-of-two alignment.</summary>
    /// <param name="value">Current exclusive used end.</param>
    /// <param name="alignment">Positive power of two from <see cref="NdsWriteOptions"/>.</param>
    /// <returns>The first aligned position at or after <paramref name="value"/>.</returns>
    private static long Align(long value, int alignment) =>
        checked((value + alignment - 1) & -alignment);
}
