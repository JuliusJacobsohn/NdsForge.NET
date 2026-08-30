namespace NdsForge;

/// <summary>Rejects preservation payload writes that would overwrite a different declared component before opening the write transaction.</summary>
internal static class NdsEditRegionProtection
{
    /// <summary>Simulates the editor's allocation order without moving programs or assuming the common used-size field includes DSi tails.</summary>
    internal static void Validate(NdsImage image, IReadOnlyList<NdsFileChange> changes,
        NdsBanner? banner, NdsHeaderEdit header, int alignment)
    {
        if (image.CarrierLayout.Kind == NdsImageCarrier.Unknown || image.CarrierLayout.Diagnostics.Any(static item => item.Severity == NdsDiagnosticSeverity.Error))
        {
            throw new InvalidDataException("Semantic preservation writes require a resolved, well-formed carrier layout.");
        }
        NdsRegion[] allocations = image.FileSystem.Allocations.Select(static item => item.Data).ToArray();
        NdsRegion[] components = GetComponents(image, header).Where(static region => !region.IsEmpty).ToArray();
        NdsRegion bannerRegion = image.Banner is null ? default : new(image.Header.BannerOffset, image.Banner.RawData.Length);
        long used = Math.Max(image.Header.UsedImageSize, allocations.Length == 0 ? 0 : allocations.Max(static region => region.End));
        foreach (NdsFileChange change in changes.OrderBy(static item => item.FileId))
        {
            long offset = change.RequiresRelocation ? Align(used, alignment) : allocations[change.FileId].Offset;
            var target = new NdsRegion(offset, change.ReplacementLength);
            Check(target, components);
            Check(target, bannerRegion);
            for (int index = 0; index < allocations.Length; index++)
            {
                if (index != change.FileId) { Check(target, allocations[index]); }
            }
            allocations[change.FileId] = target;
            if (change.RequiresRelocation) { used = target.End; }
        }

        if (banner is not null)
        {
            long offset = bannerRegion.IsEmpty || banner.RawData.Length > bannerRegion.Length
                ? Align(used, alignment) : bannerRegion.Offset;
            bannerRegion = new(offset, banner.RawData.Length);
            Check(bannerRegion, components);
            Check(bannerRegion, allocations);
            used = Math.Max(used, bannerRegion.End);
        }

        if (image.DownloadPlaySignature is not null)
        {
            var trailer = new NdsRegion(used, NdsDownloadPlaySignature.ByteLength);
            Check(trailer, components);
            Check(trailer, allocations);
            Check(trailer, bannerRegion);
        }
    }

    /// <summary>Enumerates distinct stored components, not digest/modcrypt coverage ranges which intentionally overlap payloads.</summary>
    private static IEnumerable<NdsRegion> GetComponents(NdsImage image, NdsHeaderEdit header)
    {
        yield return new(0, image.Header.RawData.Length);
        yield return image.CarrierLayout.PostHeaderRegion ?? default;
        yield return image.Header.Arm9.CompleteData;
        yield return image.Header.Arm7.CompleteData;
        yield return image.Header.FileNameTable;
        yield return image.Header.FileAllocationTable;
        yield return image.Header.Arm9OverlayTable;
        yield return image.Header.Arm7OverlayTable;
        yield return image.Header.DebugRom;
        yield return new(header.DebugRomOffset, header.DebugRomSize);
        if (image.Header.Arm9i is { } arm9i) { yield return arm9i.CompleteData; }
        if (image.Header.Arm7i is { } arm7i) { yield return arm7i.CompleteData; }
        if (image.Header.Dsi is { } dsi)
        {
            yield return dsi.SectorHashTable;
            yield return dsi.BlockHashTable;
        }
    }

    /// <summary>Uses half-open intervals so exact adjacency and zero-length allocations remain legal.</summary>
    private static void Check(NdsRegion target, IEnumerable<NdsRegion> protectedRegions)
    {
        if (target.IsEmpty) { return; }
        if (target.End > uint.MaxValue)
        {
            throw new InvalidDataException("The preservation edit exceeds the image's 32-bit address space.");
        }
        foreach (NdsRegion region in protectedRegions) { Check(target, region); }
    }

    /// <summary>Checks a single component without allocating a collection for each FAT entry.</summary>
    private static void Check(NdsRegion target, NdsRegion region)
    {
        if (!target.IsEmpty && !region.IsEmpty && target.Offset < region.End && region.Offset < target.End)
        {
            throw new InvalidDataException(
                $"Preservation write [0x{target.Offset:X}, 0x{target.End:X}) overlaps another component " +
                $"[0x{region.Offset:X}, 0x{region.End:X}). Use a structural builder to relocate components together.");
        }
    }

    /// <summary>Matches the writer's checked alignment after write options have been validated.</summary>
    private static long Align(long value, int alignment) => checked((value + alignment - 1) & -alignment);
}
