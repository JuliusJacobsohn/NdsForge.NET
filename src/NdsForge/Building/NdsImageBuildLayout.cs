namespace NdsForge;

/// <summary>Freezes every Region used jointly by header, table, and sequential byte serialization.</summary>
/// <param name="Arm9">Final primary Program Region.</param>
/// <param name="Arm7">Final secondary Program Region.</param>
/// <param name="FileNameTable">Generated FNT Region.</param>
/// <param name="FileAllocationTable">Generated FAT Region.</param>
/// <param name="Banner">Optional menu metadata Region.</param>
/// <param name="FileRegions">FAT payload Regions in File ID order.</param>
/// <param name="UsedSize">Exclusive meaningful content end.</param>
/// <param name="PhysicalSize">Final aligned stream length.</param>
internal sealed record NdsImageBuildLayout(
    NdsRegion Arm9,
    NdsRegion Arm7,
    NdsRegion FileNameTable,
    NdsRegion FileAllocationTable,
    NdsRegion? Banner,
    IReadOnlyList<NdsRegion> FileRegions,
    long UsedSize,
    long PhysicalSize);
