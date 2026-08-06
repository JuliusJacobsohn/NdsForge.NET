using System.Buffers.Binary;

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
        ReadOnlyMemory<byte> arm9Data = PrepareProgram(builder.Arm9!, isArm9: true, options.Profile);
        ReadOnlyMemory<byte> arm7Data = PrepareProgram(builder.Arm7!, isArm9: false, options.Profile);
        ReadOnlyMemory<byte>[] allocations = CollectAllocations(builder, fileSystem);
        byte[] arm9OverlayTable = BuildOverlayTable(
            builder.Arm9Overlays,
            fileSystem,
            fileSystem.FilesInIdOrder.Count);
        byte[] arm7OverlayTable = BuildOverlayTable(
            builder.Arm7Overlays,
            fileSystem,
            checked(fileSystem.FilesInIdOrder.Count + builder.Arm9Overlays.Count(static overlay => overlay.HasPrivateAllocation)));
        NdsImageBuildLayout layout = CalculateLayout(
            builder,
            fileSystem,
            arm9Data.Length,
            arm7Data.Length,
            allocations,
            arm9OverlayTable.Length,
            arm7OverlayTable.Length,
            options);
        byte[] fat = BuildFat(layout.FileRegions);
        byte[] header = NdsImageHeaderWriter.Write(builder, layout, options);

        destination.Position = 0;
        destination.SetLength(0);
        byte paddingByte = options.Profile == NdsImageBuildProfile.Ndstool1503 ? (byte)0 : options.PaddingByte;
        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await WriteAtAsync(destination, layout.Arm9.Offset, arm9Data, paddingByte, cancellationToken)
            .ConfigureAwait(false);
        if (layout.Arm9Footer is not null)
        {
            await WriteAtAsync(
                destination,
                layout.Arm9Footer.Value.Offset,
                builder.Arm9!.Footer,
                paddingByte,
                cancellationToken).ConfigureAwait(false);
        }
        await WriteAtAsync(
            destination,
            layout.Arm9OverlayTable.Offset,
            arm9OverlayTable,
            paddingByte,
            cancellationToken).ConfigureAwait(false);
        await WriteAtAsync(destination, layout.Arm7.Offset, arm7Data, paddingByte, cancellationToken)
            .ConfigureAwait(false);
        await WriteAtAsync(
            destination,
            layout.Arm7OverlayTable.Offset,
            arm7OverlayTable,
            paddingByte,
            cancellationToken).ConfigureAwait(false);
        await WriteAtAsync(destination, layout.FileNameTable.Offset, fileSystem.FileNameTable, paddingByte, cancellationToken)
            .ConfigureAwait(false);
        await WriteAtAsync(destination, layout.FileAllocationTable.Offset, fat, paddingByte, cancellationToken)
            .ConfigureAwait(false);
        if (layout.Banner is not null)
        {
            await WriteAtAsync(
                destination,
                layout.Banner.Value.Offset,
                builder.Banner!.RawData,
                paddingByte,
                cancellationToken).ConfigureAwait(false);
        }

        for (int fileId = 0; fileId < allocations.Length; fileId++)
        {
            await WriteAtAsync(
                destination,
                layout.FileRegions[fileId].Offset,
                allocations[fileId],
                paddingByte,
                cancellationToken).ConfigureAwait(false);
        }

        await FillToAsync(destination, layout.PhysicalSize, paddingByte, cancellationToken).ConfigureAwait(false);
        destination.SetLength(layout.PhysicalSize);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (options.VerifyOutput)
        {
            await NdsImageBuildVerifier.VerifyAsync(destination, builder, fileSystem, cancellationToken).ConfigureAwait(false);
        }

        destination.Position = layout.PhysicalSize;
        return new(
            layout.UsedSize,
            layout.PhysicalSize,
            layout.Arm9,
            layout.Arm9Footer,
            layout.Arm9OverlayTable,
            layout.Arm7,
            layout.Arm7OverlayTable,
            layout.FileNameTable,
            layout.FileAllocationTable,
            layout.Banner,
            fileSystem.FilesInIdOrder.Count,
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

    /// <summary>Applies storage transformations belonging to a named compatibility profile without mutating Program definitions.</summary>
    /// <param name="program">Logical raw executable supplied by the recipe.</param>
    /// <param name="isArm9">Selects the legacy 2 KiB secure-area placeholder required only before ARM9 input.</param>
    /// <param name="profile">Layout personality controlling transformations.</param>
    /// <returns>Exact header-declared Program bytes, excluding an optional SDK footer.</returns>
    private static ReadOnlyMemory<byte> PrepareProgram(
        NdsProgramDefinition program,
        bool isArm9,
        NdsImageBuildProfile profile)
    {
        if (profile != NdsImageBuildProfile.Ndstool1503)
        {
            return program.Contents;
        }

        int prefixLength = isArm9 ? 0x800 : 0;
        int length = checked((program.Contents.Length + prefixLength + 3) & ~3);
        byte[] data = new byte[length];
        if (isArm9)
        {
            for (int offset = 0; offset < prefixLength; offset += 4)
            {
                data[offset] = 0xFF;
                data[offset + 1] = 0xDE;
                data[offset + 2] = 0xFF;
                data[offset + 3] = 0xE7;
            }
        }

        program.Contents.Span.CopyTo(data.AsSpan(prefixLength));
        return data;
    }

    /// <summary>Combines named FNT payloads with private ARM9 then ARM7 Overlay Allocations in final File ID order.</summary>
    /// <param name="builder">Recipe supplying processor-separated Overlay definitions.</param>
    /// <param name="fileSystem">Snapshot supplying named payloads in encoded FNT order.</param>
    /// <returns>Immutable memory views whose array positions become FAT File IDs.</returns>
    private static ReadOnlyMemory<byte>[] CollectAllocations(
        NdsImageBuilder builder,
        NdsFileSystemBuildSnapshot fileSystem) =>
        fileSystem.FilesInIdOrder.Select(static file => file.Contents)
            .Concat(builder.Arm9Overlays.Where(static overlay => overlay.HasPrivateAllocation).Select(static overlay => overlay.Contents))
            .Concat(builder.Arm7Overlays.Where(static overlay => overlay.HasPrivateAllocation).Select(static overlay => overlay.Contents))
            .ToArray();

    /// <summary>Serializes fixed Overlay records and assigns consecutive private payload File IDs from a known base.</summary>
    /// <param name="overlays">One processor's definitions in desired table order.</param>
    /// <param name="fileSystem">Named payload order used to resolve shared NitroFS paths.</param>
    /// <param name="firstPrivateFileId">FAT index assigned to the first definition requiring a private Allocation.</param>
    /// <returns>Complete table bytes whose length is exactly 32 times the definition count.</returns>
    private static byte[] BuildOverlayTable(
        IReadOnlyList<NdsOverlayDefinition> overlays,
        NdsFileSystemBuildSnapshot fileSystem,
        int firstPrivateFileId)
    {
        Dictionary<string, int> namedFileIds = fileSystem.FilesInIdOrder
            .Select((file, fileId) => (file.Path, fileId))
            .ToDictionary(static item => item.Path, static item => item.fileId, StringComparer.Ordinal);
        byte[] data = new byte[checked(overlays.Count * 32)];
        int nextPrivateFileId = firstPrivateFileId;
        for (int index = 0; index < overlays.Count; index++)
        {
            NdsOverlayDefinition overlay = overlays[index];
            int fileId;
            string? linkedFilePath = overlay.EffectiveLinkedFilePath;
            if (linkedFilePath is not null)
            {
                if (!namedFileIds.TryGetValue(linkedFilePath, out fileId))
                {
                    throw new InvalidDataException(
                        $"Overlay {overlay.Id} links missing NitroFS file '{linkedFilePath}'.");
                }
            }
            else
            {
                fileId = nextPrivateFileId++;
            }

            Span<byte> entry = data.AsSpan(index * 32, 32);
            NdsBinary.WriteUInt32(entry, 0x00, overlay.Id);
            NdsBinary.WriteUInt32(entry, 0x04, overlay.LoadAddress);
            NdsBinary.WriteUInt32(entry, 0x08, overlay.RamSize);
            NdsBinary.WriteUInt32(entry, 0x0C, overlay.BssSize);
            NdsBinary.WriteUInt32(entry, 0x10, overlay.StaticInitializerStart);
            NdsBinary.WriteUInt32(entry, 0x14, overlay.StaticInitializerEnd);
            NdsBinary.WriteUInt32(entry, 0x18, checked((uint)fileId));
            NdsBinary.WriteUInt32(entry, 0x1C, overlay.CompressedSize | ((uint)overlay.Flags << 24));
        }

        return data;
    }

    /// <summary>Assigns monotonically increasing aligned Regions without consulting mutable stream state.</summary>
    /// <param name="builder">Recipe supplying component sizes.</param>
    /// <param name="fileSystem">Frozen FNT bytes and deterministic File ID payload order.</param>
    /// <param name="arm9DataLength">Profile-transformed ARM9 length excluding footer.</param>
    /// <param name="arm7DataLength">Profile-transformed ARM7 length.</param>
    /// <param name="allocations">Named and Overlay-private payloads in final File ID order.</param>
    /// <param name="arm9OverlayTableLength">Bytes reserved for ARM9 records.</param>
    /// <param name="arm7OverlayTableLength">Bytes reserved for ARM7 records.</param>
    /// <param name="options">Section and file alignment policy.</param>
    /// <returns>A Layout used unchanged by both metadata and byte writers.</returns>
    private static NdsImageBuildLayout CalculateLayout(
        NdsImageBuilder builder,
        NdsFileSystemBuildSnapshot fileSystem,
        int arm9DataLength,
        int arm7DataLength,
        ReadOnlyMemory<byte>[] allocations,
        int arm9OverlayTableLength,
        int arm7OverlayTableLength,
        NdsImageBuildOptions options)
    {
        long cursor = options.HeaderSize;
        NdsRegion arm9 = Place(ref cursor, arm9DataLength, options.SectionAlignment);
        NdsRegion? arm9Footer = builder.Arm9!.Footer.IsEmpty
            ? null
            : Place(ref cursor, builder.Arm9.Footer.Length, alignment: 1);
        NdsRegion arm9Overlays = arm9OverlayTableLength == 0
            ? default
            : Place(ref cursor, arm9OverlayTableLength, options.SectionAlignment);
        NdsRegion arm7 = Place(ref cursor, arm7DataLength, options.SectionAlignment);
        NdsRegion arm7Overlays = arm7OverlayTableLength == 0
            ? default
            : Place(ref cursor, arm7OverlayTableLength, options.SectionAlignment);
        NdsRegion fnt = Place(ref cursor, fileSystem.FileNameTable.Length, options.SectionAlignment);
        NdsRegion fat = Place(ref cursor, checked(allocations.Length * 8), options.SectionAlignment);
        NdsRegion? banner = builder.Banner is null
            ? null
            : Place(ref cursor, builder.Banner.RawData.Length, options.SectionAlignment);
        if (options.Profile == NdsImageBuildProfile.Ndstool1503 && allocations.Length > 0)
        {
            cursor = Align(cursor, options.SectionAlignment);
        }

        var files = new NdsRegion[allocations.Length];
        for (int fileId = 0; fileId < files.Length; fileId++)
        {
            files[fileId] = Place(ref cursor, allocations[fileId].Length, options.FileAlignment);
        }

        long contentEnd = cursor;
        long physicalSize = Align(contentEnd, options.FileAlignment);
        long usedSize = options.Profile == NdsImageBuildProfile.Ndstool1503 ? physicalSize : contentEnd;
        if (physicalSize > uint.MaxValue)
        {
            throw new InvalidDataException("The generated image exceeds the DS header's 32-bit offset and size fields.");
        }

        return new(arm9, arm9Footer, arm9Overlays, arm7, arm7Overlays, fnt, fat, banner, files, usedSize, physicalSize);
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

}
