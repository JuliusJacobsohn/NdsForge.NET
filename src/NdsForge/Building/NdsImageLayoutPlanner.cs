namespace NdsForge;

/// <summary>Assigns every physical build region from frozen content sizes without consulting mutable stream state.</summary>
internal static class NdsImageLayoutPlanner
{
    /// <summary>Plans common DS components, optional DSi Programs, and optional hierarchical digest tables.</summary>
    /// <param name="builder">Recipe supplying image kind and optional component sizes.</param>
    /// <param name="content">Frozen bytes, declared Program lengths, and final File ID payload order.</param>
    /// <param name="options">Section, file, and compatibility alignment policy.</param>
    /// <returns>One immutable Layout shared unchanged by headers, tables, payload writes, and the build report.</returns>
    public static NdsImageBuildLayout Plan(
        NdsImageBuilder builder,
        NdsImageBuildContent content,
        NdsImageBuildOptions options)
    {
        bool ndstoolProfile = options.Profile == NdsImageBuildProfile.Ndstool1503;
        long cursor = options.HeaderSize;
        NdsRegion arm9 = PlaceProgram(
            ref cursor,
            content.Arm9Data.Length,
            content.Arm9DeclaredLength,
            options.SectionAlignment);
        NdsRegion? arm9Footer = content.Arm9TrailingData.IsEmpty
            ? null
            : Place(ref cursor, content.Arm9TrailingData.Length, alignment: 1);
        NdsRegion arm9Overlays = content.Arm9OverlayTable.Length == 0
            ? default
            : Place(ref cursor, content.Arm9OverlayTable.Length, ndstoolProfile ? 1 : options.SectionAlignment);
        NdsRegion arm7 = PlaceProgram(
            ref cursor,
            content.Arm7Data.Length,
            content.Arm7DeclaredLength,
            options.SectionAlignment);
        NdsRegion arm7Overlays = content.Arm7OverlayTable.Length == 0
            ? default
            : Place(ref cursor, content.Arm7OverlayTable.Length, ndstoolProfile ? 1 : options.SectionAlignment);
        NdsRegion fnt = Place(ref cursor, content.FileSystem.FileNameTable.Length, options.SectionAlignment);
        NdsRegion fat = Place(ref cursor, checked(content.Allocations.Length * 8), options.SectionAlignment);
        NdsRegion? banner = builder.Banner is null
            ? null
            : Place(ref cursor, builder.Banner.RawData.Length, options.SectionAlignment);
        NdsRegion[] files = PlaceAllocations(ref cursor, content, options, ndstoolProfile);
        NdsRegion? debugProgram = builder.DebugProgram is null
            ? null
            : Place(ref cursor, builder.DebugProgram.Contents.Length, options.SectionAlignment);
        return PlanImageEnd(
            builder,
            content,
            options,
            arm9,
            arm9Footer,
            arm9Overlays,
            arm7,
            arm7Overlays,
            fnt,
            fat,
            banner,
            files,
            debugProgram,
            cursor,
            ndstoolProfile);
    }

    /// <summary>Places every FAT payload under deterministic or compatibility-profile alignment.</summary>
    /// <param name="cursor">Current common-content end advanced through every allocation.</param>
    /// <param name="content">Payloads already ordered by final File ID.</param>
    /// <param name="options">File and section alignment values.</param>
    /// <param name="ndstoolProfile">Uses ndstool's per-payload section alignment when true.</param>
    /// <returns>Regions whose array positions are FAT IDs.</returns>
    private static NdsRegion[] PlaceAllocations(
        ref long cursor,
        NdsImageBuildContent content,
        NdsImageBuildOptions options,
        bool ndstoolProfile)
    {
        var files = new NdsRegion[content.Allocations.Length];
        int alignment = ndstoolProfile ? options.SectionAlignment : options.FileAlignment;
        for (int fileId = 0; fileId < files.Length; fileId++)
        {
            files[fileId] = Place(ref cursor, content.Allocations[fileId].Length, alignment);
        }

        return files;
    }

