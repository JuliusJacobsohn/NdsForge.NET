using NdsForge.Shared;

namespace NdsForge;

/// <summary>Applies explicit native payload edits to a detached, source-derived structural recipe.</summary>
internal static class NdsWorkspaceImporter
{
    /// <summary>Validates the complete baseline and all inputs before returning a mutable, independently owned builder.</summary>
    internal static async ValueTask<NdsImageBuilder> ImportAsync(
        string directory, NdsWorkspaceImportOptions options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(directory);
        NdsWorkspaceRecipe recipe = await NdsWorkspaceInput.ReadRecipeAsync(root, cancellationToken).ConfigureAwait(false);
        using FileStream snapshot = NdsWorkspaceInput.OpenRead(NdsWorkspacePaths.Resolve(root, recipe.SourceImagePath));
        using NdsImage image = await NdsImage.OpenAsync(snapshot, leaveOpen: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        await NdsWorkspaceInput.ValidateBaselineAsync(recipe, image, cancellationToken).ConfigureAwait(false);
        ValidateAllocationRelationships(image);
        IReadOnlyDictionary<NdsWorkspaceAsset, byte[]> changes = await NdsWorkspaceImportInput.ReadChangesAsync(
            root, recipe, options, cancellationToken).ConfigureAwait(false);
        NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(image, cancellationToken).ConfigureAwait(false);
        foreach ((NdsWorkspaceAsset asset, byte[] bytes) in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Apply(builder, image, asset, bytes, options.MaximumAssetBytes);
        }
        return builder;
    }

    /// <summary>Rejects unrepresented orphan allocations or shared private IDs instead of silently discarding relationships.</summary>
    private static void ValidateAllocationRelationships(NdsImage image)
    {
        var represented = image.FileSystem.Files.Select(static file => file.Id).ToHashSet();
        var privateIds = new HashSet<int>();
        foreach (NdsOverlay overlay in image.Arm9Overlays.Concat(image.Arm7Overlays))
        {
            if (overlay.Data is null) { throw new InvalidDataException("A workspace overlay refers to an absent allocation."); }
            int id = checked((int)overlay.FileId);
            if (overlay.File is null && !privateIds.Add(id))
            {
                throw new InvalidDataException("Shared unnamed overlay allocations cannot yet be structurally imported; exact packing preserves them.");
            }
            represented.Add(id);
        }
        if (image.FileSystem.Allocations.Any(allocation => !represented.Contains(allocation.FileId)))
        {
            throw new InvalidDataException("Unreferenced unnamed allocations cannot yet be structurally imported; exact packing preserves them.");
        }
    }

    /// <summary>Updates a supported role without allowing raw table edits to bypass typed builder consistency rules.</summary>
    private static void Apply(NdsImageBuilder builder, NdsImage image, NdsWorkspaceAsset asset, byte[] bytes, int maximumDecodedBytes)
    {
        switch (asset.Kind)
        {
            case NdsWorkspaceAssetKind.Arm9: builder.Arm9 = ReplaceProgram(builder.Arm9!, bytes); break;
            case NdsWorkspaceAssetKind.Arm7: builder.Arm7 = ReplaceProgram(builder.Arm7!, bytes); break;
            case NdsWorkspaceAssetKind.Arm9i: builder.Arm9i = ReplaceProgram(builder.Arm9i!, bytes); break;
            case NdsWorkspaceAssetKind.Arm7i: builder.Arm7i = ReplaceProgram(builder.Arm7i!, bytes); break;
            case NdsWorkspaceAssetKind.Allocation: ReplaceAllocation(builder, image, asset.FileId!.Value, bytes, maximumDecodedBytes); break;
            case NdsWorkspaceAssetKind.Banner: builder.Banner = NdsBanner.Parse(bytes); break;
            case NdsWorkspaceAssetKind.DebugProgram: builder.DebugProgram = new(bytes, builder.DebugProgram!.LoadAddress); break;
            case NdsWorkspaceAssetKind.PostHeader:
                RequireOriginalLength(asset, bytes);
                builder.SetPostHeaderData(bytes);
                break;
            case NdsWorkspaceAssetKind.TwlReservation:
                RequireOriginalLength(asset, bytes);
                builder.SetTwlReservedData(bytes);
                break;
            case NdsWorkspaceAssetKind.DownloadPlaySignature: builder.DownloadPlaySignature = NdsDownloadPlaySignature.Parse(bytes); break;
            default: throw new InvalidDataException("The changed workspace role has no structural payload import rule.");
        }
    }

    /// <summary>Requires explicit fixed-width carrier replacements rather than interpreting truncation as generated or cleared storage.</summary>
    private static void RequireOriginalLength(NdsWorkspaceAsset asset, byte[] bytes)
    {
        if (bytes.LongLength != asset.OriginalLength)
        {
            throw new InvalidDataException($"The {asset.Kind} workspace payload must retain its original reserved length.");
        }
    }

    /// <summary>Retains runtime addresses and an existing ARM9 footer while allowing stored program size to change.</summary>
    private static NdsProgramDefinition ReplaceProgram(NdsProgramDefinition original, byte[] bytes)
    {
        int footerLength = original.Footer.Length;
        if (bytes.Length < footerLength) { throw new InvalidDataException("The edited ARM9 payload is shorter than its required footer."); }
        var program = new NdsProgramDefinition(original.Processor, bytes.AsSpan(0, bytes.Length - footerLength),
            original.LoadAddress, original.EntryAddress);
        if (footerLength != 0) { program.SetFooter(bytes.AsSpan(bytes.Length - footerLength)); }
        return program;
    }

    /// <summary>Updates every known reference to one allocation, retaining compression mode and explicit overlay runtime semantics.</summary>
    private static void ReplaceAllocation(NdsImageBuilder builder, NdsImage image, int fileId, byte[] bytes, int maximumDecodedBytes)
    {
        if (image.FileSystem.TryGetFile(fileId, out NdsFile? file)) { builder.FileSystem.SetFile(file!.FullPath, bytes); }
        foreach (NdsOverlay overlay in image.Arm9Overlays.Concat(image.Arm7Overlays).Where(overlay => overlay.FileId == fileId))
        {
            if (overlay.IsCompressed && (!BlzEngine.TryInspect(bytes, out BlzEngineInfo info) || info.DecodedLength != overlay.RamSize))
            {
                throw new InvalidDataException("An edited compressed overlay must retain its decoded RAM size; use the builder's explicit recompression operation to change runtime size.");
            }
            if (overlay.IsCompressed) { _ = BlzEngine.Decompress(bytes, maximumDecodedBytes); }
            builder.ReplaceOverlay(overlay.Processor, overlay.Id, bytes,
                overlay.IsCompressed ? NdsOverlayCompressionMode.PreserveStorage : NdsOverlayCompressionMode.Uncompressed);
        }
    }
}
