using System.Buffers.Binary;
using System.Text;

namespace NdsForge;

/// <summary>Calculates one immutable Layout, serializes its interdependent tables, and commits components in offset order.</summary>
internal static class NdsImageBuildWriter
{
    /// <summary>Validates a Build Recipe, assigns every Region, writes synchronized metadata, and optionally reopens the result.</summary>
    /// <param name="builder">Caller-owned recipe whose byte-bearing members already own stable copies.</param>
    /// <param name="destination">Readable, writable, seekable output truncated before the first write.</param>
    /// <param name="options">Validated alignment, padding, and verification policy.</param>
    /// <param name="cancellationToken">Cancels sequential writes and production-parser verification.</param>
    /// <returns>The final concrete Layout report.</returns>
    public static async ValueTask<NdsImageBuildResult> WriteAsync(
        NdsImageBuilder builder,
        Stream destination,
        NdsImageBuildOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanRead || !destination.CanWrite || !destination.CanSeek)
        {
            throw new ArgumentException("The build destination must be readable, writable, and seekable.", nameof(destination));
        }

        options.Validate();
        ValidateRecipe(builder);
        NdsFileSystemBuildSnapshot fileSystem = builder.FileSystem.BuildSnapshot();
        NdsImageBuildLayout layout = CalculateLayout(builder, fileSystem, options);
        byte[] fat = BuildFat(layout.FileRegions);
        byte[] header = BuildHeader(builder, layout, options);

        destination.Position = 0;
        destination.SetLength(0);
        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await WriteAtAsync(destination, layout.Arm9.Offset, builder.Arm9!.Contents, options.PaddingByte, cancellationToken)
            .ConfigureAwait(false);
        await WriteAtAsync(destination, layout.Arm7.Offset, builder.Arm7!.Contents, options.PaddingByte, cancellationToken)
            .ConfigureAwait(false);
        await WriteAtAsync(destination, layout.FileNameTable.Offset, fileSystem.FileNameTable, options.PaddingByte, cancellationToken)
            .ConfigureAwait(false);
        await WriteAtAsync(destination, layout.FileAllocationTable.Offset, fat, options.PaddingByte, cancellationToken)
            .ConfigureAwait(false);
        if (layout.Banner is not null)
        {
            await WriteAtAsync(
                destination,
                layout.Banner.Value.Offset,
                builder.Banner!.RawData,
                options.PaddingByte,
                cancellationToken).ConfigureAwait(false);
        }

        for (int fileId = 0; fileId < fileSystem.FilesInIdOrder.Count; fileId++)
        {
            await WriteAtAsync(
                destination,
                layout.FileRegions[fileId].Offset,
                fileSystem.FilesInIdOrder[fileId].Contents,
                options.PaddingByte,
                cancellationToken).ConfigureAwait(false);
        }

        await FillToAsync(destination, layout.PhysicalSize, options.PaddingByte, cancellationToken).ConfigureAwait(false);
        destination.SetLength(layout.PhysicalSize);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (options.VerifyOutput)
        {
            await VerifyAsync(destination, fileSystem, cancellationToken).ConfigureAwait(false);
        }

