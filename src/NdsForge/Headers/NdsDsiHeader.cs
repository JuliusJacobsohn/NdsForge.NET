namespace NdsForge;

/// <summary>Projects DSi security, digest, memory, title, and save metadata while preserving the complete extension.</summary>
public sealed class NdsDsiHeader
{
    /// <summary>Retains the complete common-plus-extension prefix because RSA coverage begins before the public DSi projection.</summary>
    private readonly ReadOnlyMemory<byte> _rawHeader;
    /// <summary>Slices typed fields from a complete 0x1000-byte DSi header without copying each fixed byte array.</summary>
    /// <param name="rawHeader">Immutable header memory retained by the parent <see cref="NdsHeader"/>.</param>
    internal NdsDsiHeader(ReadOnlyMemory<byte> rawHeader)
    {
        _rawHeader = rawHeader;
        ReadOnlySpan<byte> data = rawHeader.Span;
        RawData = rawHeader.Slice(0x180, 0xE80);
        MemoryBankSettings = rawHeader.Slice(0x180, 0x30);
        MemoryBanks = new(MemoryBankSettings);
        RegionFlags = NdsBinary.ReadUInt32(data, 0x1B0);
        AccessControl = NdsBinary.ReadUInt32(data, 0x1B4);
        ScfgExtMask = NdsBinary.ReadUInt32(data, 0x1B8);
        ApplicationFlags = data[0x1BF];
        Arm7DeviceListAddress = NdsBinary.ReadUInt32(data, 0x1D4);
        NtrDigest = ReadRegion(data, 0x1E0);
        TwlDigest = ReadRegion(data, 0x1E8);
        SectorHashTable = ReadRegion(data, 0x1F0);
        BlockHashTable = ReadRegion(data, 0x1F8);
        DigestSectorSize = NdsBinary.ReadUInt32(data, 0x200);
        DigestBlockSectorCount = NdsBinary.ReadUInt32(data, 0x204);
        BannerSize = NdsBinary.ReadUInt32(data, 0x208);
        SharedDataFileSizes = Array.AsReadOnly(new byte[]
        {
            data[0x20C], data[0x20D], data[0x214], data[0x215], data[0x216], data[0x217],
        });
        EulaVersion = data[0x20E];
        AgeRatingsUsage = data[0x20F];
        TotalImageSize = NdsBinary.ReadUInt32(data, 0x210);
        ModcryptArea1 = ReadRegion(data, 0x220);
        ModcryptArea2 = ReadRegion(data, 0x228);
        uint titleIdLow = NdsBinary.ReadUInt32(data, 0x230);
        uint titleIdHigh = NdsBinary.ReadUInt32(data, 0x234);
        TitleId = ((ulong)titleIdHigh << 32) | titleIdLow;
        PublicSaveSize = NdsBinary.ReadUInt32(data, 0x238);
        PrivateSaveSize = NdsBinary.ReadUInt32(data, 0x23C);
        AgeRatings = rawHeader.Slice(0x2F0, 0x10);
        var ratings = new NdsDsiAgeRating[16];
        for (int index = 0; index < ratings.Length; index++)
        {
            ratings[index] = new((NdsDsiAgeRatingAuthority)index, data[0x2F0 + index]);
        }

        Ratings = Array.AsReadOnly(ratings);
        Arm9Hmac = rawHeader.Slice(0x300, 20);
        Arm7Hmac = rawHeader.Slice(0x314, 20);
        DigestMasterHmac = rawHeader.Slice(0x328, 20);
        BannerHmac = rawHeader.Slice(0x33C, 20);
        Arm9iHmac = rawHeader.Slice(0x350, 20);
        Arm7iHmac = rawHeader.Slice(0x364, 20);
        Arm9WithoutSecureAreaHmac = rawHeader.Slice(0x3A0, 20);
        DebugArguments = rawHeader.Slice(0xE00, 0x180);
        RsaSignature = rawHeader.Slice(0xF80, 0x80);
    }

    /// <summary>Preserves bytes <c>0x180</c>-<c>0xFFF</c>, including reserved and cryptographic fields not yet interpreted.</summary>
    public ReadOnlyMemory<byte> RawData { get; }

    /// <summary>Gets raw global, ARM9, ARM7, and WRAM memory-bank settings.</summary>
    public ReadOnlyMemory<byte> MemoryBankSettings { get; }

