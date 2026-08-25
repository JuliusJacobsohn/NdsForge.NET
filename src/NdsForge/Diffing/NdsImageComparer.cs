using System.Globalization;

namespace NdsForge;

/// <summary>Compares hash-bearing manifests so tooling can distinguish content edits from identity and layout changes.</summary>
public static class NdsImageComparer
{
    /// <summary>Captures and compares two live images without taking ownership of either source.</summary>
    /// <param name="left">Baseline image that remains live through hashing.</param>
    /// <param name="right">Target image that remains live through hashing.</param>
    /// <param name="cancellationToken">Cancels either manifest capture before a partial diff is returned.</param>
    /// <returns>A deterministic complete comparison.</returns>
    public static async ValueTask<NdsImageDiff> CompareAsync(
        NdsImage left,
        NdsImage right,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        NdsImageManifest leftManifest = await NdsImageManifest.CaptureAsync(left, cancellationToken).ConfigureAwait(false);
        NdsImageManifest rightManifest = await NdsImageManifest.CaptureAsync(right, cancellationToken).ConfigureAwait(false);
        return Compare(leftManifest, rightManifest);
    }

    /// <summary>Compares detached manifests without reading ROM bytes, making review artifacts usable in offline CI stages.</summary>
    /// <param name="left">Validated baseline manifest.</param>
    /// <param name="right">Validated target manifest.</param>
    /// <returns>All header, Program, file, Overlay, Banner, DSi, and physical-layout changes.</returns>
    public static NdsImageDiff Compare(NdsImageManifest left, NdsImageManifest right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        left.Validate();
        right.Validate();
        var differences = new List<NdsSemanticDifference>();
        CompareHeader(left.Header, right.Header, differences);
        CompareDsi(left.Dsi, right.Dsi, differences);
        ComparePrograms(left.Programs, right.Programs, differences);
        CompareDirectories(left.Directories, right.Directories, differences);
        CompareFiles(left.Files, right.Files, differences);
        CompareAllocations(left.Allocations, right.Allocations, differences);
        CompareOverlays(left.Overlays, right.Overlays, differences);
        CompareBanner(left.Banner, right.Banner, differences);
        AddValue(differences, "Image.PhysicalLength", left.PhysicalLength, right.PhysicalLength, NdsDifferenceKind.Relocated);
        AddValue(differences, "Image.Sha256", left.ImageSha256, right.ImageSha256);
        return new(left.ImageSha256, right.ImageSha256, differences);
    }

    /// <summary>Reports explicitly empty or structural NitroFS directories that have no file from which to infer presence.</summary>
    private static void CompareDirectories(
        IEnumerable<string> left,
        IEnumerable<string> right,
        List<NdsSemanticDifference> output)
    {
        var leftSet = left.ToHashSet(StringComparer.Ordinal);
        var rightSet = right.ToHashSet(StringComparer.Ordinal);
        output.AddRange(leftSet.Except(rightSet, StringComparer.Ordinal).Select(path =>
            new NdsSemanticDifference("Directories[" + path + "]", NdsDifferenceKind.Removed, "present", null)));
        output.AddRange(rightSet.Except(leftSet, StringComparer.Ordinal).Select(path =>
            new NdsSemanticDifference("Directories[" + path + "]", NdsDifferenceKind.Added, null, "present")));
    }

    /// <summary>Compares common identity, size, timing, and lossless raw-header hash fields.</summary>
    private static void CompareHeader(NdsManifestHeader left, NdsManifestHeader right, List<NdsSemanticDifference> output)
    {
        AddValue(output, "Header.Title", left.Title, right.Title);
        AddValue(output, "Header.GameCode", left.GameCode, right.GameCode);
        AddValue(output, "Header.MakerCode", left.MakerCode, right.MakerCode);
        AddValue(output, "Header.Kind", left.Kind, right.Kind);
        AddValue(output, "Header.Version", left.Version, right.Version);
        AddValue(output, "Header.RegionCode", left.RegionCode, right.RegionCode);
        AddValue(output, "Header.DsiFlags", left.DsiFlags, right.DsiFlags);
        AddValue(output, "Header.AutoStart", left.AutoStart, right.AutoStart);
        AddValue(output, "Header.UsedImageSize", left.UsedImageSize, right.UsedImageSize, NdsDifferenceKind.Relocated);
        AddValue(output, "Header.DeviceCapacityBytes", left.DeviceCapacityBytes, right.DeviceCapacityBytes, NdsDifferenceKind.Relocated);
        AddValue(output, "Header.NormalCardControl", left.NormalCardControl, right.NormalCardControl);
        AddValue(output, "Header.SecureCardControl", left.SecureCardControl, right.SecureCardControl);
        AddValue(output, "Header.DebugRomOffset", left.DebugRomOffset, right.DebugRomOffset, NdsDifferenceKind.Relocated);
        AddValue(output, "Header.DebugRomSize", left.DebugRomSize, right.DebugRomSize);
        AddValue(output, "Header.DebugLoadAddress", left.DebugLoadAddress, right.DebugLoadAddress);
        AddValue(output, "Header.DebugRomSha256", left.DebugRomSha256, right.DebugRomSha256);
        AddValue(output, "Header.Sha256", left.Sha256, right.Sha256);
    }

