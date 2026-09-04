namespace NdsForge;

/// <summary>Resolves bounded SDK footer and parameter metadata after the common header establishes program regions.</summary>
internal static class NdsProgramMetadataParser
{
    /// <summary>Defines the marker and two pointer words stored after an SDK-built ARM9 payload.</summary>
    private const int FooterLength = 12;
    /// <summary>Defines the common fixed prefix shared by DS and DSi program-parameter structures.</summary>
    private const int ParametersLength = 0x24;
    /// <summary>Identifies SDK metadata without accepting arbitrary bytes at a program boundary as a footer.</summary>
    private const uint NitroCodeMarker = 0xDEC00621;
    /// <summary>Identifies the byte-reversed SDK marker at the end of a canonical ARM9 parameter prefix.</summary>
    private const uint ReversedNitroCodeMarker = 0x2106C0DE;

    /// <summary>Reads ARM9 footer values and every header-declared program-parameter prefix.</summary>
    public static void Parse<TSource>(TSource source, NdsHeader header)
        where TSource : IImageDataSource
    {
        ParseFooter(source, header.Arm9);
        ParseParameters(source, header, header.Arm9, 0x88);
    }

    /// <summary>Asynchronously reads the same metadata without retaining temporary buffers.</summary>
    public static async ValueTask ParseAsync<TSource>(
        TSource source,
        NdsHeader header,
        CancellationToken cancellationToken)
        where TSource : IImageDataSource
    {
        await ParseFooterAsync(source, header.Arm9, cancellationToken).ConfigureAwait(false);
        await ParseParametersAsync(source, header, header.Arm9, 0x88, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads and applies the optional ARM9 footer after proving twelve source bytes remain.</summary>
    private static void ParseFooter<TSource>(TSource source, NdsProgram program)
        where TSource : IImageDataSource
    {
        if (program.Data.End > source.Length - FooterLength)
        {
            return;
        }

        Span<byte> footer = stackalloc byte[FooterLength];
        source.ReadExactly(program.Data.End, footer);
        ApplyFooter(program, footer);
    }

    /// <summary>Reads and applies the optional ARM9 footer through the cancellable data-source contract.</summary>
    private static async ValueTask ParseFooterAsync<TSource>(
        TSource source,
        NdsProgram program,
        CancellationToken cancellationToken)
        where TSource : IImageDataSource
    {
        if (program.Data.End > source.Length - FooterLength)
        {
            return;
        }

        byte[] footer = new byte[FooterLength];
        await source.ReadExactlyAsync(program.Data.End, footer, cancellationToken).ConfigureAwait(false);
        ApplyFooter(program, footer);
    }

    /// <summary>Attaches typed pointer metadata only when the complete footer begins with the SDK marker.</summary>
    private static void ApplyFooter(NdsProgram program, ReadOnlySpan<byte> footer)
    {
        if (NdsBinary.ReadUInt32(footer, 0) != NitroCodeMarker)
        {
            return;
        }

        var region = new NdsRegion(program.Data.End, FooterLength);
        program.Footer = region;
        program.FooterMetadata = new(
            region,
            NdsBinary.ReadUInt32(footer, 4),
            NdsBinary.ReadUInt32(footer, 8));
    }

    /// <summary>Resolves one footer/header pointer and decodes its bounded fixed parameter prefix synchronously.</summary>
    private static void ParseParameters<TSource>(TSource source, NdsHeader header, NdsProgram program, int fieldOffset)
        where TSource : IImageDataSource
    {
        if (!TryGetRelativeOffset(header, program, fieldOffset, out uint relativeOffset) ||
            !TryGetParametersRegion(source.Length, program, relativeOffset, out NdsRegion region))
        {
            return;
        }

        Span<byte> parameters = stackalloc byte[ParametersLength];
        source.ReadExactly(region.Offset, parameters);
        if (HasCanonicalMarkers(parameters))
        {
            program.Parameters = new(region, relativeOffset, program.LoadAddress, parameters);
        }
    }

    /// <summary>Resolves one footer/header pointer and decodes its bounded fixed parameter prefix asynchronously.</summary>
    private static async ValueTask ParseParametersAsync<TSource>(
        TSource source,
        NdsHeader header,
        NdsProgram program,
        int fieldOffset,
        CancellationToken cancellationToken)
        where TSource : IImageDataSource
    {
        if (!TryGetRelativeOffset(header, program, fieldOffset, out uint relativeOffset) ||
            !TryGetParametersRegion(source.Length, program, relativeOffset, out NdsRegion region))
        {
            return;
        }

        byte[] parameters = new byte[ParametersLength];
        await source.ReadExactlyAsync(region.Offset, parameters, cancellationToken).ConfigureAwait(false);
        if (HasCanonicalMarkers(parameters))
        {
            program.Parameters = new(region, relativeOffset, program.LoadAddress, parameters);
        }
    }

    /// <summary>Normalizes footer-relative, late-DS absolute, and DSi-relative parameter pointers into a program offset.</summary>
    private static bool TryGetRelativeOffset(
        NdsHeader header,
        NdsProgram program,
        int fieldOffset,
        out uint relativeOffset)
    {
        if (program.Processor == NdsProcessor.Arm9 && program.FooterMetadata?.ParametersOffset is > 0)
        {
            relativeOffset = program.FooterMetadata.ParametersOffset;
            return true;
        }

        if (header.RawData.Length < fieldOffset + sizeof(uint))
        {
            relativeOffset = 0;
            return false;
        }

        uint encoded = NdsBinary.ReadUInt32(header.RawData.Span, fieldOffset);
        if (encoded == 0)
        {
            relativeOffset = 0;
            return false;
        }

        if (header.Kind == NdsImageKind.NintendoDs)
        {
            if (encoded < program.Data.Offset || encoded - program.Data.Offset > uint.MaxValue)
            {
                relativeOffset = 0;
                return false;
            }

            relativeOffset = checked((uint)(encoded - program.Data.Offset));
            return true;
        }

        relativeOffset = encoded;
        return true;
    }

    /// <summary>Proves the complete fixed prefix remains inside both its program payload and physical source.</summary>
    private static bool TryGetParametersRegion(
        long sourceLength,
        NdsProgram program,
        uint relativeOffset,
        out NdsRegion region)
    {
        long offset = program.Data.Offset + relativeOffset;
        region = new(offset, ParametersLength);
        return program.Data.Length >= ParametersLength &&
            relativeOffset <= program.Data.Length - ParametersLength &&
            offset <= sourceLength - ParametersLength;
    }

    /// <summary>Rejects legacy tool-generated footer placeholders and coincidental in-range pointers.</summary>
    private static bool HasCanonicalMarkers(ReadOnlySpan<byte> parameters) =>
        NdsBinary.ReadUInt32(parameters, 0x1C) == NitroCodeMarker &&
        NdsBinary.ReadUInt32(parameters, 0x20) == ReversedNitroCodeMarker;
}
