namespace NdsForge;

/// <summary>Converts validated ELF segments into cartridge-ready Program and Overlay definitions.</summary>
internal static class NdsElfAssembler
{
    /// <summary>Builds the selected processor payload, translates its entrypoint, and optionally imports Overlays.</summary>
    /// <param name="data">Complete ELF bytes whose segment bounds were already proven.</param>
    /// <param name="elf">Validated header and program-table projection.</param>
    /// <param name="processor">Target processor and DS/DSi mode filter.</param>
    /// <param name="options">Output and Overlay resource policy.</param>
    /// <returns>A complete result or a controlled format exception; no partial definitions escape.</returns>
    public static NdsElfImportResult Assemble(
        ReadOnlyMemory<byte> data,
        NdsElfFile elf,
        NdsProcessor processor,
        NdsElfImportOptions options)
    {
        bool twl = processor is NdsProcessor.Arm9i or NdsProcessor.Arm7i;
        NdsElfSegment[] selectedSegments = elf.Segments
            .Where(segment => segment.IsLoadable && !segment.IsOverlay && segment.IsTwl == twl)
            .ToArray();
        NdsElfSegment[] mainSegments = selectedSegments
            .Where(static segment => segment.FileSize != 0)
            .OrderBy(static segment => segment.PhysicalAddress)
            .ToArray();
        if (mainSegments.Length == 0)
        {
            throw new InvalidDataException($"The ELF contains no file-backed segments for {processor}.");
        }

        uint start = mainSegments[0].PhysicalAddress;
        ulong end = mainSegments.Max(static segment => (ulong)segment.PhysicalAddress + segment.FileSize);
        ulong outputLength = end - start;
        if (outputLength > (uint)options.MaxProgramBytes)
        {
            throw new InvalidDataException("The ELF's contiguous Program extent exceeds the configured output limit.");
        }

        ValidateNoFileOverlap(mainSegments);
        var contents = new byte[(int)outputLength];
        foreach (NdsElfSegment segment in mainSegments)
        {
            int sourceOffset = checked((int)segment.FileOffset);
            int destinationOffset = checked((int)(segment.PhysicalAddress - start));
            data.Span.Slice(sourceOffset, checked((int)segment.FileSize)).CopyTo(contents.AsSpan(destinationOffset));
        }

        uint entryPoint = TranslateEntryPoint(elf.EntryPoint, mainSegments);
        var program = new NdsProgramDefinition(processor, contents, start, entryPoint);
        NdsElfSegment[] overlaySegments = elf.Segments
            .Where(static segment => segment.IsLoadable && segment.IsOverlay)
            .ToArray();
        NdsOverlayDefinition[] overlays = options.ImportOverlays &&
            overlaySegments.Length != 0 &&
            processor is NdsProcessor.Arm9 or NdsProcessor.Arm7
            ? ImportOverlays(data, overlaySegments, processor, options)
            : [];
        uint? arm7WramAddress = processor == NdsProcessor.Arm7i
            ? FindArm7WramAddress(selectedSegments)
            : null;
        return new(program, overlays, overlaySegments.Length != 0, arm7WramAddress);
    }

    /// <summary>Rejects intersecting file-backed physical ranges before copy order could silently choose a winner.</summary>
    /// <param name="segments">Address-sorted selected main segments.</param>
    private static void ValidateNoFileOverlap(NdsElfSegment[] segments)
    {
        ulong previousEnd = 0;
        for (int index = 0; index < segments.Length; index++)
        {
            NdsElfSegment segment = segments[index];
            if (index != 0 && segment.PhysicalAddress < previousEnd)
            {
                throw new InvalidDataException("ELF file-backed Program segments overlap in physical memory.");
            }

            previousEnd = (ulong)segment.PhysicalAddress + segment.FileSize;
        }
    }

    /// <summary>Maps a virtual ELF entry through its containing selected segment, preserving it when no mapping exists.</summary>
    /// <param name="entryPoint">Virtual address from the ELF header.</param>
    /// <param name="segments">Selected Program segments eligible to supply a virtual-to-physical mapping.</param>
    /// <returns>Physical cartridge entry address or the original value for linker layouts where both are identical.</returns>
    private static uint TranslateEntryPoint(uint entryPoint, IEnumerable<NdsElfSegment> segments)
    {
        foreach (NdsElfSegment segment in segments)
        {
            ulong virtualEnd = (ulong)segment.VirtualAddress + segment.FileSize;
            if (entryPoint >= segment.VirtualAddress && entryPoint < virtualEnd)
            {
                return checked(segment.PhysicalAddress + (entryPoint - segment.VirtualAddress));
            }
        }

        return entryPoint;
    }

    /// <summary>Finds the first toolchain virtual address in the DSi ARM7 WRAM banks used for header mapping metadata.</summary>
    /// <param name="segments">Selected address-sorted ARM7i segments.</param>
    /// <returns>The first matching address, or <see langword="null"/> when all content targets other memory.</returns>
    private static uint? FindArm7WramAddress(IEnumerable<NdsElfSegment> segments) => segments
        .Select(static segment => segment.VirtualAddress)
        .Cast<uint?>()
        .FirstOrDefault(static address => address is >= 0x0300_0000 and < 0x037F_8000);

    /// <summary>Decodes one short table segment followed by exactly one private payload segment per Overlay record.</summary>
    /// <param name="data">Complete bounded ELF bytes.</param>
    /// <param name="segments">Overlay-flagged loadable headers in table order.</param>
    /// <param name="processor">ARM9 or ARM7 owner of the generated NDS Overlay table.</param>
    /// <param name="options">Overlay-count resource limit.</param>
    /// <returns>Validated immutable definitions preserving toolchain order and packed control words.</returns>
    private static NdsOverlayDefinition[] ImportOverlays(
        ReadOnlyMemory<byte> data,
        NdsElfSegment[] segments,
        NdsProcessor processor,
        NdsElfImportOptions options)
    {
        NdsElfSegment table = segments[0];
        if (table.FileSize == 0 || table.FileSize % 12 != 0)
        {
            throw new InvalidDataException("The ELF Overlay metadata segment must contain complete 12-byte records.");
        }

        int count = checked((int)(table.FileSize / 12));
        if (count > options.MaxOverlays || segments.Length != count + 1)
        {
            throw new InvalidDataException("The ELF Overlay record and payload segment counts are inconsistent or exceed policy.");
        }

        var result = new NdsOverlayDefinition[count];
        ReadOnlySpan<byte> tableData = data.Span.Slice(checked((int)table.FileOffset), checked((int)table.FileSize));
        for (int index = 0; index < count; index++)
        {
            NdsElfSegment payload = segments[index + 1];
            ReadOnlySpan<byte> record = tableData.Slice(index * 12, 12);
            uint control = NdsBinary.ReadUInt32(record, 8);
            byte[] contents = data.Span
                .Slice(checked((int)payload.FileOffset), checked((int)payload.FileSize))
                .ToArray();
            result[index] = new(
                processor,
                checked((uint)index),
                contents,
                payload.VirtualAddress,
                payload.FileSize,
                payload.MemorySize - payload.FileSize,
                NdsBinary.ReadUInt32(record, 0),
                NdsBinary.ReadUInt32(record, 4),
                control & 0x00FF_FFFF,
                (byte)(control >> 24));
        }

        return result;
    }
}
