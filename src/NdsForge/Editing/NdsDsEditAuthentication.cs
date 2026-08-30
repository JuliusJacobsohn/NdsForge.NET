namespace NdsForge;

/// <summary>Preflights preservation edits and reserves complete late-DS authentication coverage before finalization.</summary>
internal static class NdsDsEditAuthentication
{
    /// <summary>Rejects implicit stale writes and unsupported credentials before truncating the caller's stream.</summary>
    internal static void Validate(NdsImage image, NdsWriteOptions options, bool hasChanges, string gameCode, bool hasBanner)
    {
        NdsDsIntegrityOptions? integrity = options.DsIntegrity;
        if (image.Header.DsExtended is not { } metadata)
        {
            if (integrity is not null)
            {
                throw new InvalidDataException("Late-DS authentication policies require an original-DS extended header.");
            }

            return;
        }

        NdsProgramFeatures features = metadata.ProgramFeatures;
        if (integrity is null)
        {
            if (hasChanges && (features & (NdsProgramFeatures.AuthenticatesPrograms | NdsProgramFeatures.AuthenticatesBanner)) != 0)
            {
                throw new InvalidDataException("Editing declared late-DS authentication requires an explicit preserve, clear, or regenerate policy.");
            }

            return;
        }

        integrity.Validate(features, hasBanner);
        if (integrity.Mode == NdsDsAuthenticationWriteMode.Regenerate &&
            (features & NdsProgramFeatures.AuthenticatesPrograms) != 0)
        {
            byte[] prefix = NdsDsProgramAuthentication.ReadEncryptedSecureArea(image, integrity.SecureAreaKeyTable);
            if (!string.Equals(gameCode, image.Header.GameCode, StringComparison.Ordinal))
            {
                using Stream source = image.OpenRead(new(image.Header.Arm9.Data.Offset, prefix.Length));
                source.ReadExactly(prefix);
                _ = NdsDsProgramAuthentication.NormalizeSecureArea(prefix, gameCode, integrity.SecureAreaKeyTable);
            }

            _ = NdsDsAuthentication.GetOverlayHashRegions(image);
        }
    }

    /// <summary>Includes sector-rounded relocated payload tails without changing the meaningful used-image end.</summary>
    internal static long CompletePhysicalSize(NdsImage image, NdsWriteOptions options, NdsRegion[] allocations, long physicalSize)
    {
        if (options.DsIntegrity?.Mode != NdsDsAuthenticationWriteMode.Regenerate ||
            (image.Header.DsExtended!.ProgramFeatures & NdsProgramFeatures.AuthenticatesPrograms) == 0)
        {
            return physicalSize;
        }

        IReadOnlyList<NdsRegion> regions = NdsDsAuthentication.GetOverlayHashRegions(long.MaxValue,
            image.Header.Arm9OverlayTable, image.Header.FileAllocationTable, allocations,
            image.Arm9Overlays.Select(static item => item.FileId).ToArray());
        long required = Math.Max(physicalSize, regions.Count == 0 ? 0 : regions.Max(static region => region.End));
        if (required > uint.MaxValue)
        {
            throw new InvalidDataException("Rounded late-DS authentication coverage exceeds the image address space.");
        }

        return required;
    }
}
