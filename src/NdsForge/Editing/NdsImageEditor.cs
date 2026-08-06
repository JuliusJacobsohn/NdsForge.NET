using System.Buffers.Binary;

namespace NdsForge;

/// <summary>Collects explicit image changes and saves them without mutating the source.</summary>
public sealed class NdsImageEditor
{
    private readonly NdsImage _image;
    private readonly Dictionary<int, byte[]> _replacements = [];
    private NdsBanner? _bannerReplacement;

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
        destination.Position = 0;
        destination.SetLength(0);
        using (Stream source = _image.OpenRead(new(0, _image.Length)))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
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
        long physicalSize = Math.Max(_image.Length, usedSize);
        destination.SetLength(physicalSize);
        await WriteMetadataAsync(destination, allocations, usedSize, bannerOffset, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (options.VerifyOutput)
        {
            await VerifyAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        destination.Position = physicalSize;
        return new(_replacements.Count, relocated, usedSize, physicalSize);
    }

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

    private async ValueTask WriteMetadataAsync(
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
        header[0x14] = CalculateDeviceCapacity(usedSize, _image.Header.DeviceCapacityExponent);
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(0x15E),
            NdsChecksums.ComputeCrc16(header.AsSpan(0, 0x15E)));
        destination.Position = 0;
        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask VerifyAsync(Stream destination, CancellationToken cancellationToken)
    {
        destination.Position = 0;
        using NdsImage output = await NdsImage.OpenAsync(
            destination,
            leaveOpen: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        NdsValidationResult validation = output.Validate();
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

    private static long Align(long value, int alignment) =>
        checked((value + alignment - 1) & -alignment);
}
