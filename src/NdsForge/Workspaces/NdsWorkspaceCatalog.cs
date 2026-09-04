using System.Security.Cryptography;

namespace NdsForge;

/// <summary>Derives complete component roles from a parsed snapshot rather than trusting recipe-supplied offsets.</summary>
internal static class NdsWorkspaceCatalog
{
    /// <summary>Enumerates native inputs in stable role order, reusing inventory hashes for ordinary allocations.</summary>
    internal static async ValueTask<IReadOnlyList<NdsWorkspaceAsset>> CaptureAsync(
        NdsImage image, NdsImageManifest inventory, CancellationToken cancellationToken)
    {
        var assets = new List<NdsWorkspaceAsset>();
        NdsHeader header = image.Header;
        await AddAsync(NdsWorkspaceAssetKind.Header, "components/header.bin", new(0, header.RawData.Length), inventory.Header.Sha256).ConfigureAwait(false);
        await AddAsync(NdsWorkspaceAssetKind.Arm9, "components/arm9.bin", header.Arm9.CompleteData).ConfigureAwait(false);
        await AddAsync(NdsWorkspaceAssetKind.Arm7, "components/arm7.bin", header.Arm7.Data).ConfigureAwait(false);
        if (header.Arm9i is not null) { await AddAsync(NdsWorkspaceAssetKind.Arm9i, "components/arm9i.bin", header.Arm9i.Data).ConfigureAwait(false); }
        if (header.Arm7i is not null) { await AddAsync(NdsWorkspaceAssetKind.Arm7i, "components/arm7i.bin", header.Arm7i.Data).ConfigureAwait(false); }
        await AddAsync(NdsWorkspaceAssetKind.FileNameTable, "tables/fnt.bin", header.FileNameTable).ConfigureAwait(false);
        await AddAsync(NdsWorkspaceAssetKind.FileAllocationTable, "tables/fat.bin", header.FileAllocationTable).ConfigureAwait(false);
        await AddAsync(NdsWorkspaceAssetKind.Arm9OverlayTable, "tables/arm9-overlays.bin", header.Arm9OverlayTable).ConfigureAwait(false);
        await AddAsync(NdsWorkspaceAssetKind.Arm7OverlayTable, "tables/arm7-overlays.bin", header.Arm7OverlayTable).ConfigureAwait(false);
        foreach (NdsManifestAllocation allocation in inventory.Allocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assets.Add(new()
            {
                Kind = NdsWorkspaceAssetKind.Allocation,
                FileId = allocation.FileId,
                Path = FormattableString.Invariant($"allocations/{allocation.FileId:D7}.bin"),
                OriginalOffset = allocation.Offset,
                OriginalLength = allocation.Length,
                OriginalSha256 = allocation.Sha256,
            });
        }
        if (image.Banner is not null)
        {
            await AddAsync(NdsWorkspaceAssetKind.Banner, "components/banner.bin", new(header.BannerOffset, image.Banner.RawData.Length), inventory.Banner!.Sha256).ConfigureAwait(false);
        }
        if (header.DebugRomSize != 0) { await AddAsync(NdsWorkspaceAssetKind.DebugProgram, "components/debug.bin", header.DebugRom, inventory.Header.DebugRomSha256).ConfigureAwait(false); }
        if (image.CarrierLayout.PostHeaderRegion is NdsRegion postHeader)
        {
            await AddAsync(NdsWorkspaceAssetKind.PostHeader, "preservation/post-header.bin", postHeader).ConfigureAwait(false);
        }
        if (image.CarrierLayout is NdsCartridgeLayout { TwlReservedRegion: NdsRegion twl })
        {
            await AddAsync(NdsWorkspaceAssetKind.TwlReservation, "preservation/twl-reservation.bin", twl).ConfigureAwait(false);
        }
        if (header.Dsi is NdsDsiHeader dsi)
        {
            await AddAsync(NdsWorkspaceAssetKind.SectorHashTable, "tables/sector-hashes.bin", dsi.SectorHashTable).ConfigureAwait(false);
            await AddAsync(NdsWorkspaceAssetKind.BlockHashTable, "tables/block-hashes.bin", dsi.BlockHashTable).ConfigureAwait(false);
        }
        if (image.DownloadPlaySignatureRegion is NdsRegion signature)
        {
            await AddAsync(NdsWorkspaceAssetKind.DownloadPlaySignature, "preservation/download-play-signature.bin", signature).ConfigureAwait(false);
        }
        return assets.AsReadOnly();

        async ValueTask AddAsync(NdsWorkspaceAssetKind kind, string path, NdsRegion region, string? hash = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Stream source = image.OpenRead(region);
            hash ??= Convert.ToHexStringLower(await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false));
            assets.Add(new() { Kind = kind, Path = path, OriginalOffset = region.Offset, OriginalLength = region.Length, OriginalSha256 = hash });
        }
    }
}
