namespace NdsForge;

/// <summary>Projects the common cartridge header into typed fields while retaining every byte needed for lossless work.</summary>
public sealed class NdsHeader
{
    /// <summary>Decodes the common DS prefix and an authentication or DSi extension when declared.</summary>
    /// <param name="rawData">Exactly the 0x200-byte classic header or a complete 0x1000-byte extended header.</param>
    internal NdsHeader(ReadOnlyMemory<byte> rawData)
    {
        RawData = rawData;
        ReadOnlySpan<byte> data = rawData.Span;
        Title = NdsBinary.ReadAscii(data, 0x00, 12);
        GameCode = NdsBinary.ReadAscii(data, 0x0C, 4);
        MakerCode = NdsBinary.ReadAscii(data, 0x10, 2);
        Kind = ParseKind(data[0x12]);
        EncryptionSeedSelect = data[0x13];
        DeviceCapacityExponent = data[0x14];
        DsiFlags = data[0x1C];
        RegionCode = data[0x1D];
        Version = data[0x1E];
        AutoStart = data[0x1F];
        Arm9 = ReadProgram(data, NdsProcessor.Arm9, 0x20);
        Arm7 = ReadProgram(data, NdsProcessor.Arm7, 0x30);
        FileNameTable = ReadRegion(data, 0x40);
        FileAllocationTable = ReadRegion(data, 0x48);
        Arm9OverlayTable = ReadRegion(data, 0x50);
        Arm7OverlayTable = ReadRegion(data, 0x58);
        NormalCardControl = NdsBinary.ReadUInt32(data, 0x60);
        SecureCardControl = NdsBinary.ReadUInt32(data, 0x64);
        BannerOffset = NdsBinary.ReadUInt32(data, 0x68);
        SecureAreaCrc = NdsBinary.ReadUInt16(data, 0x6C);
        SecureTransferTimeout = NdsBinary.ReadUInt16(data, 0x6E);
        Arm9AutoLoad = NdsBinary.ReadUInt32(data, 0x70);
        Arm7AutoLoad = NdsBinary.ReadUInt32(data, 0x74);
        SecureDisable = ((ulong)NdsBinary.ReadUInt32(data, 0x7C) << 32) | NdsBinary.ReadUInt32(data, 0x78);
        UsedImageSize = NdsBinary.ReadUInt32(data, 0x80);
        HeaderSize = NdsBinary.ReadUInt32(data, 0x84);
        NandRomEndUnits = NdsBinary.ReadUInt16(data, 0x94);
        NandWritableStartUnits = NdsBinary.ReadUInt16(data, 0x96);
        NintendoLogoCrc = NdsBinary.ReadUInt16(data, 0x15C);
        HeaderCrc = NdsBinary.ReadUInt16(data, 0x15E);
        DebugRomOffset = NdsBinary.ReadUInt32(data, 0x160);
        DebugRomSize = NdsBinary.ReadUInt32(data, 0x164);
        DebugLoadAddress = NdsBinary.ReadUInt32(data, 0x168);

        ProgramFeatures = (NdsProgramFeatures)data[0x1BF];
        bool digitalTitle = data.Length >= 0x238 && NdsCarrierLayoutParser.IsDigitalCategory(NdsBinary.ReadUInt32(data, 0x234));

        if (data.Length >= 0x1000 && Kind == NdsImageKind.NintendoDs && !digitalTitle &&
            (ProgramFeatures & (NdsProgramFeatures.AuthenticatesBanner | NdsProgramFeatures.AuthenticatesPrograms)) != 0)
        {
            DsExtended = new(rawData);
        }

        if (data.Length >= 0x1000 && (Kind != NdsImageKind.NintendoDs || digitalTitle))
        {
            Arm9i = ReadDsiProgram(data, NdsProcessor.Arm9i, 0x1C0);
            Arm7i = ReadDsiProgram(data, NdsProcessor.Arm7i, 0x1D0);
            Dsi = new(rawData);
        }
    }

    /// <summary>Preserves reserved and currently unmodeled fields for byte-exact extraction and copy-on-write editing.</summary>
    public ReadOnlyMemory<byte> RawData { get; }

    /// <summary>Decodes the padded 12-byte ASCII label shown by low-level cartridge inspection tools.</summary>
    public string Title { get; }

    /// <summary>Decodes the four-byte product code used for title identity, region suffixes, and encryption derivation.</summary>
    public string GameCode { get; }

    /// <summary>Decodes the two-byte publisher identifier; Nintendo-authored retail images commonly use <c>01</c>.</summary>
    public string MakerCode { get; }

    /// <summary>Interprets unit code <c>0</c>, <c>2</c>, or <c>3</c> as DS, DSi-enhanced, or DSi-exclusive.</summary>
    public NdsImageKind Kind { get; }

    /// <summary>Preserves the raw seed-selection byte consumed by cartridge secure-area protocols.</summary>
    public byte EncryptionSeedSelect { get; }

    /// <summary>Stores the power-of-two exponent that scales the 128 KiB base cartridge capacity.</summary>
    public byte DeviceCapacityExponent { get; }

