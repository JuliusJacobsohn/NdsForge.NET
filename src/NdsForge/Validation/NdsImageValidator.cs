namespace NdsForge;

/// <summary>
/// Applies cross-component integrity checks that require both parsed metadata and the source image length.
/// </summary>
internal static class NdsImageValidator
{
    /// <summary>Collects stable diagnostic codes instead of failing fast, allowing callers to report every detected defect.</summary>
    /// <param name="image">A live parsed image whose regions and optional components are inspected.</param>
    /// <param name="options">Optional DSi trust material and development-marker validation policy.</param>
    /// <returns>An immutable result containing checksum, bounds, overlay, and banner findings.</returns>
    public static NdsValidationResult Validate(NdsImage image, NdsValidationOptions options)
    {
        var diagnostics = new List<NdsDiagnostic>();
        ValidateChecksum(
            diagnostics, "NDS1001", "header", image.Header.HeaderCrc,
            image.Header.RawData.Span[..0x15E], new(0, 0x160));
        ValidateChecksum(
            diagnostics, "NDS1002", "Nintendo logo", image.Header.NintendoLogoCrc,
            image.Header.RawData.Span.Slice(0xC0, 156), new(0xC0, 156));

        ValidateRegion(diagnostics, image.Length, "NDS1101", "ARM9 program", image.Header.Arm9.Data);
        ValidateRegion(diagnostics, image.Length, "NDS1102", "ARM7 program", image.Header.Arm7.Data);
        ValidateRegion(diagnostics, image.Length, "NDS1103", "filename table", image.Header.FileNameTable);
        ValidateRegion(diagnostics, image.Length, "NDS1104", "file allocation table", image.Header.FileAllocationTable);
        ValidateRegion(diagnostics, image.Length, "NDS1105", "ARM9 overlay table", image.Header.Arm9OverlayTable);
        ValidateRegion(diagnostics, image.Length, "NDS1106", "ARM7 overlay table", image.Header.Arm7OverlayTable);
        if (image.Header.Arm9i is not null)
        {
            ValidateRegion(diagnostics, image.Length, "NDS1107", "ARM9i program", image.Header.Arm9i.Data);
        }

        if (image.Header.Arm7i is not null)
        {
            ValidateRegion(diagnostics, image.Length, "NDS1108", "ARM7i program", image.Header.Arm7i.Data);
        }

        ValidateOverlays(diagnostics, image.Arm9Overlays);
        ValidateOverlays(diagnostics, image.Arm7Overlays);
        if (image.Banner is not null)
        {
            diagnostics.AddRange(image.Banner.ValidateCrcs(image.Header.BannerOffset));
        }

        NdsDsiIntegrityValidator.Validate(image, diagnostics, options);

        return new NdsValidationResult(diagnostics);
    }

    /// <summary>Compares one stored little-endian CRC16 with the library's Modbus-polynomial calculation.</summary>
    /// <param name="diagnostics">Accumulator receiving a stable error when the values differ.</param>
    /// <param name="code">Public diagnostic identifier assigned to this checksum field.</param>
    /// <param name="name">Human-readable component name included in the diagnostic message.</param>
    /// <param name="stored">Checksum recorded in the image metadata.</param>
    /// <param name="data">Exact protected byte sequence, excluding the checksum field itself.</param>
    /// <param name="region">Image location reported so tools can highlight the affected structure.</param>
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

    /// <summary>Reports malformed header ranges without attempting reads that could escape the source.</summary>
    /// <param name="diagnostics">Accumulator receiving an error for an invalid range.</param>
    /// <param name="imageLength">Physical source length in bytes, rather than the header's claimed used size.</param>
    /// <param name="code">Stable identifier distinguishing the affected header field.</param>
    /// <param name="name">Component name used in the user-facing explanation.</param>
    /// <param name="region">Offset and length interpreted as an overflow-safe half-open interval.</param>
    private static void ValidateRegion(
        List<NdsDiagnostic> diagnostics,
        long imageLength,
        string code,
        string name,
        NdsRegion region)
    {
        if (region.Offset < 0 || region.Length < 0 || region.Offset > imageLength - region.Length)
        {
            diagnostics.Add(new(
                code,
                NdsDiagnosticSeverity.Error,
                $"The {name} region at 0x{region.Offset:X} with length 0x{region.Length:X} is outside the 0x{imageLength:X}-byte image.",
                region));
        }
    }

    /// <summary>Checks overlay relationships that are not guaranteed merely by decoding each 32-byte table entry.</summary>
    /// <param name="diagnostics">Accumulator receiving missing-payload and reversed-initializer errors.</param>
    /// <param name="overlays">One processor's overlays in source table order.</param>
    private static void ValidateOverlays(List<NdsDiagnostic> diagnostics, IEnumerable<NdsOverlay> overlays)
    {
        foreach (NdsOverlay overlay in overlays)
        {
            if (overlay.Data is null)
            {
                diagnostics.Add(new(
                    "NDS1201",
                    NdsDiagnosticSeverity.Error,
                    $"{overlay.Processor} overlay {overlay.Id} references missing FAT file ID {overlay.FileId}."));
            }

            if (overlay.StaticInitializerEnd < overlay.StaticInitializerStart)
            {
                diagnostics.Add(new(
                    "NDS1202",
                    NdsDiagnosticSeverity.Error,
                    $"{overlay.Processor} overlay {overlay.Id} has a reversed static-initializer range."));
            }
        }
    }
}
