namespace NdsForge;

/// <summary>Places cartridge-only protocol windows separately from compact digital executable layouts.</summary>
internal static class NdsDsiCartridgePlanner
{
    /// <summary>Reserves the full ARM9i secure window and keeps optional digest tables in the NTR area.</summary>
    internal static NdsDsiCartridgeTail Plan(NdsImageBuilder builder, NdsImageBuildContent content,
        NdsImageBuildOptions options, long arm9Offset, long commonEnd)
    {
        long arm7Relative = Align(checked(0x3000L + Math.Max(0x4000, content.Arm9iData.Length)), options.SectionAlignment);
        long twlLength = checked(arm7Relative + content.Arm7iData.Length - 0x3000);
        long cursor = commonEnd;
        NdsRegion ntrDigest = default;
        NdsRegion sectorTable = default;
        NdsRegion blockTable = default;
        if (builder.DsiMetadata!.Digests is { } digests)
        {
            cursor = Align(cursor, digests.SectorSize);
            ntrDigest = new(arm9Offset, checked(cursor - arm9Offset));
            long sectors = checked(DivideRoundUp(ntrDigest.Length, digests.SectorSize) + DivideRoundUp(twlLength, digests.SectorSize));
            sectorTable = new(cursor, checked(sectors * 20));
            cursor = Align(sectorTable.End, options.SectionAlignment);
            blockTable = new(cursor, checked(DivideRoundUp(sectors, digests.BlockSectorCount) * 20));
            cursor = blockTable.End;
        }
        long used = Align(cursor, options.SectionAlignment);
        long boundary = Align(checked(used + (builder.DownloadPlaySignature?.RawData.Length ?? 0)),
            Math.Max(0x80000, options.SectionAlignment));
        var reservation = new NdsRegion(boundary, 0x3000);
        var arm9i = new NdsRegion(reservation.End, content.Arm9iData.Length);
        var arm7i = new NdsRegion(checked(boundary + arm7Relative), content.Arm7iData.Length);
        NdsRegion twlDigest = builder.DsiMetadata.Digests is null ? default : new(arm9i.Offset, twlLength);
        return new(arm9i, arm7i, reservation, ntrDigest, twlDigest, sectorTable, blockTable,
            used, Align(arm7i.End, options.SectionAlignment));
    }

    /// <summary>Rounds a nonnegative offset upward using validated power-of-two alignment.</summary>
    private static long Align(long value, int alignment) => checked((value + alignment - 1) & -(long)alignment);

    /// <summary>Counts full and partial independently covered chunks.</summary>
    private static long DivideRoundUp(long value, int divisor) => checked((value + divisor - 1) / divisor);
}

/// <summary>Freezes cartridge-tail regions and size declarations for the shared image writer.</summary>
/// <param name="Arm9i">DSi primary executable region.</param>
/// <param name="Arm7i">DSi secondary executable region beyond the secure window.</param>
/// <param name="Reservation">Opaque 12 KiB region at the TWL boundary.</param>
/// <param name="NtrDigest">Common content covered before the digest tables.</param>
/// <param name="TwlDigest">DSi executable content covered independently of the reservation.</param>
/// <param name="SectorTable">Optional first-level authentication table.</param>
/// <param name="BlockTable">Optional second-level authentication table.</param>
/// <param name="UsedSize">NTR used size including tables, but excluding TWL content.</param>
/// <param name="PhysicalSize">Aligned total image size including TWL content.</param>
internal sealed record NdsDsiCartridgeTail(NdsRegion Arm9i, NdsRegion Arm7i, NdsRegion Reservation,
    NdsRegion NtrDigest, NdsRegion TwlDigest, NdsRegion SectorTable, NdsRegion BlockTable, long UsedSize, long PhysicalSize);