    /// <summary>Computes the nominal power-of-two cartridge capacity independently from physical or used image length.</summary>
    /// <exception cref="InvalidOperationException">The raw exponent cannot be represented as a positive 64-bit length.</exception>
    public long DeviceCapacityBytes => NdsImageSizeInfo.DecodeDeviceCapacity(DeviceCapacityExponent)
        ?? throw new InvalidOperationException("The device-capacity exponent exceeds the positive 64-bit byte-length range.");

    /// <summary>Preserves the DSi flags at offset <c>0x1C</c>; DS-only software normally leaves them zero.</summary>
    public byte DsiFlags { get; }

    /// <summary>Projects defined DSi execution and modcrypt bits while <see cref="DsiFlags"/> retains the raw byte.</summary>
    public NdsDsiCryptoPolicy DsiCryptoPolicy => (NdsDsiCryptoPolicy)DsiFlags;

    /// <summary>Gets currently unassigned high bits from <see cref="DsiFlags"/>.</summary>
    public byte UnknownDsiFlagBits => (byte)(DsiFlags & 0xF0);

    /// <summary>Preserves the header's region byte, whose defined interpretation depends on DS versus DSi mode.</summary>
    public byte RegionCode { get; }

    /// <summary>Projects the original-DS region value; inspect <see cref="RegionCode"/> for undefined values.</summary>
    public NdsLegacyRegion LegacyRegion => new(RegionCode);

    /// <summary>Projects DSi launch-policy bits when <see cref="Kind"/> selects DSi execution.</summary>
    public NdsDsiLaunchPolicy DsiLaunchPolicy => (NdsDsiLaunchPolicy)RegionCode;

    /// <summary>Gets launch-byte bits not currently assigned by the DSi header format.</summary>
    public byte UnknownDsiLaunchBits => (byte)(RegionCode & 0xFC);

    /// <summary>Exposes the publisher-controlled one-byte software revision rather than the banner or format version.</summary>
    public byte Version { get; }

    /// <summary>Preserves the boot-control byte at offset <c>0x1F</c>, including reserved bits for lossless rewriting.</summary>
    public byte AutoStart { get; }

    /// <summary>Locates the primary processor payload and its entry/load addresses from header offsets <c>0x20</c>-<c>0x2F</c>.</summary>
    public NdsProgram Arm9 { get; }

    /// <summary>Locates the secondary processor payload and its entry/load addresses from header offsets <c>0x30</c>-<c>0x3F</c>.</summary>
    public NdsProgram Arm7 { get; }

    /// <summary>Locates the ARM9i payload in an extended DSi or digital-system header, including explicit empty tuples.</summary>
    public NdsProgram? Arm9i { get; }

    /// <summary>Locates the ARM7i payload in an extended DSi or digital-system header, including explicit empty tuples.</summary>
    public NdsProgram? Arm7i { get; }

    /// <summary>Gets DSi metadata, including declared DS-mode digital system titles; ordinary DS cartridges return null.</summary>
    public NdsDsiHeader? Dsi { get; }

    /// <summary>Gets the DSi-era authentication extension used by late DS software, or <see langword="null"/> when absent.</summary>
    public NdsDsExtendedHeader? DsExtended { get; }

    /// <summary>Interprets launcher and authentication capabilities stored in the common feature byte at <c>0x1BF</c>.</summary>
    public NdsProgramFeatures ProgramFeatures { get; }

    /// <summary>Locates the FNT main records and name subtables that give FAT identifiers a hierarchy and names.</summary>
    public NdsRegion FileNameTable { get; }

    /// <summary>Locates the flat array of eight-byte start/end records used by NitroFS files and overlays.</summary>
    public NdsRegion FileAllocationTable { get; }

    /// <summary>Locates fixed 32-byte records describing dynamically loaded ARM9 code and their FAT payload IDs.</summary>
    public NdsRegion Arm9OverlayTable { get; }

    /// <summary>Locates fixed 32-byte records describing dynamically loaded ARM7 code and their FAT payload IDs.</summary>
    public NdsRegion Arm7OverlayTable { get; }

    /// <summary>Preserves the raw ROM-control timing word used for ordinary cartridge transfers.</summary>
    public uint NormalCardControl { get; }

    /// <summary>Preserves the raw ROM-control timing word used during secure-area cartridge transfers.</summary>
    public uint SecureCardControl { get; }

    /// <summary>Gets the banner offset, or zero when no banner is present.</summary>
    public uint BannerOffset { get; }

    /// <summary>Contains the header's CRC16 for the secure area; interpretation requires secure-area encryption state.</summary>
    public ushort SecureAreaCrc { get; }

    /// <summary>Preserves the cartridge-transfer timeout value used while accessing the secure area.</summary>
    public ushort SecureTransferTimeout { get; }

    /// <summary>Identifies the ARM9 SDK autoload-list address used by the runtime initialization process.</summary>
    public uint Arm9AutoLoad { get; }

    /// <summary>Identifies the ARM7 SDK autoload-list address used by the runtime initialization process.</summary>
    public uint Arm7AutoLoad { get; }