    /// <summary>Compares optional DSi metadata while keeping addition or removal of the entire extension visible.</summary>
    private static void CompareDsi(NdsManifestDsi? left, NdsManifestDsi? right, List<NdsSemanticDifference> output)
    {
        if (left is null || right is null)
        {
            AddPresence(output, "Dsi", left, right);
            return;
        }

        AddValue(output, "Dsi.TitleId", left.TitleId, right.TitleId);
        AddValue(output, "Dsi.TotalImageSize", left.TotalImageSize, right.TotalImageSize, NdsDifferenceKind.Relocated);
        AddValue(output, "Dsi.RegionFlags", left.RegionFlags, right.RegionFlags);
        AddValue(output, "Dsi.AccessControl", left.AccessControl, right.AccessControl);
        AddValue(output, "Dsi.ScfgExtMask", left.ScfgExtMask, right.ScfgExtMask);
        AddValue(output, "Dsi.ApplicationFlags", left.ApplicationFlags, right.ApplicationFlags);
        AddValue(output, "Dsi.EulaVersion", left.EulaVersion, right.EulaVersion);
        AddValue(output, "Dsi.AgeRatingsUsage", left.AgeRatingsUsage, right.AgeRatingsUsage);
        AddValue(output, "Dsi.MemoryBankSettingsHex", left.MemoryBankSettingsHex, right.MemoryBankSettingsHex);
        AddValue(output, "Dsi.SharedDataFileSizesHex", left.SharedDataFileSizesHex, right.SharedDataFileSizesHex);
        AddValue(output, "Dsi.AgeRatingsHex", left.AgeRatingsHex, right.AgeRatingsHex);
        AddValue(output, "Dsi.HasModcryptAreas", left.HasModcryptAreas, right.HasModcryptAreas);
        AddValue(output, "Dsi.UsesInsecureModcryptKey", left.UsesInsecureModcryptKey, right.UsesInsecureModcryptKey);
        CompareRegion("Dsi.ModcryptArea1", left.ModcryptArea1, right.ModcryptArea1, output);
        CompareRegion("Dsi.ModcryptArea2", left.ModcryptArea2, right.ModcryptArea2, output);
    }

    /// <summary>Compares executable identity, content, runtime addresses, and physical placement by processor.</summary>
    private static void ComparePrograms(
        IEnumerable<NdsManifestProgram> left,
        IEnumerable<NdsManifestProgram> right,
        List<NdsSemanticDifference> output)
    {
        Dictionary<NdsProcessor, NdsManifestProgram> leftMap = left.ToDictionary(static value => value.Processor);
        Dictionary<NdsProcessor, NdsManifestProgram> rightMap = right.ToDictionary(static value => value.Processor);
        foreach (NdsProcessor processor in leftMap.Keys.Union(rightMap.Keys).Order())
        {
            string path = $"Programs[{processor}]";
            bool hasBefore = leftMap.TryGetValue(processor, out NdsManifestProgram? before);
            bool hasAfter = rightMap.TryGetValue(processor, out NdsManifestProgram? after);
            if (!hasBefore || !hasAfter)
            {
                AddPresence(output, path, before, after);
                continue;
            }

            NdsManifestProgram leftProgram = before!;
            NdsManifestProgram rightProgram = after!;
            AddValue(output, path + ".Sha256", leftProgram.Sha256, rightProgram.Sha256);
            AddValue(output, path + ".Offset", leftProgram.Offset, rightProgram.Offset, NdsDifferenceKind.Relocated);
            AddValue(output, path + ".Length", leftProgram.Length, rightProgram.Length);
            AddValue(output, path + ".LoadAddress", leftProgram.LoadAddress, rightProgram.LoadAddress);
            AddValue(output, path + ".EntryAddress", leftProgram.EntryAddress, rightProgram.EntryAddress);
        }
    }

