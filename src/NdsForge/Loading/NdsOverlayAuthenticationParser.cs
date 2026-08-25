using NdsForge.Shared;

namespace NdsForge;

/// <summary>Resolves ARM9-relative Download Play records after overlay identities and SDK footer metadata are known.</summary>
internal static class NdsOverlayAuthenticationParser
{
    /// <summary>Each positional record is one complete HMAC-SHA1 value.</summary>
    private const int RecordLength = 20;

    /// <summary>Reads and decodes ARM9 only when flags or a footer pointer declare a potentially meaningful table.</summary>
    internal static NdsOverlayAuthenticationTable? Parse<TSource>(
        TSource source,
        NdsHeader header,
        IReadOnlyList<NdsOverlay> overlays,
        NdsReadOptions options)
        where TSource : IImageDataSource
    {
        NdsOverlayAuthenticationTable? structural = InspectDeclaration(header, overlays);
        if (structural is null || structural.State != NdsOverlayAuthenticationTableState.Complete)
        {
            return structural;
        }

        int storedLength = GetStoredLength(header.Arm9, options);
        byte[] stored = new byte[storedLength];
        source.ReadExactly(header.Arm9.Data.Offset, stored);
        return DecodeTable(stored, structural.RelativeOffset, overlays, options);
    }

    /// <summary>Asynchronously materializes the same bounded ARM9 payload before sharing the in-memory decoder.</summary>
    internal static async ValueTask<NdsOverlayAuthenticationTable?> ParseAsync<TSource>(
        TSource source,
        NdsHeader header,
        IReadOnlyList<NdsOverlay> overlays,
        NdsReadOptions options,
        CancellationToken cancellationToken)
        where TSource : IImageDataSource
    {
        NdsOverlayAuthenticationTable? structural = InspectDeclaration(header, overlays);
        if (structural is null || structural.State != NdsOverlayAuthenticationTableState.Complete)
        {
            return structural;
        }

        int storedLength = GetStoredLength(header.Arm9, options);
        byte[] stored = new byte[storedLength];
        await source.ReadExactlyAsync(header.Arm9.Data.Offset, stored, cancellationToken).ConfigureAwait(false);
        return DecodeTable(stored, structural.RelativeOffset, overlays, options);
    }

    /// <summary>Separates absent tables from malformed declarations without reading program bytes.</summary>
    private static NdsOverlayAuthenticationTable? InspectDeclaration(
        NdsHeader header,
        IReadOnlyList<NdsOverlay> overlays)
    {
        if (header.Kind != NdsImageKind.NintendoDs)
        {
            return null;
        }

        bool hasAuthenticatedOverlay = overlays.Any(static overlay => overlay.IsAuthenticated);
        NdsProgramFooter? footer = header.Arm9.FooterMetadata;
        if (!hasAuthenticatedOverlay)
        {
            return null;
        }

        if (footer is null)
        {
            return CreateStructural(NdsOverlayAuthenticationTableState.MissingFooter);
        }

        if (footer.OverlayHmacTableOffset == 0)
        {
            return CreateStructural(NdsOverlayAuthenticationTableState.MissingTablePointer);
        }

        return new(
            NdsOverlayAuthenticationTableState.Complete,
            footer.OverlayHmacTableOffset,
            decodedProgramLength: 0,
            NdsProgramStorageEncoding.Plain,
            uncompressedPrefixLength: 0,
            Array.Empty<NdsOverlayAuthenticationRecord>(),
            ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>Decodes a valid BLZ envelope when present and proves every positional digest remains inside ARM9.</summary>
    private static NdsOverlayAuthenticationTable DecodeTable(
        ReadOnlySpan<byte> stored,
        uint relativeOffset,
        IReadOnlyList<NdsOverlay> overlays,
        NdsReadOptions options)
    {
        bool compressed = BlzEngine.TryInspect(stored, out BlzEngineInfo info);
        byte[] decoded = compressed
            ? BlzEngine.Decompress(stored, options.MaximumDecodedProgramBytes)
            : stored.ToArray();
        int prefixLength = compressed ? info.UncompressedPrefixLength : decoded.Length;
        long tableLength = checked(overlays.Count * (long)RecordLength);
        if (relativeOffset > decoded.LongLength || tableLength > decoded.LongLength - relativeOffset)
        {
            return new(
                NdsOverlayAuthenticationTableState.TableOutOfRange,
                relativeOffset,
                decoded.Length,
                compressed ? NdsProgramStorageEncoding.Blz : NdsProgramStorageEncoding.Plain,
                prefixLength,
                Array.Empty<NdsOverlayAuthenticationRecord>(),
                decoded);
        }

        var records = new NdsOverlayAuthenticationRecord[overlays.Count];
        for (int index = 0; index < records.Length; index++)
        {
            int offset = checked((int)relativeOffset + index * RecordLength);
            records[index] = new(index, overlays[index].Id, decoded.AsSpan(offset, RecordLength));
            overlays[index].AuthenticationRecord = records[index];
        }

        return new(
            NdsOverlayAuthenticationTableState.Complete,
            relativeOffset,
            decoded.Length,
            compressed ? NdsProgramStorageEncoding.Blz : NdsProgramStorageEncoding.Plain,
            prefixLength,
            Array.AsReadOnly(records),
            decoded);
    }

    /// <summary>Bounds both stored allocation and possible plain-program retention under one explicit parser limit.</summary>
    private static int GetStoredLength(NdsProgram program, NdsReadOptions options)
    {
        if (program.Data.Length > options.MaximumDecodedProgramBytes || program.Data.Length > Array.MaxLength)
        {
            throw new InvalidDataException(
                $"The ARM9 program length 0x{program.Data.Length:X} exceeds the configured decoded-program limit.");
        }

        return checked((int)program.Data.Length);
    }

    /// <summary>Returns diagnostic-only metadata without retaining program or digest buffers.</summary>
    private static NdsOverlayAuthenticationTable CreateStructural(NdsOverlayAuthenticationTableState state) => new(
        state,
        relativeOffset: 0,
        decodedProgramLength: 0,
        NdsProgramStorageEncoding.Plain,
        uncompressedPrefixLength: 0,
        Array.Empty<NdsOverlayAuthenticationRecord>(),
        ReadOnlyMemory<byte>.Empty);
}