    /// <summary>Combines the two header words forming the raw 64-bit secure-area disable token.</summary>
    public ulong SecureDisable { get; }

    /// <summary>Reports the builder-declared meaningful end of image, excluding optional capacity padding.</summary>
    public uint UsedImageSize { get; }

    /// <summary>Reports the header byte count claimed on cartridge, commonly <c>0x4000</c> despite a smaller parsed prefix.</summary>
    public uint HeaderSize { get; }

    /// <summary>Preserves the NAND ROM partition's exclusive end at 0x94, in 128 KiB DS or 512 KiB DSi units; zero is unspecified.</summary>
    public ushort NandRomEndUnits { get; }

    /// <summary>Preserves the NAND writable partition's start at 0x96, independently from its unknown length; zero is unspecified.</summary>
    public ushort NandWritableStartUnits { get; }

    /// <summary>Projects the NAND ROM boundary into a 64-bit cartridge address; this is not the required physical file length.</summary>
    public long NandRomEndOffset => NdsNandHeader.Decode(NandRomEndUnits, Kind);

    /// <summary>Projects the NAND writable boundary into a 64-bit cartridge address; zero does not establish absence of NAND hardware.</summary>
    public long NandWritableStartOffset => NdsNandHeader.Decode(NandWritableStartUnits, Kind);

    /// <summary>Contains the CRC16 protecting the 156-byte Nintendo logo at header offset <c>0xC0</c>.</summary>
    public ushort NintendoLogoCrc { get; }

    /// <summary>Contains the CRC16 over bytes <c>0x000</c>-<c>0x15D</c>, excluding this field itself.</summary>
    public ushort HeaderCrc { get; }

    /// <summary>Gets the absolute source offset of an optional debug program, or zero when absent.</summary>
    public uint DebugRomOffset { get; }

    /// <summary>Reports the stored byte length of the optional debug executable.</summary>
    public uint DebugRomSize { get; }

    /// <summary>Identifies the runtime address receiving the first optional debug executable byte.</summary>
    public uint DebugLoadAddress { get; }

    /// <summary>Combines the debug source offset and size into a half-open image region.</summary>
    public NdsRegion DebugRom => NdsRegion.FromUInt32(DebugRomOffset, DebugRomSize);

    /// <summary>Restricts unit codes to hardware modes whose header and program layouts this library understands.</summary>
    /// <param name="value">Raw unit code from header offset <c>0x12</c>.</param>
    /// <returns>The corresponding DS-family execution target.</returns>
    private static NdsImageKind ParseKind(byte value) => value switch
    {
        0 => NdsImageKind.NintendoDs,
        2 => NdsImageKind.NintendoDsiEnhanced,
        3 => NdsImageKind.NintendoDsiExclusive,
        _ => throw new InvalidDataException($"Unsupported Nintendo DS unit code 0x{value:X2}."),
    };

    /// <summary>Decodes the four-word offset, entry, load, and size tuple used by original DS programs.</summary>
    /// <param name="data">Common header bytes.</param>
    /// <param name="processor">ARM9 or ARM7 identity attached to the model.</param>
    /// <param name="offset">First tuple byte, normally <c>0x20</c> or <c>0x30</c>.</param>
    /// <returns>A program with independent entry and load addresses.</returns>
    private static NdsProgram ReadProgram(ReadOnlySpan<byte> data, NdsProcessor processor, int offset) =>
        new(
            processor,
            NdsRegion.FromUInt32(NdsBinary.ReadUInt32(data, offset), NdsBinary.ReadUInt32(data, offset + 12)),
            NdsBinary.ReadUInt32(data, offset + 4),
            NdsBinary.ReadUInt32(data, offset + 8));

    /// <summary>Decodes the DSi program tuple whose single RAM address serves as both load and entry address.</summary>
    /// <param name="data">Extended header bytes.</param>
    /// <param name="processor">ARM9i or ARM7i identity attached to the model.</param>
    /// <param name="offset">First tuple byte, normally <c>0x1C0</c> or <c>0x1D0</c>.</param>
    /// <returns>A DSi program whose entry and load addresses intentionally match.</returns>
    private static NdsProgram ReadDsiProgram(ReadOnlySpan<byte> data, NdsProcessor processor, int offset)
    {
        uint loadAddress = NdsBinary.ReadUInt32(data, offset + 8);
        return new(
            processor,
            NdsRegion.FromUInt32(NdsBinary.ReadUInt32(data, offset), NdsBinary.ReadUInt32(data, offset + 12)),
            loadAddress,
            loadAddress);
    }

    /// <summary>Decodes adjacent unsigned offset and length words into the library's 64-bit region model.</summary>
    /// <param name="data">Header containing the pair.</param>
    /// <param name="offset">Byte offset of the region's start word.</param>
    /// <returns>A half-open image interval; bounds are validated against the source separately.</returns>
    private static NdsRegion ReadRegion(ReadOnlySpan<byte> data, int offset) =>
        NdsRegion.FromUInt32(NdsBinary.ReadUInt32(data, offset), NdsBinary.ReadUInt32(data, offset + 4));
}
