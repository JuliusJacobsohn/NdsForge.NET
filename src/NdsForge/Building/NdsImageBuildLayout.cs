namespace NdsForge;

/// <summary>Freezes every Region used jointly by header, table, and sequential byte serialization.</summary>
/// <param name="Arm9">Final primary Program Region.</param>
/// <param name="Arm9Footer">Optional 12-byte SDK footer immediately following ARM9.</param>
/// <param name="Arm9OverlayTable">Final ARM9 Overlay table Region.</param>
/// <param name="Arm7">Final secondary Program Region.</param>
/// <param name="Arm7OverlayTable">Final ARM7 Overlay table Region.</param>
/// <param name="FileNameTable">Generated FNT Region.</param>
/// <param name="FileAllocationTable">Generated FAT Region.</param>
/// <param name="Banner">Optional menu metadata Region.</param>
/// <param name="FileRegions">FAT payload Regions in File ID order.</param>
/// <param name="Arm9i">Optional DSi-mode ARM9 Region placed after common DS content.</param>
/// <param name="Arm7i">Optional DSi-mode ARM7 Region placed after ARM9i.</param>
/// <param name="UsedSize">Exclusive NTR/common content end encoded at header offset <c>0x80</c>.</param>
/// <param name="PhysicalSize">Final aligned stream length.</param>
internal sealed record NdsImageBuildLayout(
    NdsRegion Arm9,
    NdsRegion? Arm9Footer,
    NdsRegion Arm9OverlayTable,
    NdsRegion Arm7,
    NdsRegion Arm7OverlayTable,
    NdsRegion FileNameTable,
    NdsRegion FileAllocationTable,
    NdsRegion? Banner,
    IReadOnlyList<NdsRegion> FileRegions,
    NdsRegion? Arm9i,
    NdsRegion? Arm7i,
    long UsedSize,
    long PhysicalSize);
