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
    /// Computes the late-DS program HMAC over the common header prefix, a complete caller-selected ARM9
    /// authentication representation, and the declared ARM7 bytes, in that order.
    /// </summary>
    /// <param name="headerPrefix">Exactly the first 0x160 image bytes, including the finalized header CRC.</param>
    /// <param name="arm9AuthenticationData">The complete stored ARM9 program with its secure area in the encrypted authentication representation.</param>
    /// <param name="arm7Data">The complete ARM7 program, excluding trailing alignment padding.</param>
    /// <param name="key">Non-empty caller-supplied late-DS program/overlay authentication key.</param>
    /// <returns>The twenty-byte HMAC over the supplied bytes; no image is modified or authenticated implicitly.</returns>
    /// <remarks>
    /// This byte-level primitive neither encrypts nor decompresses programs and does not infer a secure-area key.
    /// Supplying a decrypted dump directly produces its digest, not the encrypted-form cartridge digest.
    /// Callers must finalize program storage and header metadata before calculation.
    /// </remarks>
    public static byte[] ComputeProgramsHmac(
        ReadOnlySpan<byte> headerPrefix,
        ReadOnlySpan<byte> arm9AuthenticationData,
        ReadOnlySpan<byte> arm7Data,
        ReadOnlySpan<byte> key)
    {
        if (headerPrefix.Length != 0x160)
        {
            throw new ArgumentException("Late-DS program authentication covers exactly 0x160 header bytes.", nameof(headerPrefix));
        }

        RequireKey(key);
        using IncrementalHash hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, key);
        hash.AppendData(headerPrefix);
        hash.AppendData(arm9AuthenticationData);
        hash.AppendData(arm7Data);
        return hash.GetHashAndReset();
    }

    /// <summary>Computes the late-DS banner HMAC over the complete version-defined banner, including stored CRCs.</summary>
    /// <param name="banner">Complete immutable banner whose CRCs and contents have already been finalized.</param>
    /// <param name="key">Non-empty caller-supplied late-DS banner key, distinct from the program/overlay key.</param>
    /// <returns>The twenty-byte digest without changing the banner, repairing CRCs, or including external padding.</returns>
    public static byte[] ComputeBannerHmac(NdsBanner banner, ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(banner);
        RequireKey(key);
        using IncrementalHash hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, key);
        hash.AppendData(banner.RawData.Span);
        return hash.GetHashAndReset();
    }

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

        return GetOverlayHashRegions(image.Length, image.Header.Arm9OverlayTable, image.Header.FileAllocationTable,
            image.FileSystem.Allocations.Select(static item => item.Data).ToArray(),
            image.Arm9Overlays.Select(static item => item.FileId).ToArray());
    }

    /// <summary>Shares exact input selection with the writer's preflight and physical-padding planner.</summary>
    internal static IReadOnlyList<NdsRegion> GetOverlayHashRegions(
        long imageLength, NdsRegion overlayTable, NdsRegion fat,
        IReadOnlyList<NdsRegion> allocations, IReadOnlyList<uint> fileIds)
    {
        int count = fileIds.Count;
        if (count == 0)
        {
            return Array.Empty<NdsRegion>();
        }

        ValidateAllocationPrefix(allocations.Count, fileIds);
        RequireWithinImage(imageLength, overlayTable);
        RequireWithinImage(imageLength, new(fat.Offset, checked(count * 8L)));
        var regions = new List<NdsRegion>(checked(count + 2))
        {
            overlayTable,
            new(fat.Offset, checked(count * 8L)),
        };
        int sectorsLeft = PayloadSectorBudget;
        for (int index = 0; index < count; index++)
        {
            NdsRegion allocation = allocations[index];
            long roundedSectors = checked((allocation.Length + SectorLength - 1) / SectorLength);
            int sectors = checked((int)Math.Min(roundedSectors, sectorsLeft / (count - index)));
            var covered = new NdsRegion(allocation.Offset, checked(sectors * (long)SectorLength));
            RequireWithinImage(imageLength, covered);
            if (!covered.IsEmpty)
            {
                regions.Add(covered);
            }

            sectorsLeft -= sectors;
        }

        return regions.AsReadOnly();
    }

    /// <summary>
    /// Computes HMAC-SHA1 over the exact regions returned by <see cref="GetOverlayHashRegions(NdsImage)"/> using a caller's
    /// late-DS key. This is distinct from the ARM9-embedded per-overlay key. With no overlays the raw result is
    /// HMAC of empty input; an absent on-cartridge aggregate field instead remains twenty zero bytes.
    /// </summary>
    /// <param name="image">Original-DS image retained by the caller throughout calculation.</param>
    /// <param name="key">Non-empty caller-supplied late-DS authentication key; no default key is provided.</param>
    /// <returns>The twenty-byte aggregate digest, without changing any image bytes or claiming RSA authenticity.</returns>
    public static byte[] ComputeOverlayHmac(NdsImage image, ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(image);
        RequireKey(key);

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

    /// <summary>Requires intentional caller credentials instead of silently accepting an absent key.</summary>
    private static void RequireKey(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A late-DS authentication key cannot be empty.", nameof(key));
        }
    }

    /// <summary>Rejects unsupported allocation selections instead of accidentally authenticating unrelated named files.</summary>
    private static void ValidateAllocationPrefix(int allocationCount, IReadOnlyList<uint> fileIds)
    {
        int count = fileIds.Count;
        if (allocationCount < count)
        {
            throw new InvalidDataException("The ARM9 authentication allocation prefix exceeds the FAT.");
        }

        var seen = new bool[count];
        foreach (uint fileId in fileIds)
        {
            if (fileId >= count || seen[checked((int)fileId)])
            {
                throw new InvalidDataException(
                    "Late-DS aggregate authentication requires ARM9 overlays to reference every leading FAT entry exactly once.");
            }

            seen[checked((int)fileId)] = true;
        }
    }

    /// <summary>Checks rounded authentication coverage against physical EOF before creating any source stream.</summary>
    private static void RequireWithinImage(long imageLength, NdsRegion region)
    {
        if (region.Offset < 0 || region.Length < 0 || region.Offset > imageLength - region.Length)
        {
            throw new InvalidDataException("Late-DS authentication coverage extends beyond the physical image.");
        }
    }
}
