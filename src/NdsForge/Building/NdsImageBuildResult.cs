namespace NdsForge;

/// <summary>Reports the concrete Layout identities assigned while committing a Build Recipe.</summary>
public sealed class NdsImageBuildResult
{
    /// <summary>Captures final Regions after the output has passed optional reopen verification.</summary>
    /// <param name="usedSize">Exclusive end of meaningful image content recorded in the header.</param>
    /// <param name="physicalSize">Actual destination length, including deterministic final alignment.</param>
    /// <param name="arm9">Final ARM9 cartridge Region.</param>
    /// <param name="arm9Footer">Optional SDK footer excluded from the Program size tuple.</param>
    /// <param name="arm9OverlayTable">Generated ARM9 Overlay table Region.</param>
    /// <param name="arm7">Final ARM7 cartridge Region.</param>
    /// <param name="arm7OverlayTable">Generated ARM7 Overlay table Region.</param>
    /// <param name="fileNameTable">Final FNT Region.</param>
    /// <param name="fileAllocationTable">Final FAT Region.</param>
    /// <param name="banner">Final Banner Region, or <see langword="null"/> when absent.</param>
    /// <param name="debugProgram">Final debug executable Region, or <see langword="null"/> when absent.</param>
    /// <param name="arm9i">Final ARM9i Region, or <see langword="null"/> for a DS-only build.</param>
    /// <param name="arm7i">Final ARM7i Region, or <see langword="null"/> for a DS-only build.</param>
    /// <param name="sectorHashTable">Generated first-level DSi digest table, or an empty region.</param>
    /// <param name="blockHashTable">Generated second-level DSi digest table, or an empty region.</param>
    /// <param name="fileCount">Number of named NitroFS payloads assigned File IDs.</param>
    /// <param name="allocationCount">Total FAT records, including unnamed Overlay payloads.</param>
    internal NdsImageBuildResult(
        long usedSize,
        long physicalSize,
        NdsRegion arm9,
        NdsRegion? arm9Footer,
        NdsRegion arm9OverlayTable,
        NdsRegion arm7,
        NdsRegion arm7OverlayTable,
        NdsRegion fileNameTable,
        NdsRegion fileAllocationTable,
        NdsRegion? banner,
        NdsRegion? debugProgram,
        NdsRegion? arm9i,
        NdsRegion? arm7i,
        NdsRegion sectorHashTable,
        NdsRegion blockHashTable,
        int fileCount,
        int allocationCount)
    {
        UsedSize = usedSize;
        PhysicalSize = physicalSize;
        Arm9 = arm9;
        Arm9Footer = arm9Footer;
        Arm9OverlayTable = arm9OverlayTable;
        Arm7 = arm7;
        Arm7OverlayTable = arm7OverlayTable;
        FileNameTable = fileNameTable;
        FileAllocationTable = fileAllocationTable;
        Banner = banner;
        DebugProgram = debugProgram;
        Arm9i = arm9i;
        Arm7i = arm7i;
        SectorHashTable = sectorHashTable;
        BlockHashTable = blockHashTable;
        FileCount = fileCount;
        AllocationCount = allocationCount;
    }

    /// <summary>Identifies the meaningful content end written to header offset <c>0x80</c>.</summary>
    public long UsedSize { get; }

    /// <summary>Reports destination bytes after final alignment and excludes no hidden sparse extent.</summary>
    public long PhysicalSize { get; }

    /// <summary>Locates the primary processor payload selected by header offsets <c>0x20</c>-<c>0x2F</c>.</summary>
    public NdsRegion Arm9 { get; }

    /// <summary>Locates a recognized SDK footer immediately after ARM9 while excluding it from executable length.</summary>
    public NdsRegion? Arm9Footer { get; }

    /// <summary>Locates generated 32-byte ARM9 Overlay records in caller insertion order.</summary>
    public NdsRegion Arm9OverlayTable { get; }

    /// <summary>Locates the secondary processor payload selected by header offsets <c>0x30</c>-<c>0x3F</c>.</summary>
    public NdsRegion Arm7 { get; }

    /// <summary>Locates generated 32-byte ARM7 Overlay records in caller insertion order.</summary>
    public NdsRegion Arm7OverlayTable { get; }

    /// <summary>Locates the generated hierarchy and name table referenced by the common header.</summary>
    public NdsRegion FileNameTable { get; }

    /// <summary>Locates generated start/end records whose array positions are File IDs.</summary>
    public NdsRegion FileAllocationTable { get; }

    /// <summary>Locates optional checksummed menu metadata after final alignment.</summary>
    public NdsRegion? Banner { get; }

    /// <summary>Locates the optional debug executable referenced by common-header debug fields.</summary>
    public NdsRegion? DebugProgram { get; }

    /// <summary>Locates the DSi-mode ARM9 payload appended after common content, or remains absent for DS images.</summary>
    public NdsRegion? Arm9i { get; }

    /// <summary>Locates the DSi-mode ARM7 payload appended after ARM9i, or remains absent for DS images.</summary>
    public NdsRegion? Arm7i { get; }

    /// <summary>Locates generated per-sector DSi HMACs, or remains empty when hierarchical digests are disabled.</summary>
    public NdsRegion SectorHashTable { get; }

    /// <summary>Locates generated HMACs over sector-hash groups, or remains empty when hierarchical digests are disabled.</summary>
    public NdsRegion BlockHashTable { get; }

    /// <summary>Counts named files serialized into both FNT ordering and FAT allocations.</summary>
    public int FileCount { get; }

    /// <summary>Counts every FAT entry, including unnamed payloads reachable only through Overlay records.</summary>
    public int AllocationCount { get; }
}
