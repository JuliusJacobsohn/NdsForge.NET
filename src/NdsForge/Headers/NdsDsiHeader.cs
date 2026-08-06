namespace NdsForge;

/// <summary>Represents the lossless typed extension in a DSi-capable image header.</summary>
public sealed class NdsDsiHeader
{
    internal NdsDsiHeader(ReadOnlyMemory<byte> rawHeader)
    {
        ReadOnlySpan<byte> data = rawHeader.Span;
        RawData = rawHeader.Slice(0x180, 0xE80);
        MemoryBankSettings = rawHeader.Slice(0x180, 0x30);
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
        TotalImageSize = NdsBinary.ReadUInt32(data, 0x210);
        ModcryptArea1 = ReadRegion(data, 0x220);
        ModcryptArea2 = ReadRegion(data, 0x228);
        uint titleIdLow = NdsBinary.ReadUInt32(data, 0x230);
        uint titleIdHigh = NdsBinary.ReadUInt32(data, 0x234);
        TitleId = ((ulong)titleIdHigh << 32) | titleIdLow;
        PublicSaveSize = NdsBinary.ReadUInt32(data, 0x238);
        PrivateSaveSize = NdsBinary.ReadUInt32(data, 0x23C);
        AgeRatings = rawHeader.Slice(0x2F0, 0x10);
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

    /// <summary>Gets the complete unmodified extended-header bytes from 0x180 through 0xFFF.</summary>
    public ReadOnlyMemory<byte> RawData { get; }

    /// <summary>Gets raw global, ARM9, ARM7, and WRAM memory-bank settings.</summary>
    public ReadOnlyMemory<byte> MemoryBankSettings { get; }

    /// <summary>Gets permitted DSi region flags.</summary>
    public uint RegionFlags { get; }

    /// <summary>Gets DSi access-control flags.</summary>
    public uint AccessControl { get; }

    /// <summary>Gets the SCFG_EXT mask.</summary>
    public uint ScfgExtMask { get; }

    /// <summary>Gets DSi application flags.</summary>
    public byte ApplicationFlags { get; }

    /// <summary>Gets the ARM7 device-list address.</summary>
    public uint Arm7DeviceListAddress { get; }

    /// <summary>Gets the Nintendo DS-mode digest region.</summary>
    public NdsRegion NtrDigest { get; }

    /// <summary>Gets the DSi-mode digest region.</summary>
    public NdsRegion TwlDigest { get; }

    /// <summary>Gets the sector hash-table region.</summary>
    public NdsRegion SectorHashTable { get; }

    /// <summary>Gets the block hash-table region.</summary>
    public NdsRegion BlockHashTable { get; }

    /// <summary>Gets the digest sector size in bytes.</summary>
    public uint DigestSectorSize { get; }

    /// <summary>Gets the number of sectors represented by each digest block.</summary>
    public uint DigestBlockSectorCount { get; }

    /// <summary>Gets the extended banner size.</summary>
    public uint BannerSize { get; }

    /// <summary>Gets the DSi total image size field.</summary>
    public uint TotalImageSize { get; }

    /// <summary>Gets the first modcrypt area.</summary>
    public NdsRegion ModcryptArea1 { get; }

    /// <summary>Gets the second modcrypt area.</summary>
    public NdsRegion ModcryptArea2 { get; }

    /// <summary>Gets the 64-bit DSi title ID.</summary>
    public ulong TitleId { get; }

    /// <summary>Gets the requested public save size.</summary>
    public uint PublicSaveSize { get; }

    /// <summary>Gets the requested private save size.</summary>
    public uint PrivateSaveSize { get; }

    /// <summary>Gets the 16 raw age-rating bytes.</summary>
    public ReadOnlyMemory<byte> AgeRatings { get; }

    /// <summary>Gets the stored ARM9 HMAC.</summary>
    public ReadOnlyMemory<byte> Arm9Hmac { get; }

    /// <summary>Gets the stored ARM7 HMAC.</summary>
    public ReadOnlyMemory<byte> Arm7Hmac { get; }

    /// <summary>Gets the stored digest-master HMAC.</summary>
    public ReadOnlyMemory<byte> DigestMasterHmac { get; }

    /// <summary>Gets the stored banner HMAC.</summary>
    public ReadOnlyMemory<byte> BannerHmac { get; }

    /// <summary>Gets the stored ARM9i HMAC.</summary>
    public ReadOnlyMemory<byte> Arm9iHmac { get; }

    /// <summary>Gets the stored ARM7i HMAC.</summary>
    public ReadOnlyMemory<byte> Arm7iHmac { get; }

    /// <summary>Gets the ARM9-without-secure-area HMAC.</summary>
    public ReadOnlyMemory<byte> Arm9WithoutSecureAreaHmac { get; }

    /// <summary>Gets raw DSi debug arguments.</summary>
    public ReadOnlyMemory<byte> DebugArguments { get; }

    /// <summary>Gets the stored RSA signature bytes without making an authenticity claim.</summary>
    public ReadOnlyMemory<byte> RsaSignature { get; }

    private static NdsRegion ReadRegion(ReadOnlySpan<byte> data, int offset) =>
        NdsRegion.FromUInt32(NdsBinary.ReadUInt32(data, offset), NdsBinary.ReadUInt32(data, offset + 4));
}

