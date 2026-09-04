namespace NdsForge;

/// <summary>Bounds the unreadable-on-hardware TWL reservation without treating it as executable data.</summary>
internal static class NdsCartridgeLayoutParser
{
    /// <summary>Reads at most 12 KiB and never claims an aliased stored component as opaque carrier material.</summary>
    internal static NdsRegion GetReadableReservation(long length, NdsHeader header, NdsFileSystem fileSystem, NdsBanner? banner,
        NdsDownloadPlaySignature? signature)
    {
        if (header.Dsi is null || NdsCarrierLayoutParser.IsDigitalCategory((uint)(header.Dsi.TitleId >> 32))) { return default; }
        long offset = NdsBinary.ReadUInt16(header.RawData.Span, 0x92) * 0x80000L;
        if (offset == 0 || offset >= length) { return default; }
        long end = Math.Min(checked(offset + 0x3000), length);
        if (offset < header.HeaderSize || NdsCarrierLayoutParser.GetComponents(header, fileSystem, banner, signature)
            .Any(region => !region.IsEmpty && region.Offset < end && region.End > offset)) { return default; }
        return new(offset, end - offset);
    }

    /// <summary>Diagnoses contradictory boundaries, unavailable reservations, and payloads inside protocol-only intervals.</summary>
    internal static void Validate(NdsHeader header, byte[] twlData, List<NdsDiagnostic> diagnostics)
    {
        if (header.Dsi is null) { return; }
        long ntrEnd = NdsBinary.ReadUInt16(header.RawData.Span, 0x90) * 0x80000L;
        long twlStart = NdsBinary.ReadUInt16(header.RawData.Span, 0x92) * 0x80000L;
        if (ntrEnd == 0 && twlStart == 0)
        {
            diagnostics.Add(new("NDS1563", NdsDiagnosticSeverity.Warning,
                "The DSi cartridge declares no access boundaries; its cartridge protocol layout is unspecified.", new(0x90, 4)));
            return;
        }
        if (ntrEnd == 0 || twlStart == 0 || ntrEnd > twlStart || header.UsedImageSize > ntrEnd)
        {
            diagnostics.Add(new("NDS1564", NdsDiagnosticSeverity.Error,
                "The NTR/TWL cartridge boundaries contradict each other or the common used-image size.", new(0x90, 4)));
        }
        if (twlStart != 0 && twlData.Length != 0x3000)
        {
            diagnostics.Add(new("NDS1565", NdsDiagnosticSeverity.Error,
                "The TWL cartridge reservation is missing, truncated, or overlaps a stored component.", new(twlStart, 0x3000)));
        }
        if (twlStart != 0 && (header.Arm9i is null || header.Arm9i.Data.Offset != twlStart + 0x3000 ||
            header.Arm7i is null || header.Arm7i.Data.Offset < twlStart + 0x7000))
        {
            diagnostics.Add(new("NDS1566", NdsDiagnosticSeverity.Error,
                "DSi cartridge programs do not respect the TWL reservation and 16 KiB ARM9i secure window.", new(0x1C0, 0x20)));
        }
    }
}