    /// <summary>Compares files by path, then recognizes unique same-content remove/add pairs as path moves.</summary>
    private static void CompareFiles(
        IEnumerable<NdsManifestFile> left,
        IEnumerable<NdsManifestFile> right,
        List<NdsSemanticDifference> output)
    {
        Dictionary<string, NdsManifestFile> leftMap = left.ToDictionary(static value => value.Path, StringComparer.Ordinal);
        Dictionary<string, NdsManifestFile> rightMap = right.ToDictionary(static value => value.Path, StringComparer.Ordinal);
        foreach (string path in leftMap.Keys.Intersect(rightMap.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            CompareFile("Files[" + path + "]", leftMap[path], rightMap[path], output);
        }

        List<NdsManifestFile> removed = leftMap.Values.Where(file => !rightMap.ContainsKey(file.Path)).ToList();
        List<NdsManifestFile> added = rightMap.Values.Where(file => !leftMap.ContainsKey(file.Path)).ToList();
        foreach (NdsManifestFile before in removed.ToArray())
        {
            NdsManifestFile[] matches = added
                .Where(after => after.Sha256 == before.Sha256 && after.Length == before.Length)
                .ToArray();
            if (matches.Length != 1 || removed.Count(candidate => candidate.Sha256 == before.Sha256 && candidate.Length == before.Length) != 1)
            {
                continue;
            }

            NdsManifestFile after = matches[0];
            output.Add(new("Files.Path", NdsDifferenceKind.Moved, before.Path, after.Path));
            CompareFile("Files[" + after.Path + "]", before, after, output);
            removed.Remove(before);
            added.Remove(after);
        }

        output.AddRange(removed.Select(file => new NdsSemanticDifference(
            "Files[" + file.Path + "]", NdsDifferenceKind.Removed, file.Sha256, null)));
        output.AddRange(added.Select(file => new NdsSemanticDifference(
            "Files[" + file.Path + "]", NdsDifferenceKind.Added, null, file.Sha256)));
    }

    /// <summary>Compares one path- or content-matched file's hash, FAT identity, size, and image placement.</summary>
    private static void CompareFile(
        string path,
        NdsManifestFile left,
        NdsManifestFile right,
        List<NdsSemanticDifference> output)
    {
        AddValue(output, path + ".Sha256", left.Sha256, right.Sha256);
        AddValue(output, path + ".FileId", left.FileId, right.FileId, NdsDifferenceKind.Renumbered);
        AddValue(output, path + ".Offset", left.Offset, right.Offset, NdsDifferenceKind.Relocated);
        AddValue(output, path + ".Length", left.Length, right.Length);
    }

    /// <summary>Compares the complete FAT, including unnamed allocations that file- and Overlay-centric views can omit.</summary>
    private static void CompareAllocations(
        IEnumerable<NdsManifestAllocation> left,
        IEnumerable<NdsManifestAllocation> right,
        List<NdsSemanticDifference> output)
    {
        Dictionary<int, NdsManifestAllocation> leftMap = left.ToDictionary(static value => value.FileId);
        Dictionary<int, NdsManifestAllocation> rightMap = right.ToDictionary(static value => value.FileId);
        foreach (int fileId in leftMap.Keys.Union(rightMap.Keys).Order())
        {
            string path = $"Allocations[{fileId}]";
            bool hasBefore = leftMap.TryGetValue(fileId, out NdsManifestAllocation? before);
            bool hasAfter = rightMap.TryGetValue(fileId, out NdsManifestAllocation? after);
            if (!hasBefore || !hasAfter)
            {
                AddPresence(output, path, before, after);
                continue;
            }

            NdsManifestAllocation leftAllocation = before!;
            NdsManifestAllocation rightAllocation = after!;
            AddValue(output, path + ".Sha256", leftAllocation.Sha256, rightAllocation.Sha256);
            AddValue(output, path + ".Offset", leftAllocation.Offset, rightAllocation.Offset, NdsDifferenceKind.Relocated);
            AddValue(output, path + ".Length", leftAllocation.Length, rightAllocation.Length);
        }
    }

    /// <summary>Compares Overlay metadata and payload identity by processor plus runtime Overlay ID.</summary>
    private static void CompareOverlays(
        IEnumerable<NdsManifestOverlay> left,
        IEnumerable<NdsManifestOverlay> right,
        List<NdsSemanticDifference> output)
    {
        Dictionary<string, NdsManifestOverlay> leftMap = left.ToDictionary(OverlayKey, StringComparer.Ordinal);
        Dictionary<string, NdsManifestOverlay> rightMap = right.ToDictionary(OverlayKey, StringComparer.Ordinal);
        foreach (string key in leftMap.Keys.Union(rightMap.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            string path = "Overlays[" + key + "]";
            bool hasBefore = leftMap.TryGetValue(key, out NdsManifestOverlay? before);
            bool hasAfter = rightMap.TryGetValue(key, out NdsManifestOverlay? after);
            if (!hasBefore || !hasAfter)
            {
                AddPresence(output, path, before, after);
                continue;
            }

            NdsManifestOverlay leftOverlay = before!;
            NdsManifestOverlay rightOverlay = after!;
            AddValue(output, path + ".Sha256", leftOverlay.Sha256, rightOverlay.Sha256);
            AddValue(output, path + ".FileId", leftOverlay.FileId, rightOverlay.FileId, NdsDifferenceKind.Renumbered);
            AddValue(output, path + ".FilePath", leftOverlay.FilePath, rightOverlay.FilePath, NdsDifferenceKind.Moved);
            AddValue(output, path + ".Offset", leftOverlay.Offset, rightOverlay.Offset, NdsDifferenceKind.Relocated);
            AddValue(output, path + ".Length", leftOverlay.Length, rightOverlay.Length);
            AddValue(output, path + ".LoadAddress", leftOverlay.LoadAddress, rightOverlay.LoadAddress);
            AddValue(output, path + ".RamSize", leftOverlay.RamSize, rightOverlay.RamSize);
            AddValue(output, path + ".BssSize", leftOverlay.BssSize, rightOverlay.BssSize);
            AddValue(output, path + ".StaticInitializerStart", leftOverlay.StaticInitializerStart, rightOverlay.StaticInitializerStart);
            AddValue(output, path + ".StaticInitializerEnd", leftOverlay.StaticInitializerEnd, rightOverlay.StaticInitializerEnd);
            AddValue(output, path + ".CompressedSize", leftOverlay.CompressedSize, rightOverlay.CompressedSize);
            AddValue(output, path + ".Flags", leftOverlay.Flags, rightOverlay.Flags);
        }
    }

    /// <summary>Compares optional native Banner identity, layout, animation format, and every localized title.</summary>
    private static void CompareBanner(NdsManifestBanner? left, NdsManifestBanner? right, List<NdsSemanticDifference> output)
    {
        if (left is null || right is null)
        {
            AddPresence(output, "Banner", left, right);
            return;
        }

        AddValue(output, "Banner.Sha256", left.Sha256, right.Sha256);
        AddValue(output, "Banner.Offset", left.Offset, right.Offset, NdsDifferenceKind.Relocated);
        AddValue(output, "Banner.Length", left.Length, right.Length);
        AddValue(output, "Banner.Version", left.Version, right.Version);
        AddValue(output, "Banner.IsAnimated", left.IsAnimated, right.IsAnimated);
        foreach (string language in left.Titles.Keys.Union(right.Titles.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            left.Titles.TryGetValue(language, out string? before);
            right.Titles.TryGetValue(language, out string? after);
            AddValue(output, $"Banner.Titles[{language}]", before, after);
        }
    }

    /// <summary>Compares one transport region's offset and length as independent layout values.</summary>
    private static void CompareRegion(
        string path,
        NdsManifestRegion left,
        NdsManifestRegion right,
        List<NdsSemanticDifference> output)
    {
        AddValue(output, path + ".Offset", left.Offset, right.Offset, NdsDifferenceKind.Relocated);
        AddValue(output, path + ".Length", left.Length, right.Length, NdsDifferenceKind.Relocated);
    }

    /// <summary>Emits one whole-component addition or removal and ignores the both-present case.</summary>
    private static void AddPresence(List<NdsSemanticDifference> output, string path, object? left, object? right)
    {
        if (left is null && right is not null)
        {
            output.Add(new(path, NdsDifferenceKind.Added, null, "present"));
        }
        else if (left is not null && right is null)
        {
            output.Add(new(path, NdsDifferenceKind.Removed, "present", null));
        }
    }

    /// <summary>Formats unequal scalar values invariantly and appends exactly one classified finding.</summary>
    private static void AddValue<T>(
        List<NdsSemanticDifference> output,
        string path,
        T left,
        T right,
        NdsDifferenceKind kind = NdsDifferenceKind.Modified)
    {
        if (EqualityComparer<T>.Default.Equals(left, right))
        {
            return;
        }

        output.Add(new(path, kind, Format(left), Format(right)));
    }

    /// <summary>Converts nullable scalar values without current-culture or locale-specific formatting.</summary>
    private static string? Format<T>(T value) => value switch
    {
        null => null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>Builds an ordinal compound key for independent ARM9 and ARM7 Overlay namespaces.</summary>
    private static string OverlayKey(NdsManifestOverlay overlay) => $"{overlay.Processor}:{overlay.OverlayId}";
}
