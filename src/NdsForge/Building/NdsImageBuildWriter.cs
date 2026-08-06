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
        NdsImageBuildContent content = NdsImageBuildContentPreparer.Prepare(builder, options);
        NdsImageBuildLayout layout = CalculateLayout(builder, content, options);
        byte[] fat = BuildFat(layout.FileRegions);
        byte[] header = NdsImageHeaderWriter.Write(builder, layout, content, options);

        destination.Position = 0;
        destination.SetLength(0);
        byte paddingByte = options.Profile == NdsImageBuildProfile.Ndstool1503 ? (byte)0 : options.PaddingByte;
        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await WriteAtAsync(destination, layout.Arm9.Offset, content.Arm9Data, paddingByte, cancellationToken)
            .ConfigureAwait(false);
        if (layout.Arm9Footer is not null)
        {
            await WriteAtAsync(
                destination,
                layout.Arm9Footer.Value.Offset,
                content.Arm9TrailingData,
                paddingByte,
                cancellationToken).ConfigureAwait(false);
        }
        await WriteAtAsync(
            destination,
            layout.Arm9OverlayTable.Offset,
            content.Arm9OverlayTable,
            paddingByte,
            cancellationToken).ConfigureAwait(false);
        await WriteAtAsync(destination, layout.Arm7.Offset, content.Arm7Data, paddingByte, cancellationToken)
            .ConfigureAwait(false);
        await WriteAtAsync(
            destination,
            layout.Arm7OverlayTable.Offset,
            content.Arm7OverlayTable,
            paddingByte,
            cancellationToken).ConfigureAwait(false);
        await WriteAtAsync(destination, layout.FileNameTable.Offset, content.FileSystem.FileNameTable, paddingByte, cancellationToken)
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

        for (int fileId = 0; fileId < content.Allocations.Length; fileId++)
        {
            await WriteAtAsync(
                destination,
                layout.FileRegions[fileId].Offset,
                content.Allocations[fileId],
                paddingByte,
                cancellationToken).ConfigureAwait(false);
        }

        if (layout.Arm9i is not null)
        {
            await WriteAtAsync(
                destination,
                layout.Arm9i.Value.Offset,
                content.Arm9iData,
                paddingByte,
                cancellationToken).ConfigureAwait(false);
        }

        if (layout.Arm7i is not null)
        {
            await WriteAtAsync(
                destination,
                layout.Arm7i.Value.Offset,
                content.Arm7iData,
                paddingByte,
                cancellationToken).ConfigureAwait(false);
        }

        await FillToAsync(destination, layout.PhysicalSize, paddingByte, cancellationToken).ConfigureAwait(false);
        destination.SetLength(layout.PhysicalSize);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (options.VerifyOutput)
        {
            await NdsImageBuildVerifier.VerifyAsync(destination, builder, content.FileSystem, cancellationToken).ConfigureAwait(false);
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
            layout.Arm9i,
            layout.Arm7i,
            content.FileSystem.FilesInIdOrder.Count,
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

        bool isDsi = builder.Kind != NdsImageKind.NintendoDs;
        if (!isDsi && (builder.Arm9i is not null || builder.Arm7i is not null || builder.DsiMetadata is not null))
        {
            throw new InvalidDataException("DS recipes cannot contain DSi Programs or extended metadata; select a DSi image kind explicitly.");
        }

        if (isDsi)
        {
            ValidateDsiRecipe(builder);
        }

        ValidateAscii(builder.Title, 0, 12, nameof(builder.Title));
        ValidateAscii(builder.GameCode, 4, 4, nameof(builder.GameCode));
        ValidateAscii(builder.MakerCode, 2, 2, nameof(builder.MakerCode));
    }

    /// <summary>Rejects incomplete DSi recipes and address identities that cannot be encoded by the extended tuples.</summary>
    /// <param name="builder">Recipe whose unit code already selects DSi-enhanced or DSi-exclusive execution.</param>
    private static void ValidateDsiRecipe(NdsImageBuilder builder)
    {
        if (builder.DsiMetadata is null)
        {
            throw new InvalidDataException("A DSi recipe requires explicit extended metadata and integrity policy.");
        }

        if (builder.Arm9i is null || builder.Arm9i.Processor != NdsProcessor.Arm9i || builder.Arm9i.Contents.IsEmpty ||
            builder.Arm9i.EntryAddress != builder.Arm9i.LoadAddress)
        {
            throw new InvalidDataException("A DSi recipe requires non-empty ARM9i data whose entry and load addresses match.");
        }

        if (builder.Arm7i is null || builder.Arm7i.Processor != NdsProcessor.Arm7i || builder.Arm7i.Contents.IsEmpty ||
            builder.Arm7i.EntryAddress != builder.Arm7i.LoadAddress)
        {
            throw new InvalidDataException("A DSi recipe requires non-empty ARM7i data whose entry and load addresses match.");
        }

        if (builder.DsiMetadata.Integrity is null)
        {
            throw new InvalidDataException("A DSi recipe must name how authentication fields are populated.");
        }
    }

    /// <summary>Assigns monotonically increasing aligned Regions without consulting mutable stream state.</summary>
    /// <param name="builder">Recipe supplying component sizes.</param>
    /// <param name="content">Frozen bytes, declared Program lengths, and final File ID payload order.</param>
    /// <param name="options">Section and file alignment policy.</param>
    /// <returns>A Layout used unchanged by both metadata and byte writers.</returns>
    private static NdsImageBuildLayout CalculateLayout(
        NdsImageBuilder builder,
        NdsImageBuildContent content,
        NdsImageBuildOptions options)
    {
        bool ndstoolProfile = options.Profile == NdsImageBuildProfile.Ndstool1503;
        long cursor = options.HeaderSize;
        NdsRegion arm9 = PlaceProgram(
            ref cursor,
            content.Arm9Data.Length,
            content.Arm9DeclaredLength,
            options.SectionAlignment);
        NdsRegion? arm9Footer = content.Arm9TrailingData.IsEmpty
            ? null
            : Place(ref cursor, content.Arm9TrailingData.Length, alignment: 1);
        NdsRegion arm9Overlays = content.Arm9OverlayTable.Length == 0
            ? default
            : Place(ref cursor, content.Arm9OverlayTable.Length, ndstoolProfile ? 1 : options.SectionAlignment);
        NdsRegion arm7 = PlaceProgram(
            ref cursor,
            content.Arm7Data.Length,
            content.Arm7DeclaredLength,
            options.SectionAlignment);
        NdsRegion arm7Overlays = content.Arm7OverlayTable.Length == 0
            ? default
            : Place(ref cursor, content.Arm7OverlayTable.Length, ndstoolProfile ? 1 : options.SectionAlignment);
        NdsRegion fnt = Place(ref cursor, content.FileSystem.FileNameTable.Length, options.SectionAlignment);
        NdsRegion fat = Place(ref cursor, checked(content.Allocations.Length * 8), options.SectionAlignment);
        NdsRegion? banner = builder.Banner is null
            ? null
            : Place(ref cursor, builder.Banner.RawData.Length, options.SectionAlignment);
        var files = new NdsRegion[content.Allocations.Length];
        int allocationAlignment = ndstoolProfile
            ? options.SectionAlignment
            : options.FileAlignment;
        for (int fileId = 0; fileId < files.Length; fileId++)
        {
            files[fileId] = Place(ref cursor, content.Allocations[fileId].Length, allocationAlignment);
        }

        long commonContentEnd = cursor;
        NdsRegion? arm9i = null;
        NdsRegion? arm7i = null;
        long usedSize;
        long physicalSize;
        if (builder.Kind == NdsImageKind.NintendoDs)
        {
            physicalSize = Align(commonContentEnd, options.FileAlignment);
            usedSize = ndstoolProfile ? physicalSize : commonContentEnd;
        }
        else
        {
            usedSize = Align(commonContentEnd, options.FileAlignment);
            cursor = usedSize;
            arm9i = Place(ref cursor, content.Arm9iData.Length, alignment: 0x400);
            arm7i = Place(ref cursor, content.Arm7iData.Length, options.SectionAlignment);
            physicalSize = Align(cursor, options.SectionAlignment);
        }

        if (physicalSize > uint.MaxValue)
        {
            throw new InvalidDataException("The generated image exceeds the DS header's 32-bit offset and size fields.");
        }

        return new(
            arm9,
            arm9Footer,
            arm9Overlays,
            arm7,
            arm7Overlays,
            fnt,
            fat,
            banner,
            files,
            arm9i,
            arm7i,
            usedSize,
            physicalSize);
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

    /// <summary>
    /// Advances by bytes actually written while reporting the potentially rounded Program length encoded in the
    /// header. This models ndstool's legacy footer overlap without introducing phantom bytes into the output stream.
    /// </summary>
    /// <param name="cursor">Current physical end aligned and advanced by <paramref name="physicalLength"/>.</param>
    /// <param name="physicalLength">Number of program bytes supplied to the writer.</param>
    /// <param name="declaredLength">Header-visible size, which must not be smaller than physical data.</param>
    /// <param name="alignment">Boundary applied to the Program start.</param>
    /// <returns>A header-facing Region whose end may extend past the current physical cursor.</returns>
    private static NdsRegion PlaceProgram(
        ref long cursor,
        int physicalLength,
        int declaredLength,
        int alignment)
    {
        if (declaredLength < physicalLength)
        {
            throw new InvalidDataException("A Program's declared length cannot exclude physical executable bytes.");
        }

        cursor = Align(cursor, alignment);
        var region = new NdsRegion(cursor, declaredLength);
        cursor = checked(cursor + physicalLength);
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
