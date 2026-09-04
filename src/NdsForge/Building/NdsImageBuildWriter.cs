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
        NdsImageBuildLayout layout = NdsImageLayoutPlanner.Plan(builder, content, options);
        layout = NdsDsHeaderWriter.CompleteLayout(builder, content, layout, options);
        layout = NdsImageCapacityPlanner.Apply(builder, layout, options, destination);
        byte[] fat = BuildFat(layout.FileRegions);

        cancellationToken.ThrowIfCancellationRequested();
        destination.Position = 0;
        destination.SetLength(0);
        byte paddingByte = options.Profile == NdsImageBuildProfile.Ndstool1503 ? (byte)0 : options.PaddingByte;
        await destination.WriteAsync(new byte[options.HeaderSize], cancellationToken).ConfigureAwait(false);
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

        if (layout.DebugProgram is not null)
        {
            await WriteAtAsync(
                destination,
                layout.DebugProgram.Value.Offset,
                builder.DebugProgram!.Contents,
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

        await FillToAsync(destination, layout.ContentSize, paddingByte, cancellationToken).ConfigureAwait(false);
        NdsDsiDigestBuildResult? digestResult = null;
        if (builder.DsiMetadata?.Digests is not null)
        {
            digestResult = await NdsDsiDigestBuilder.BuildAsync(
                destination,
                layout,
                builder.DsiMetadata.Digests,
                builder.DsiMetadata.Integrity.HmacKey,
                cancellationToken).ConfigureAwait(false);
            await WriteAtAsync(
                destination,
                layout.SectorHashTable.Offset,
                digestResult.SectorHashes,
                paddingByte,
                cancellationToken).ConfigureAwait(false);
            await WriteAtAsync(
                destination,
                layout.BlockHashTable.Offset,
                digestResult.BlockHashes,
                paddingByte,
                cancellationToken).ConfigureAwait(false);
        }

        if (builder.DownloadPlaySignature is not null)
        {
            await FillToAsync(destination, layout.UsedSize, paddingByte, cancellationToken).ConfigureAwait(false);
            await NdsDownloadPlaySignatureWriter.WriteAsync(destination, builder.DownloadPlaySignature, layout.UsedSize, cancellationToken).ConfigureAwait(false);
        }
        await FillToAsync(destination, layout.ContentSize, paddingByte, cancellationToken).ConfigureAwait(false);
        await FillToAsync(destination, layout.PhysicalSize, options.PaddingByte, cancellationToken).ConfigureAwait(false);
        destination.SetLength(layout.PhysicalSize);
        byte[] header = NdsImageHeaderWriter.Write(builder, layout, content, options, digestResult);
        destination.Position = 0;
        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await NdsTwlReservationWriter.WriteAsync(destination, builder, layout.TwlReserved, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<NdsDiagnostic> diagnostics = builder.DsMetadata is null
            ? Array.Empty<NdsDiagnostic>()
            : await NdsDsHeaderWriter.FinalizeAsync(destination, header, builder.DsMetadata.Integrity, cancellationToken).ConfigureAwait(false);
        diagnostics = NdsDownloadPlaySignatureWriter.AppendDiagnostic(diagnostics, builder.DownloadPlaySignature, layout.UsedSize);
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
            layout.DebugProgram,
            layout.Arm9i,
            layout.Arm7i,
            layout.SectorHashTable,
            layout.BlockHashTable,
            content.FileSystem.FilesInIdOrder.Count,
            layout.FileRegions.Count,
            diagnostics);
    }

    /// <summary>Rejects incomplete or mismatched Program definitions and validates fixed-width ASCII identity fields.</summary>
    /// <param name="builder">Recipe checked before any destination bytes are changed.</param>
    private static void ValidateRecipe(NdsImageBuilder builder)
    {
        NdsCarrierBuildValidator.Validate(builder);
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

        builder.DsMetadata?.Validate(builder);

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

        if (builder.Arm9i is null || builder.Arm9i.Processor != NdsProcessor.Arm9i ||
            (builder.Arm9i.Contents.IsEmpty && (builder.Carrier != NdsImageCarrier.DigitalSrl || builder.Arm9i.LoadAddress == 0)) ||
            builder.Arm9i.EntryAddress != builder.Arm9i.LoadAddress)
        {
            throw new InvalidDataException("A DSi recipe requires matching ARM9i entry/load addresses and non-empty data, or an explicit empty digital tuple with a nonzero load address.");
        }

        if (builder.Arm7i is null || builder.Arm7i.Processor != NdsProcessor.Arm7i ||
            (builder.Arm7i.Contents.IsEmpty && (builder.Carrier != NdsImageCarrier.DigitalSrl || builder.Arm7i.LoadAddress == 0)) ||
            builder.Arm7i.EntryAddress != builder.Arm7i.LoadAddress)
        {
            throw new InvalidDataException("A DSi recipe requires matching ARM7i entry/load addresses and non-empty data, or an explicit empty digital tuple with a nonzero load address.");
        }

        if (builder.DsiMetadata.Integrity is null)
        {
            throw new InvalidDataException("A DSi recipe must name how authentication fields are populated.");
        }

        if (builder.DsiMetadata.Digests is not null)
        {
            builder.DsiMetadata.Digests.Validate();
            if (!builder.DsiMetadata.Integrity.ComputesHmacSha1)
            {
                throw new InvalidDataException("DSi digest tables require an explicit HMAC-SHA1 key policy.");
            }
        }
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
