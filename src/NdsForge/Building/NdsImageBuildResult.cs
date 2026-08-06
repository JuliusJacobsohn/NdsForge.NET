namespace NdsForge;

/// <summary>Reports the concrete Layout identities assigned while committing a Build Recipe.</summary>
public sealed class NdsImageBuildResult
{
    /// <summary>Captures final Regions after the output has passed optional reopen verification.</summary>
    /// <param name="usedSize">Exclusive end of meaningful image content recorded in the header.</param>
    /// <param name="physicalSize">Actual destination length, including deterministic final alignment.</param>
    /// <param name="arm9">Final ARM9 cartridge Region.</param>
    /// <param name="arm7">Final ARM7 cartridge Region.</param>
    /// <param name="fileNameTable">Final FNT Region.</param>
    /// <param name="fileAllocationTable">Final FAT Region.</param>
    /// <param name="banner">Final Banner Region, or <see langword="null"/> when absent.</param>
    /// <param name="fileCount">Number of named NitroFS payloads assigned File IDs.</param>
    internal NdsImageBuildResult(
        long usedSize,
        long physicalSize,
        NdsRegion arm9,
        NdsRegion arm7,
        NdsRegion fileNameTable,
        NdsRegion fileAllocationTable,
        NdsRegion? banner,
        int fileCount)
    {
        UsedSize = usedSize;
        PhysicalSize = physicalSize;
        Arm9 = arm9;
        Arm7 = arm7;
        FileNameTable = fileNameTable;
        FileAllocationTable = fileAllocationTable;
        Banner = banner;
        FileCount = fileCount;
    }

    /// <summary>Identifies the meaningful content end written to header offset <c>0x80</c>.</summary>
    public long UsedSize { get; }

    /// <summary>Reports destination bytes after final alignment and excludes no hidden sparse extent.</summary>
    public long PhysicalSize { get; }

    /// <summary>Locates the primary processor payload selected by header offsets <c>0x20</c>-<c>0x2F</c>.</summary>
    public NdsRegion Arm9 { get; }

    /// <summary>Locates the secondary processor payload selected by header offsets <c>0x30</c>-<c>0x3F</c>.</summary>
    public NdsRegion Arm7 { get; }

    /// <summary>Locates the generated hierarchy and name table referenced by the common header.</summary>
    public NdsRegion FileNameTable { get; }

    /// <summary>Locates generated start/end records whose array positions are File IDs.</summary>
    public NdsRegion FileAllocationTable { get; }

    /// <summary>Locates optional checksummed menu metadata after final alignment.</summary>
    public NdsRegion? Banner { get; }

    /// <summary>Counts named files serialized into both FNT ordering and FAT allocations.</summary>
    public int FileCount { get; }
}
