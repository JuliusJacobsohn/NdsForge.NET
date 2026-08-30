namespace NdsForge;

/// <summary>Interprets cartridge NAND boundaries separately from stored file extents and save-data completeness.</summary>
internal static class NdsNandHeader
{
    /// <summary>Converts all raw 16-bit values without overflow, including declarations beyond supported build sizes.</summary>
    internal static long Decode(ushort units, NdsImageKind kind) =>
        (long)units * (kind == NdsImageKind.NintendoDs ? 0x20000 : 0x80000);

    /// <summary>Protects known cartridge partitions from newly written payloads without forbidding byte-exact copies of original storage.</summary>
    internal static void ValidateWrite(NdsImage image, NdsRegion target)
    {
        if (target.IsEmpty || image.CarrierLayout.Kind != NdsImageCarrier.Cartridge) { return; }
        long romEnd = image.Header.NandRomEndOffset;
        long writableStart = image.Header.NandWritableStartOffset;
        if ((romEnd != 0 && target.End > romEnd) || (writableStart != 0 && target.End > writableStart))
        {
            throw new InvalidDataException("Preservation payload write crosses a declared NAND partition boundary; automatic partition relocation is not supported.");
        }
    }

    /// <summary>Rejects contradictory structural layouts before destination mutation, without relocating NAND partitions.</summary>
    internal static long RequiredCapacity(NdsImageBuilder builder, long contentLength)
    {
        long romEnd = Decode(builder.NandRomEndUnits, builder.Kind);
        long writableStart = Decode(builder.NandWritableStartUnits, builder.Kind);
        long boundary = Math.Max(romEnd, writableStart);
        if (boundary == 0) { return contentLength; }
        if (builder.Carrier != NdsImageCarrier.Cartridge)
        {
            throw new ArgumentException("NAND partition boundaries require a cartridge carrier.");
        }
        if (boundary > 0x100000000)
        {
            throw new ArgumentException("NAND partition boundaries exceed the supported 4 GiB structural output capacity.");
        }
        if (romEnd != 0 && writableStart != 0 && romEnd > writableStart)
        {
            throw new ArgumentException("The NAND ROM partition extends into the declared writable partition.");
        }
        if ((romEnd != 0 && contentLength > romEnd) || (writableStart != 0 && contentLength > writableStart))
        {
            throw new ArgumentException("Planned content crosses a declared NAND partition boundary; automatic partition relocation is not supported.");
        }
        return Math.Max(contentLength, boundary);
    }

    /// <summary>Reports ambiguous or conflicting declarations without treating NAND address space as missing image bytes.</summary>
    internal static void Validate(NdsImage image, List<NdsDiagnostic> diagnostics)
    {
        long romEnd = image.Header.NandRomEndOffset;
        long writableStart = image.Header.NandWritableStartOffset;
        long boundary = Math.Max(romEnd, writableStart);
        if (boundary == 0) { return; }
        if (image.CarrierLayout.Kind != NdsImageCarrier.Cartridge)
        {
            Add("NDS1594", "Nonzero cartridge NAND partition fields occur in a non-cartridge carrier; no writable-storage interpretation is established.");
            return;
        }
        if (image.SizeInfo.DeviceCapacityBytes is long capacity && boundary > capacity)
        {
            Add("NDS1590", "A declared NAND partition boundary exceeds the header's device capacity.");
        }
        if ((romEnd != 0 && image.SizeInfo.DeclaredContentEnd > romEnd) ||
            (writableStart != 0 && image.SizeInfo.DeclaredContentEnd > writableStart))
        {
            Add("NDS1591", "Declared image content crosses a NAND partition boundary; partition relocation is not supported.");
        }
        if (romEnd != 0 && writableStart != 0 && romEnd > writableStart)
        {
            Add("NDS1592", "The declared NAND ROM end overlaps the declared writable partition.");
        }
        if ((romEnd == 0) != (writableStart == 0))
        {
            Add("NDS1593", "Only one NAND partition boundary is specified; zero does not establish the missing boundary or absence of NAND hardware.");
        }

        void Add(string code, string message) => diagnostics.Add(new(code, NdsDiagnosticSeverity.Warning, message, new(0x94, 4)));
    }
}