    /// <summary>Completes either the DS final boundary or the DSi Program and digest tail.</summary>
    /// <param name="builder">Recipe selecting DS versus DSi and optional digests.</param>
    /// <param name="content">Frozen DSi Program byte lengths.</param>
    /// <param name="options">Final, section, and digest-table alignment inputs.</param>
    /// <param name="arm9">Common ARM9 Region that anchors NTR digest coverage.</param>
    /// <param name="arm9Footer">Optional common ARM9 trailing bytes.</param>
    /// <param name="arm9Overlays">Common ARM9 Overlay table.</param>
    /// <param name="arm7">Common ARM7 Region.</param>
    /// <param name="arm7Overlays">Common ARM7 Overlay table.</param>
    /// <param name="fnt">Common filename table.</param>
    /// <param name="fat">Common allocation table.</param>
    /// <param name="banner">Optional menu metadata.</param>
    /// <param name="files">Final FAT payload Regions.</param>
    /// <param name="debugProgram">Optional debug executable included in common content.</param>
    /// <param name="commonContentEnd">Exclusive end before any DSi-only data.</param>
    /// <param name="ndstoolProfile">Controls the DS used-size convention.</param>
    /// <returns>The complete DS or DSi Layout.</returns>
    private static NdsImageBuildLayout PlanImageEnd(
        NdsImageBuilder builder,
        NdsImageBuildContent content,
        NdsImageBuildOptions options,
        NdsRegion arm9,
        NdsRegion? arm9Footer,
        NdsRegion arm9Overlays,
        NdsRegion arm7,
        NdsRegion arm7Overlays,
        NdsRegion fnt,
        NdsRegion fat,
        NdsRegion? banner,
        NdsRegion[] files,
        NdsRegion? debugProgram,
        long commonContentEnd,
        bool ndstoolProfile)
    {
        long cursor = commonContentEnd;
        NdsRegion? arm9i = null;
        NdsRegion? arm7i = null;
        NdsRegion? twlReserved = null;
        NdsRegion ntrDigest = default;
        NdsRegion twlDigest = default;
        NdsRegion sectorHashTable = default;
        NdsRegion blockHashTable = default;
        long usedSize;
        long physicalSize;
        int signatureLength = builder.DownloadPlaySignature?.RawData.Length ?? 0;
        if (builder.Kind == NdsImageKind.NintendoDs)
        {
            physicalSize = Align(commonContentEnd, options.FileAlignment);
            usedSize = ndstoolProfile ? physicalSize : commonContentEnd;
            physicalSize = Align(checked(usedSize + signatureLength), options.FileAlignment);
        }
        else if (builder.Carrier == NdsImageCarrier.Cartridge)
        {
            NdsDsiCartridgeTail tail = NdsDsiCartridgePlanner.Plan(builder, content, options, arm9.Offset, commonContentEnd);
            arm9i = tail.Arm9i;
            arm7i = tail.Arm7i;
            twlReserved = tail.Reservation;
            ntrDigest = tail.NtrDigest;
            twlDigest = tail.TwlDigest;
            sectorHashTable = tail.SectorTable;
            blockHashTable = tail.BlockTable;
            usedSize = tail.UsedSize;
            physicalSize = tail.PhysicalSize;
        }
        else
        {
            usedSize = Align(commonContentEnd, options.FileAlignment);
            cursor = checked(usedSize + signatureLength);
            arm9i = Place(ref cursor, content.Arm9iData.Length, alignment: 0x400);
            arm7i = Place(ref cursor, content.Arm7iData.Length, options.SectionAlignment);
            if (builder.DsiMetadata!.Digests is not null)
            {
                NdsDsiDigestOptions digestOptions = builder.DsiMetadata.Digests;
                ntrDigest = new(arm9.Offset, checked(usedSize - arm9.Offset));
                twlDigest = new(arm9i.Value.Offset, checked(arm7i.Value.End - arm9i.Value.Offset));
                long sectorCount = checked(
                    DivideRoundUp(ntrDigest.Length, digestOptions.SectorSize) +
                    DivideRoundUp(twlDigest.Length, digestOptions.SectorSize));
                sectorHashTable = Place(ref cursor, checked(sectorCount * 20), digestOptions.SectorSize);
                long blockCount = DivideRoundUp(sectorCount, digestOptions.BlockSectorCount);
                blockHashTable = Place(ref cursor, checked(blockCount * 20), options.SectionAlignment);
            }

            physicalSize = Align(cursor, options.SectionAlignment);
        }

        if (physicalSize > uint.MaxValue)
        {
            throw new InvalidDataException("The generated image exceeds the DS header's 32-bit offset and size fields.");
        }

        return new(
            arm9,
            arm9Footer,
            arm9Overlays,
            arm7,
            arm7Overlays,
            fnt,
            fat,
            banner,
            files,
            debugProgram,
            arm9i,
            arm7i,
            twlReserved,
            ntrDigest,
            twlDigest,
            sectorHashTable,
            blockHashTable,
            usedSize,
            physicalSize);
    }

    /// <summary>Places a component at the next aligned offset and advances the exclusive Layout cursor.</summary>
    /// <param name="cursor">Current exclusive end updated to the new Region end.</param>
    /// <param name="length">Non-negative component byte count.</param>
    /// <param name="alignment">Validated positive power-of-two boundary.</param>
    /// <returns>The assigned half-open Region.</returns>
    private static NdsRegion Place(ref long cursor, long length, int alignment)
    {
        cursor = Align(cursor, alignment);
        var region = new NdsRegion(cursor, length);
        cursor = region.End;
        return region;
    }

    /// <summary>Advances by physical Program bytes while reporting a potentially rounded header length.</summary>
    /// <param name="cursor">Current physical end aligned and advanced by <paramref name="physicalLength"/>.</param>
    /// <param name="physicalLength">Number of Program bytes supplied to the writer.</param>
    /// <param name="declaredLength">Header-visible size, never smaller than physical data.</param>
    /// <param name="alignment">Boundary applied to the Program start.</param>
    /// <returns>A header-facing Region whose end may extend past the physical cursor for legacy compatibility.</returns>
    private static NdsRegion PlaceProgram(
        ref long cursor,
        int physicalLength,
        int declaredLength,
        int alignment)
    {
        if (declaredLength < physicalLength)
        {
            throw new InvalidDataException("A Program's declared length cannot exclude physical executable bytes.");
        }

        cursor = Align(cursor, alignment);
        var region = new NdsRegion(cursor, declaredLength);
        cursor = checked(cursor + physicalLength);
        return region;
    }

    /// <summary>Rounds an offset upward under a validated power-of-two boundary.</summary>
    /// <param name="value">Non-negative current Layout end.</param>
    /// <param name="alignment">Positive power of two.</param>
    /// <returns>The first compatible offset at or after <paramref name="value"/>.</returns>
    private static long Align(long value, int alignment) => checked((value + alignment - 1) & -alignment);

    /// <summary>Rounds a non-negative count upward into fixed partitions.</summary>
    /// <param name="value">Count being partitioned.</param>
    /// <param name="divisor">Validated positive partition size.</param>
    /// <returns>Number of partitions required to contain every input unit.</returns>
    private static long DivideRoundUp(long value, int divisor) => checked((value + divisor - 1) / divisor);
}