    /// <summary>Projects the global and processor-local MBK words plus MBK9 and WRAM control.</summary>
    public NdsDsiMemoryBankConfiguration MemoryBanks { get; }

    /// <summary>Gets permitted DSi region flags.</summary>
    public uint RegionFlags { get; }

    /// <summary>Projects defined territory bits while <see cref="RegionFlags"/> retains all 32 stored bits.</summary>
    public NdsDsiRegionPermissions Regions => (NdsDsiRegionPermissions)RegionFlags;

    /// <summary>Gets territory-field bits not currently assigned to a named region.</summary>
    public uint UnknownRegionFlagBits => RegionFlags & ~0x3Fu;

    /// <summary>Gets DSi access-control flags.</summary>
    public uint AccessControl { get; }

    /// <summary>Projects defined service and storage capabilities while retaining the raw word.</summary>
    public NdsDsiAccessCapabilities AccessControlFlags => (NdsDsiAccessCapabilities)AccessControl;

    /// <summary>Gets access-control bits whose behavior is not currently defined.</summary>
    public uint UnknownAccessControlBits => AccessControl & ~0x8001FFFFu;

    /// <summary>Preserves the SCFG_EXT mask controlling which DSi hardware configuration bits software may change.</summary>
    public uint ScfgExtMask { get; }

    /// <summary>Gets DSi application flags.</summary>
    public byte ApplicationFlags { get; }

    /// <summary>Projects the complete application-policy byte into named capabilities.</summary>
    public NdsDsiApplicationFeatures ApplicationFeatures => (NdsDsiApplicationFeatures)ApplicationFlags;

    /// <summary>Identifies the runtime ARM7 address of the DSi device-list structure used during service initialization.</summary>
    public uint Arm7DeviceListAddress { get; }

    /// <summary>Locates the NTR-mode content range covered by the hierarchical DSi digest tables.</summary>
    public NdsRegion NtrDigest { get; }

    /// <summary>Locates the TWL-mode content range covered by the hierarchical DSi digest tables.</summary>
    public NdsRegion TwlDigest { get; }

    /// <summary>Locates the SHA-1 sector digest array whose grouping is defined by <see cref="DigestSectorSize"/>.</summary>
    public NdsRegion SectorHashTable { get; }

    /// <summary>Locates the second-level SHA-1 array covering groups of sector hashes.</summary>
    public NdsRegion BlockHashTable { get; }

    /// <summary>Defines the content byte granularity represented by each entry in <see cref="SectorHashTable"/>.</summary>
    public uint DigestSectorSize { get; }

    /// <summary>Defines how many sector digests contribute to each entry in <see cref="BlockHashTable"/>.</summary>
    public uint DigestBlockSectorCount { get; }

    /// <summary>Declares the DSi banner allocation, including animated data when the version is <c>0x0103</c>.</summary>
    public uint BannerSize { get; }

    /// <summary>Gets six raw shared-data file-size units in native slot order.</summary>
    public IReadOnlyList<byte> SharedDataFileSizes { get; }

    /// <summary>Gets the title's required EULA revision byte.</summary>
    public byte EulaVersion { get; }

    /// <summary>Preserves the exact byte controlling whether system software evaluates parental ratings.</summary>
    public byte AgeRatingsUsage { get; }

    /// <summary>Reports whether the header requests parental-control rating evaluation.</summary>
    public bool UsesAgeRatings => AgeRatingsUsage != 0;

    /// <summary>Reports the DSi metadata's total content extent, distinct from physical padding and the common used-size field.</summary>
    public uint TotalImageSize { get; }

    /// <summary>Locates the first optional region transformed by DSi modcrypt when corresponding flags enable it.</summary>
    public NdsRegion ModcryptArea1 { get; }

    /// <summary>Locates the second optional region transformed by DSi modcrypt when corresponding flags enable it.</summary>
    public NdsRegion ModcryptArea2 { get; }

    /// <summary>Reports whether either modcrypt interval has content, independently from key-selection flags.</summary>
    public bool HasModcryptAreas => !ModcryptArea1.IsEmpty || !ModcryptArea2.IsEmpty;

    /// <summary>
    /// Indicates that modcrypt uses the public first sixteen header bytes instead of a securely derived normal key.
    /// Either common-header flag <c>0x04</c> or DSi application flag <c>0x80</c> selects this development mode.
    /// </summary>
    public bool UsesInsecureModcryptKey =>
        (_rawHeader.Span[0x1C] & 0x04) != 0 || (ApplicationFlags & 0x80) != 0;

