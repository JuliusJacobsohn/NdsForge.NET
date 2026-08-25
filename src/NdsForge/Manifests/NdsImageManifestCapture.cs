using System.Security.Cryptography;

namespace NdsForge;

/// <summary>Streams image regions into stable manifest DTOs without materializing payloads or leaking source ownership.</summary>
internal static class NdsImageManifestCapture
{
    /// <summary>Captures all manifest sections in deterministic order while honoring cancellation between components.</summary>
    /// <param name="image">Live parsed source.</param>
    /// <param name="cancellationToken">Cancels hashing and enumeration.</param>
    /// <returns>A complete validated manifest.</returns>
    public static async ValueTask<NdsImageManifest> CaptureAsync(
        NdsImage image,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        NdsManifestProgram[] programs = await CaptureProgramsAsync(image, cancellationToken).ConfigureAwait(false);
        var allocations = new List<NdsManifestAllocation>(image.FileSystem.Allocations.Count);
        var allocationHashes = new Dictionary<int, string>(image.FileSystem.Allocations.Count);
        foreach (NdsFileAllocation allocation in image.FileSystem.Allocations.OrderBy(static value => value.FileId))
        {
            string hash = await HashRegionAsync(image, allocation.Data, cancellationToken).ConfigureAwait(false);
            allocationHashes.Add(allocation.FileId, hash);
            allocations.Add(new()
            {
                FileId = allocation.FileId,
                Offset = allocation.Data.Offset,
                Length = allocation.Data.Length,
                Sha256 = hash,
            });
        }

        var files = new List<NdsManifestFile>(image.FileSystem.Files.Count);
        foreach (NdsFile file in image.FileSystem.Files.OrderBy(static value => value.FullPath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(new()
            {
                Path = file.FullPath,
                FileId = file.Id,
                Offset = file.Data.Offset,
                Length = file.Data.Length,
                Sha256 = allocationHashes[file.Id],
            });
        }

        var overlays = new List<NdsManifestOverlay>();
        foreach (NdsOverlay overlay in image.Arm9Overlays.Concat(image.Arm7Overlays)
            .OrderBy(static value => value.Processor).ThenBy(static value => value.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            overlays.Add(new()
            {
                Processor = overlay.Processor,
                OverlayId = overlay.Id,
                FileId = overlay.FileId,
                FilePath = overlay.File?.FullPath,
                Offset = overlay.Data?.Offset,
                Length = overlay.Data?.Length,
                LoadAddress = overlay.LoadAddress,
                RamSize = overlay.RamSize,
                BssSize = overlay.BssSize,
                StaticInitializerStart = overlay.StaticInitializerStart,
                StaticInitializerEnd = overlay.StaticInitializerEnd,
                CompressedSize = overlay.CompressedSize,
                Flags = overlay.Flags,
                Sha256 = overlay.Data is not null
                    ? allocationHashes[checked((int)overlay.FileId)]
                    : null,
            });
        }

        NdsHeader header = image.Header;
        string? debugRomHash = header.DebugRomSize == 0
            ? null
            : await HashRegionAsync(image, header.DebugRom, cancellationToken).ConfigureAwait(false);
        var manifest = new NdsImageManifest
        {
            PhysicalLength = image.Length,
            ImageSha256 = await HashRegionAsync(image, new(0, image.Length), cancellationToken).ConfigureAwait(false),
            Header = new()
            {
                Title = header.Title,
                GameCode = header.GameCode,
                MakerCode = header.MakerCode,
                Kind = header.Kind,
                Version = header.Version,
                RegionCode = header.RegionCode,
                DsiFlags = header.DsiFlags,
                AutoStart = header.AutoStart,
                UsedImageSize = header.UsedImageSize,
                DeviceCapacityBytes = header.DeviceCapacityBytes,
                NormalCardControl = header.NormalCardControl,
                SecureCardControl = header.SecureCardControl,
                DebugRomOffset = header.DebugRomOffset,
                DebugRomSize = header.DebugRomSize,
                DebugLoadAddress = header.DebugLoadAddress,
                DebugRomSha256 = debugRomHash,
                Sha256 = HashMemory(header.RawData.Span),
            },
            Dsi = CaptureDsi(header.Dsi),
            Programs = programs,
            Directories = image.FileSystem.Directories
                .Select(static value => value.FullPath)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            Files = files.AsReadOnly(),
            Allocations = allocations.AsReadOnly(),
            Overlays = overlays.AsReadOnly(),
            Banner = CaptureBanner(image),
        };
        manifest.Validate();
        return manifest;
    }

    /// <summary>Hashes present Programs in processor order without treating an ARM9 SDK footer as executable content.</summary>
    /// <param name="image">Live source.</param>
    /// <param name="cancellationToken">Cancels each region hash.</param>
    /// <returns>Two DS entries plus two DSi entries when extended Programs exist.</returns>
    private static async ValueTask<NdsManifestProgram[]> CaptureProgramsAsync(
        NdsImage image,
        CancellationToken cancellationToken)
    {
        NdsProgram[] programs = [image.Header.Arm9, image.Header.Arm7];
        IEnumerable<NdsProgram> all = programs.Concat(
            new[] { image.Header.Arm9i, image.Header.Arm7i }.OfType<NdsProgram>());
        var result = new List<NdsManifestProgram>();
        foreach (NdsProgram program in all.OrderBy(static value => value.Processor))
        {
            result.Add(new()
            {
                Processor = program.Processor,
                Offset = program.Data.Offset,
                Length = program.Data.Length,
                LoadAddress = program.LoadAddress,
                EntryAddress = program.EntryAddress,
                Sha256 = await HashRegionAsync(image, program.Data, cancellationToken).ConfigureAwait(false),
            });
        }

        return result.ToArray();
    }

    /// <summary>Copies selected extended-header values into JSON-friendly scalars and interval objects.</summary>
    /// <param name="dsi">Optional parsed extension.</param>
    /// <returns>A detached DSi snapshot, or <see langword="null"/> for DS-only input.</returns>
    private static NdsManifestDsi? CaptureDsi(NdsDsiHeader? dsi) => dsi is null ? null : new()
    {
        TitleId = dsi.TitleId,
        TotalImageSize = dsi.TotalImageSize,
        RegionFlags = dsi.RegionFlags,
        AccessControl = dsi.AccessControl,
        ScfgExtMask = dsi.ScfgExtMask,
        ApplicationFlags = dsi.ApplicationFlags,
        EulaVersion = dsi.EulaVersion,
        AgeRatingsUsage = dsi.AgeRatingsUsage,
        MemoryBankSettingsHex = Convert.ToHexStringLower(dsi.MemoryBankSettings.Span),
        SharedDataFileSizesHex = Convert.ToHexStringLower(dsi.SharedDataFileSizes.ToArray()),
        AgeRatingsHex = Convert.ToHexStringLower(dsi.AgeRatings.Span),
        HasModcryptAreas = dsi.HasModcryptAreas,
        UsesInsecureModcryptKey = dsi.UsesInsecureModcryptKey,
        ModcryptArea1 = CaptureRegion(dsi.ModcryptArea1),
        ModcryptArea2 = CaptureRegion(dsi.ModcryptArea2),
    };

    /// <summary>Copies a value-type region into an explicit manifest transport object.</summary>
    /// <param name="region">Half-open source interval.</param>
    /// <returns>Detached offset and length scalars.</returns>
    private static NdsManifestRegion CaptureRegion(NdsRegion region) => new()
    {
        Offset = region.Offset,
        Length = region.Length,
    };

    /// <summary>Copies native banner identity, hashes, and language text without rendering or image-codec dependencies.</summary>
    /// <param name="image">Image supplying the optional parsed banner and its absolute offset.</param>
    /// <returns>A detached banner snapshot, or <see langword="null"/> when absent.</returns>
    private static NdsManifestBanner? CaptureBanner(NdsImage image)
    {
        NdsBanner? banner = image.Banner;
        return banner is null ? null : new()
        {
            Offset = image.Header.BannerOffset,
            Length = banner.RawData.Length,
            Version = banner.Version,
            IsAnimated = banner.IsAnimated,
            Titles = banner.Titles.ToDictionary(
                static pair => pair.Key.ToString(),
                static pair => pair.Value,
                StringComparer.Ordinal),
            Sha256 = HashMemory(banner.RawData.Span),
        };
    }

    /// <summary>Streams one validated interval through SHA-256 and returns canonical lowercase hexadecimal text.</summary>
    /// <param name="image">Live source used to open a bounded reader.</param>
    /// <param name="region">Exact bytes included in the digest.</param>
    /// <param name="cancellationToken">Cancels stream hashing.</param>
    /// <returns>A 64-character lowercase digest.</returns>
    private static async ValueTask<string> HashRegionAsync(
        NdsImage image,
        NdsRegion region,
        CancellationToken cancellationToken)
    {
        using Stream stream = image.OpenRead(region);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>Hashes already resident header or banner bytes without an unnecessary stream adapter.</summary>
    /// <param name="data">Exact native structure bytes.</param>
    /// <returns>A 64-character lowercase digest.</returns>
    private static string HashMemory(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