        destination.Position = layout.PhysicalSize;
        return new(
            layout.UsedSize,
            layout.PhysicalSize,
            layout.Arm9,
            layout.Arm7,
            layout.FileNameTable,
            layout.FileAllocationTable,
            layout.Banner,
            layout.FileRegions.Count);
    }

    /// <summary>Rejects incomplete or mismatched Program definitions and validates fixed-width ASCII identity fields.</summary>
    /// <param name="builder">Recipe checked before any destination bytes are changed.</param>
    private static void ValidateRecipe(NdsImageBuilder builder)
    {
        if (builder.Arm9 is null || builder.Arm9.Processor != NdsProcessor.Arm9 || builder.Arm9.Contents.IsEmpty)
        {
            throw new InvalidDataException("A non-empty ARM9 definition with the ARM9 processor identity is required.");
        }

        if (builder.Arm7 is null || builder.Arm7.Processor != NdsProcessor.Arm7 || builder.Arm7.Contents.IsEmpty)
        {
            throw new InvalidDataException("A non-empty ARM7 definition with the ARM7 processor identity is required.");
        }

        ValidateAscii(builder.Title, 0, 12, nameof(builder.Title));
        ValidateAscii(builder.GameCode, 4, 4, nameof(builder.GameCode));
        ValidateAscii(builder.MakerCode, 2, 2, nameof(builder.MakerCode));
    }

    /// <summary>Assigns monotonically increasing aligned Regions without consulting mutable stream state.</summary>
    /// <param name="builder">Recipe supplying component sizes.</param>
    /// <param name="fileSystem">Frozen FNT bytes and deterministic File ID payload order.</param>
    /// <param name="options">Section and file alignment policy.</param>
    /// <returns>A Layout used unchanged by both metadata and byte writers.</returns>
    private static NdsImageBuildLayout CalculateLayout(
        NdsImageBuilder builder,
        NdsFileSystemBuildSnapshot fileSystem,
        NdsImageBuildOptions options)
    {
        long cursor = options.HeaderSize;
        NdsRegion arm9 = Place(ref cursor, builder.Arm9!.Contents.Length, options.SectionAlignment);
        NdsRegion arm7 = Place(ref cursor, builder.Arm7!.Contents.Length, options.SectionAlignment);
        NdsRegion fnt = Place(ref cursor, fileSystem.FileNameTable.Length, options.SectionAlignment);
        NdsRegion fat = Place(ref cursor, checked(fileSystem.FilesInIdOrder.Count * 8), options.SectionAlignment);
        NdsRegion? banner = builder.Banner is null
            ? null
            : Place(ref cursor, builder.Banner.RawData.Length, options.SectionAlignment);
        var files = new NdsRegion[fileSystem.FilesInIdOrder.Count];
        for (int fileId = 0; fileId < files.Length; fileId++)
        {
            files[fileId] = Place(ref cursor, fileSystem.FilesInIdOrder[fileId].Contents.Length, options.FileAlignment);
        }

        long usedSize = cursor;
        long physicalSize = Align(usedSize, options.FileAlignment);
        if (physicalSize > uint.MaxValue)
        {
            throw new InvalidDataException("The generated image exceeds the DS header's 32-bit offset and size fields.");
        }

        return new(arm9, arm7, fnt, fat, banner, files, usedSize, physicalSize);
    }

    /// <summary>Writes File ID-indexed half-open payload intervals in the eight-byte FAT record format.</summary>
    /// <param name="regions">Payload Regions whose list positions are their encoded File IDs.</param>
    /// <returns>Complete little-endian FAT bytes.</returns>
    private static byte[] BuildFat(IReadOnlyList<NdsRegion> regions)
    {
        byte[] fat = new byte[checked(regions.Count * 8)];
        for (int fileId = 0; fileId < regions.Count; fileId++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(fat.AsSpan(fileId * 8), checked((uint)regions[fileId].Offset));
            BinaryPrimitives.WriteUInt32LittleEndian(fat.AsSpan((fileId * 8) + 4), checked((uint)regions[fileId].End));
        }

        return fat;
    }

    /// <summary>Serializes the common DS header from the final Layout and computes logo/header CRCs last.</summary>
    /// <param name="builder">Typed identity, Program addresses, logo, and Banner source.</param>
    /// <param name="layout">Final component offsets shared with the byte writer.</param>
    /// <param name="options">Declared header size policy.</param>
    /// <returns>A zero-initialized reserved header area ending exactly at the first allowed Program offset.</returns>
    private static byte[] BuildHeader(
        NdsImageBuilder builder,
        NdsImageBuildLayout layout,
        NdsImageBuildOptions options)
    {
        byte[] header = new byte[options.HeaderSize];
        WriteAscii(header.AsSpan(0x00, 12), builder.Title);
        WriteAscii(header.AsSpan(0x0C, 4), builder.GameCode);
        WriteAscii(header.AsSpan(0x10, 2), builder.MakerCode);
        header[0x14] = CalculateDeviceCapacity(layout.PhysicalSize);
        header[0x1E] = builder.Version;
        WriteProgram(header, 0x20, layout.Arm9, builder.Arm9!);
        WriteProgram(header, 0x30, layout.Arm7, builder.Arm7!);
        WriteRegion(header, 0x40, layout.FileNameTable);
        WriteRegion(header, 0x48, layout.FileAllocationTable);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x68), checked((uint)(layout.Banner?.Offset ?? 0)));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x80), checked((uint)layout.UsedSize));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x84), checked((uint)options.HeaderSize));
        if (!builder.NintendoLogo.IsEmpty)
        {
            builder.NintendoLogo.Span.CopyTo(header.AsSpan(0xC0, 156));
        }

        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(0x15C),
            NdsChecksums.ComputeCrc16(header.AsSpan(0xC0, 156)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(0x15E),
            NdsChecksums.ComputeCrc16(header.AsSpan(0, 0x15E)));
        return header;
    }

    /// <summary>Writes a DS Program's cartridge offset, entry address, load address, and byte count tuple.</summary>
    /// <param name="header">Mutable common header prefix.</param>
    /// <param name="offset">Tuple offset: <c>0x20</c> for ARM9 or <c>0x30</c> for ARM7.</param>
    /// <param name="region">Final cartridge payload Region.</param>
    /// <param name="program">Runtime addresses and payload length.</param>
    private static void WriteProgram(Span<byte> header, int offset, NdsRegion region, NdsProgramDefinition program)
    {
        NdsBinary.WriteUInt32(header, offset, checked((uint)region.Offset));
        NdsBinary.WriteUInt32(header, offset + 4, program.EntryAddress);
        NdsBinary.WriteUInt32(header, offset + 8, program.LoadAddress);
        NdsBinary.WriteUInt32(header, offset + 12, checked((uint)region.Length));
    }

    /// <summary>Writes one adjacent offset/length pair used by common-header table fields.</summary>
    /// <param name="header">Mutable common header prefix.</param>
    /// <param name="offset">Byte offset of the destination start word.</param>
    /// <param name="region">Final Region converted only after checked 32-bit validation.</param>
    private static void WriteRegion(Span<byte> header, int offset, NdsRegion region)
    {
        NdsBinary.WriteUInt32(header, offset, checked((uint)region.Offset));
        NdsBinary.WriteUInt32(header, offset + 4, checked((uint)region.Length));
    }

    /// <summary>Places a component at the next aligned offset and advances the exclusive Layout cursor.</summary>
    /// <param name="cursor">Current exclusive end updated to the new Region end.</param>
    /// <param name="length">Non-negative component byte count.</param>
    /// <param name="alignment">Validated positive power-of-two boundary.</param>
    /// <returns>The assigned half-open Region.</returns>
    private static NdsRegion Place(ref long cursor, long length, int alignment)
    {
        cursor = Align(cursor, alignment);
        var region = new NdsRegion(cursor, length);
        cursor = region.End;
        return region;
    }

    /// <summary>Rounds an offset upward with checked arithmetic under a validated power-of-two boundary.</summary>
    /// <param name="value">Non-negative current Layout end.</param>
    /// <param name="alignment">Positive power of two.</param>
    /// <returns>The first compatible offset at or after <paramref name="value"/>.</returns>
    private static long Align(long value, int alignment) => checked((value + alignment - 1) & -alignment);

    /// <summary>Derives the smallest 128 KiB-scaled capacity exponent that contains the physical output.</summary>
    /// <param name="physicalSize">Final destination length.</param>
    /// <returns>A header byte whose nominal capacity is at least the generated length.</returns>
    private static byte CalculateDeviceCapacity(long physicalSize)
    {
        byte exponent = 0;
        long capacity = 128 * 1024;
        while (capacity < physicalSize && exponent < 31)
        {
            exponent++;
            capacity <<= 1;
        }

        return exponent;
    }

    /// <summary>Validates printable fixed-field text before the destination is truncated.</summary>
    /// <param name="value">Proposed visible ASCII characters.</param>
    /// <param name="minimum">Minimum field length, including exact identifiers.</param>
    /// <param name="maximum">Maximum field width.</param>
    /// <param name="name">Recipe property named in the failure.</param>
    private static void ValidateAscii(string value, int minimum, int maximum, string name)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length < minimum || value.Length > maximum || value.Any(static character => character is < ' ' or > '~'))
        {
            throw new InvalidDataException($"{name} must contain {minimum} through {maximum} printable ASCII characters.");
        }
    }

    /// <summary>Writes validated text and leaves the remainder of a zero-initialized fixed-width field padded with NUL bytes.</summary>
    /// <param name="destination">Exact header field width.</param>
    /// <param name="value">Printable ASCII text no longer than the destination.</param>
    private static void WriteAscii(Span<byte> destination, string value) => Encoding.ASCII.GetBytes(value, destination);

    /// <summary>Fills any forward gap before committing bytes at their precomputed absolute offset.</summary>
    /// <param name="destination">Sequential output whose current length is the committed Layout end.</param>
    /// <param name="offset">Precomputed component start.</param>
    /// <param name="data">Exact component bytes.</param>
    /// <param name="paddingByte">Deterministic byte used between components.</param>
    /// <param name="cancellationToken">Cancels gap or component writes.</param>
    private static async ValueTask WriteAtAsync(
        Stream destination,
        long offset,
        ReadOnlyMemory<byte> data,
        byte paddingByte,
        CancellationToken cancellationToken)
    {
        await FillToAsync(destination, offset, paddingByte, cancellationToken).ConfigureAwait(false);
        destination.Position = offset;
        await destination.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Streams repeated padding in bounded chunks until the physical destination reaches a target offset.</summary>
    /// <param name="destination">Output whose length is extended, never shortened.</param>
    /// <param name="targetOffset">Exclusive target length.</param>
    /// <param name="paddingByte">Repeated deterministic gap byte.</param>
    /// <param name="cancellationToken">Cancels chunked writes.</param>
    private static async ValueTask FillToAsync(
        Stream destination,
        long targetOffset,
        byte paddingByte,
        CancellationToken cancellationToken)
    {
        if (destination.Length >= targetOffset)
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

    /// <summary>Uses the production reader to prove checksums, paths, File IDs, Regions, and payload bytes agree with the recipe.</summary>
    /// <param name="destination">Completed readable stream left open by the loader.</param>
    /// <param name="fileSystem">Frozen expected path and payload order.</param>
    /// <param name="cancellationToken">Cancels reopen parsing or payload comparisons.</param>
    private static async ValueTask VerifyAsync(
        Stream destination,
        NdsFileSystemBuildSnapshot fileSystem,
        CancellationToken cancellationToken)
    {
        destination.Position = 0;
        using NdsImage image = await NdsImage.OpenAsync(
            destination,
            leaveOpen: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        NdsValidationResult validation = image.Validate();
        if (!validation.IsValid)
        {
            throw new InvalidDataException(
                $"Generated image verification failed: {string.Join("; ", validation.Diagnostics.Select(static item => item.Message))}");
        }

        for (int fileId = 0; fileId < fileSystem.FilesInIdOrder.Count; fileId++)
        {
            byte[] actual = await image.FileSystem.GetFile(fileId).ReadAllBytesAsync(cancellationToken).ConfigureAwait(false);
            if (!actual.AsSpan().SequenceEqual(fileSystem.FilesInIdOrder[fileId].Contents.Span))
            {
                throw new InvalidDataException($"Generated image payload verification failed for File ID {fileId}.");
            }
        }
    }
}