    /// <summary>Selects one declared modcrypt interval while rejecting undefined enum values at the model boundary.</summary>
    /// <param name="area">First or second area whose absolute image region is required.</param>
    /// <returns>The header-declared half-open interval, which may be empty.</returns>
    public NdsRegion GetModcryptArea(NdsModcryptArea area) => area switch
    {
        NdsModcryptArea.First => ModcryptArea1,
        NdsModcryptArea.Second => ModcryptArea2,
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown modcrypt area."),
    };

    /// <summary>Combines low and high words into the platform title identifier used by DSi services and save storage.</summary>
    public ulong TitleId { get; }

    /// <summary>Declares public writable storage bytes requested by DSi software, not an embedded ROM region.</summary>
    public uint PublicSaveSize { get; }

    /// <summary>Declares private writable storage bytes requested by DSi software, not an embedded ROM region.</summary>
    public uint PrivateSaveSize { get; }

    /// <summary>Preserves sixteen territory-specific rating bytes because their flags and authorities vary by slot.</summary>
    public ReadOnlyMemory<byte> AgeRatings { get; }

    /// <summary>Projects all eight assigned and eight unassigned authority slots without dropping raw flag bits.</summary>
    public IReadOnlyList<NdsDsiAgeRating> Ratings { get; }

    /// <summary>Contains the 20-byte SHA-1 HMAC authenticating the common ARM9 payload; no key-validity claim is implied.</summary>
    public ReadOnlyMemory<byte> Arm9Hmac { get; }

    /// <summary>Contains the 20-byte SHA-1 HMAC authenticating the common ARM7 payload; no key-validity claim is implied.</summary>
    public ReadOnlyMemory<byte> Arm7Hmac { get; }

    /// <summary>Contains the 20-byte SHA-1 HMAC authenticating the digest hierarchy's master data.</summary>
    public ReadOnlyMemory<byte> DigestMasterHmac { get; }

    /// <summary>Contains the 20-byte SHA-1 HMAC authenticating DSi banner bytes when required by title metadata.</summary>
    public ReadOnlyMemory<byte> BannerHmac { get; }

    /// <summary>Contains the 20-byte SHA-1 HMAC authenticating the DSi-mode ARM9i payload.</summary>
    public ReadOnlyMemory<byte> Arm9iHmac { get; }

    /// <summary>Contains the 20-byte SHA-1 HMAC authenticating the DSi-mode ARM7i payload.</summary>
    public ReadOnlyMemory<byte> Arm7iHmac { get; }

    /// <summary>Contains the alternate ARM9 HMAC calculated with its secure-area portion excluded.</summary>
    public ReadOnlyMemory<byte> Arm9WithoutSecureAreaHmac { get; }

    /// <summary>Gets raw DSi debug arguments.</summary>
    public ReadOnlyMemory<byte> DebugArguments { get; }

    /// <summary>Preserves the 128-byte header signature for later verification without claiming trust or key provenance.</summary>
    public ReadOnlyMemory<byte> RsaSignature { get; }

    /// <summary>
    /// Verifies the stored signature against an explicitly trusted public key and the preserved final bytes
    /// <c>0x000</c>-<c>0xDFF</c>. A true result establishes authenticity only relative to that caller trust choice.
    /// </summary>
    /// <param name="publicKey">Caller-trusted RSA-1024 key.</param>
    /// <returns><see langword="true"/> when the type-one padded raw-SHA1 RSA signature matches.</returns>
    public bool VerifyRsaSignature(NdsDsiRsaPublicKey publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        return publicKey.VerifyHeader(_rawHeader.Span[..0xE00], RsaSignature.Span);
    }

    /// <summary>Decodes an extended-header offset/length pair without conflating zero length with absent metadata.</summary>
    /// <param name="data">Complete DSi header beginning at cartridge offset zero.</param>
    /// <param name="offset">Byte offset of the unsigned start word.</param>
    /// <returns>A widened half-open region whose physical bounds are validated separately.</returns>
    private static NdsRegion ReadRegion(ReadOnlySpan<byte> data, int offset) =>
        NdsRegion.FromUInt32(NdsBinary.ReadUInt32(data, offset), NdsBinary.ReadUInt32(data, offset + 4));
}
