namespace NdsForge;

/// <summary>Detects independently declared storage semantics and reads only a bounded, reserved post-header interval.</summary>
internal static class NdsCarrierLayoutParser
{
    /// <summary>Recognizes executable digital title categories; non-executable NAND data is deliberately excluded.</summary>
    internal static bool IsDigitalCategory(uint category) => category is 0x00030004 or 0x00030005 or 0x00030015 or 0x00030017;

    /// <summary>Reads carrier-only bytes after all stored payload and table regions are known.</summary>
    internal static NdsCarrierLayout Parse(IImageDataSource source, NdsHeader header, NdsFileSystem fileSystem, NdsBanner? banner,
        NdsDownloadPlaySignature? signature)
    {
        int length = GetAvailableLength(source.Length, header, fileSystem, banner, signature);
        byte[] data = new byte[length];
        if (length != 0) { source.ReadExactly(0x1000, data); }
        NdsRegion twl = NdsCartridgeLayoutParser.GetReadableReservation(source.Length, header, fileSystem, banner, signature);
        byte[] twlData = new byte[twl.Length];
        if (!twl.IsEmpty) { source.ReadExactly(twl.Offset, twlData); }
        return Create(header, data, twlData);
    }

    /// <summary>Uses asynchronous I/O for the same bounded carrier material and classification.</summary>
    internal static async ValueTask<NdsCarrierLayout> ParseAsync(IImageDataSource source, NdsHeader header,
        NdsFileSystem fileSystem, NdsBanner? banner, NdsDownloadPlaySignature? signature, CancellationToken cancellationToken)
    {
        int length = GetAvailableLength(source.Length, header, fileSystem, banner, signature);
        byte[] data = new byte[length];
        if (length != 0) { await source.ReadExactlyAsync(0x1000, data, cancellationToken).ConfigureAwait(false); }
        NdsRegion twl = NdsCartridgeLayoutParser.GetReadableReservation(source.Length, header, fileSystem, banner, signature);
        byte[] twlData = new byte[twl.Length];
        if (!twl.IsEmpty) { await source.ReadExactlyAsync(twl.Offset, twlData, cancellationToken).ConfigureAwait(false); }
        return Create(header, data, twlData);
    }

    /// <summary>Never interprets a program, table, banner, or FAT allocation as opaque header reservation.</summary>
    private static int GetAvailableLength(long sourceLength, NdsHeader header, NdsFileSystem fileSystem, NdsBanner? banner,
        NdsDownloadPlaySignature? signature)
    {
        long end = Math.Min(0x4000, Math.Min(sourceLength, header.HeaderSize));
        if (end <= 0x1000) { return 0; }
        if (GetComponents(header, fileSystem, banner, signature).Any(region => !region.IsEmpty && region.Offset < end && region.End > 0x1000)) { return 0; }
        return checked((int)(end - 0x1000));
    }

    /// <summary>Lists stored components, excluding coverage intervals that intentionally alias their contents.</summary>
    internal static IEnumerable<NdsRegion> GetComponents(NdsHeader header, NdsFileSystem fileSystem, NdsBanner? banner,
        NdsDownloadPlaySignature? signature)
        => new[]
        {
            header.Arm9.CompleteData, header.Arm7.CompleteData, header.Arm9i?.CompleteData ?? default,
            header.Arm7i?.CompleteData ?? default, header.FileNameTable, header.FileAllocationTable,
            header.Arm9OverlayTable, header.Arm7OverlayTable, header.DebugRom,
            header.Dsi?.SectorHashTable ?? default, header.Dsi?.BlockHashTable ?? default,
            banner is null ? default : new NdsRegion(header.BannerOffset, banner.RawData.Length),
            signature is null ? default : new NdsRegion(header.UsedImageSize, signature.RawData.Length),
        }.Concat(fileSystem.Allocations.Select(static allocation => allocation.Data));

    /// <summary>Uses category declarations, not extensions or the DSi unit bit, and refuses contradictory digital boundaries.</summary>
    private static NdsCarrierLayout Create(NdsHeader header, byte[] data, byte[] twlData)
    {
        uint category = header.RawData.Length >= 0x238 ? NdsBinary.ReadUInt32(header.RawData.Span, 0x234) : 0;
        if (header.Dsi is null && (category >> 16) != 3) { category = 0; }
        var diagnostics = new List<NdsDiagnostic>();
        if (IsDigitalCategory(category))
        {
            if (NdsBinary.ReadUInt32(header.RawData.Span, 0x90) != 0)
            {
                diagnostics.Add(new("NDS1560", NdsDiagnosticSeverity.Error,
                    "A digital title declares nonzero cartridge NTR/TWL access boundaries; its carrier layout is ambiguous.", new(0x90, 4)));
                return new NdsUnknownCarrierLayout(data, diagnostics.AsReadOnly());
            }
            if (data.Length != 0x3000)
            {
                diagnostics.Add(new("NDS1561", NdsDiagnosticSeverity.Error,
                    "The digital SRL's complete 0x1000–0x3FFF reservation is missing, truncated, or overlaps another component.", new(0x1000, 0x3000)));
            }
            ulong titleId = ((ulong)category << 32) | NdsBinary.ReadUInt32(header.RawData.Span, 0x230);
            return new NdsDigitalSrlLayout(titleId, data, diagnostics.AsReadOnly());
        }
        if (category != 0 && category != 0x00030000)
        {
            diagnostics.Add(new("NDS1562", NdsDiagnosticSeverity.Error,
                "The title category is not a supported cartridge or executable digital-SRL category.", new(0x234, 4)));
            return new NdsUnknownCarrierLayout(data, diagnostics.AsReadOnly());
        }
        NdsCartridgeLayoutParser.Validate(header, twlData, diagnostics);
        return new NdsCartridgeLayout(header, data, twlData, diagnostics.AsReadOnly());
    }
}
