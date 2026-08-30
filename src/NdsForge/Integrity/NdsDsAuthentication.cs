using System.Security.Cryptography;

namespace NdsForge;

/// <summary>Computes late-DS authentication inputs without supplying publisher credentials or inferring signature trust.</summary>
public static class NdsDsAuthentication
{
    /// <summary>Limits authenticated payload prefixes to the format's shared budget of 512 KiB.</summary>
    private const int PayloadSectorBudget = 1024;

    /// <summary>Defines the physical sector width included when an authenticated payload ends within a sector.</summary>
    private const int SectorLength = 512;

    /// <summary>
    /// Lists the exact ordered image intervals covered by the late-DS aggregate overlay HMAC: the ARM9 overlay
    /// table, its leading FAT records, and bounded sector-rounded payload prefixes in FAT order.
    /// </summary>
    /// <param name="image">Original-DS image whose ARM9 overlays occupy the complete leading FAT prefix.</param>
    /// <returns>Immutable ordered regions, or an empty list when the image contains no ARM9 overlays.</returns>
    /// <exception cref="InvalidDataException">Overlay allocations do not form the supported prefix or required padding lies outside the image.</exception>
    /// <exception cref="ArgumentException">The image selects DSi authentication rather than late-DS authentication.</exception>
    public static IReadOnlyList<NdsRegion> GetOverlayHashRegions(NdsImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Header.Kind != NdsImageKind.NintendoDs)
        {
            throw new ArgumentException("DSi images use a separate authentication hierarchy.", nameof(image));
        }

        int count = image.Arm9Overlays.Count;
        if (count == 0)
        {
            return Array.Empty<NdsRegion>();
        }

        ValidateAllocationPrefix(image, count);
        var regions = new List<NdsRegion>(checked(count + 2))
        {
            image.Header.Arm9OverlayTable,
            new(image.Header.FileAllocationTable.Offset, checked(count * 8L)),
        };
        int sectorsLeft = PayloadSectorBudget;
        for (int index = 0; index < count; index++)
        {
            NdsRegion allocation = image.FileSystem.Allocations[index].Data;
            long roundedSectors = checked((allocation.Length + SectorLength - 1) / SectorLength);
            int sectors = checked((int)Math.Min(roundedSectors, sectorsLeft / (count - index)));
            var covered = new NdsRegion(allocation.Offset, checked(sectors * (long)SectorLength));
            RequireWithinImage(image, covered);
            if (!covered.IsEmpty)
            {
                regions.Add(covered);
            }

            sectorsLeft -= sectors;
        }

        return regions.AsReadOnly();
    }

    /// <summary>
    /// Computes HMAC-SHA1 over the exact regions returned by <see cref="GetOverlayHashRegions"/> using a caller's
    /// late-DS key. This is distinct from the ARM9-embedded per-overlay key. With no overlays the raw result is
    /// HMAC of empty input; an absent on-cartridge aggregate field instead remains twenty zero bytes.
    /// </summary>
    /// <param name="image">Original-DS image retained by the caller throughout calculation.</param>
    /// <param name="key">Non-empty caller-supplied late-DS authentication key; no default key is provided.</param>
    /// <returns>The twenty-byte aggregate digest, without changing any image bytes or claiming RSA authenticity.</returns>
    public static byte[] ComputeOverlayHmac(NdsImage image, ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (key.IsEmpty)
        {
            throw new ArgumentException("A late-DS authentication key cannot be empty.", nameof(key));
        }

        IReadOnlyList<NdsRegion> regions = GetOverlayHashRegions(image);
        using IncrementalHash hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, key);
        byte[] buffer = new byte[8192];
        foreach (NdsRegion region in regions)
        {
            using Stream stream = image.OpenRead(region);
            int length;
            while ((length = stream.Read(buffer)) != 0)
            {
                hash.AppendData(buffer.AsSpan(0, length));
            }
        }

        return hash.GetHashAndReset();
    }

    /// <summary>Rejects unsupported allocation selections instead of accidentally authenticating unrelated named files.</summary>
    private static void ValidateAllocationPrefix(NdsImage image, int count)
    {
        if (image.FileSystem.Allocations.Count < count)
        {
            throw new InvalidDataException("The ARM9 authentication allocation prefix exceeds the FAT.");
        }

        var seen = new bool[count];
        foreach (NdsOverlay overlay in image.Arm9Overlays)
        {
            if (overlay.FileId >= count || seen[checked((int)overlay.FileId)])
            {
                throw new InvalidDataException(
                    "Late-DS aggregate authentication requires ARM9 overlays to reference every leading FAT entry exactly once.");
            }

            seen[checked((int)overlay.FileId)] = true;
        }

        RequireWithinImage(image, image.Header.Arm9OverlayTable);
        RequireWithinImage(image, new(image.Header.FileAllocationTable.Offset, checked(count * 8L)));
    }

    /// <summary>Checks rounded authentication coverage against physical EOF before creating any source stream.</summary>
    private static void RequireWithinImage(NdsImage image, NdsRegion region)
    {
        if (region.Offset < 0 || region.Length < 0 || region.Offset > image.Length - region.Length)
        {
            throw new InvalidDataException("Late-DS authentication coverage extends beyond the physical image.");
        }
    }
}
