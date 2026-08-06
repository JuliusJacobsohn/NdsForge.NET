namespace NdsForge;

/// <summary>Represents the typed fields in a Nintendo DS-family image header.</summary>
public sealed class NdsHeader
{
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
        NintendoLogoCrc = NdsBinary.ReadUInt16(data, 0x15C);
        HeaderCrc = NdsBinary.ReadUInt16(data, 0x15E);

        if (data.Length >= 0x1E0 && Kind != NdsImageKind.NintendoDs)
        {
            Arm9i = ReadDsiProgram(data, NdsProcessor.Arm9i, 0x1C0);
            Arm7i = ReadDsiProgram(data, NdsProcessor.Arm7i, 0x1D0);
            Dsi = new(rawData);
        }
    }

    /// <summary>Gets the unmodified bytes used to parse this header.</summary>
    public ReadOnlyMemory<byte> RawData { get; }

    /// <summary>Gets the application title.</summary>
    public string Title { get; }

    /// <summary>Gets the four-character game code.</summary>
    public string GameCode { get; }

    /// <summary>Gets the two-character maker code.</summary>
    public string MakerCode { get; }

    /// <summary>Gets the hardware family targeted by the image.</summary>
    public NdsImageKind Kind { get; }

    /// <summary>Gets the encryption seed selection value.</summary>
    public byte EncryptionSeedSelect { get; }

    /// <summary>Gets the device-capacity exponent encoded by the header.</summary>
    public byte DeviceCapacityExponent { get; }

    /// <summary>Gets the nominal device capacity in bytes.</summary>
    public long DeviceCapacityBytes => 128L * 1024L << DeviceCapacityExponent;

    /// <summary>Gets the DSi feature flags.</summary>
    public byte DsiFlags { get; }

    /// <summary>Gets the region code byte.</summary>
    public byte RegionCode { get; }

    /// <summary>Gets the application version.</summary>
    public byte Version { get; }

    /// <summary>Gets the autostart flag byte.</summary>
    public byte AutoStart { get; }

    /// <summary>Gets the ARM9 program.</summary>
    public NdsProgram Arm9 { get; }

    /// <summary>Gets the ARM7 program.</summary>
    public NdsProgram Arm7 { get; }

    /// <summary>Gets the optional DSi-mode ARM9 program.</summary>
    public NdsProgram? Arm9i { get; }

    /// <summary>Gets the optional DSi-mode ARM7 program.</summary>
    public NdsProgram? Arm7i { get; }

    /// <summary>Gets the extended DSi header, or <see langword="null"/> for DS-only images.</summary>
    public NdsDsiHeader? Dsi { get; }

    /// <summary>Gets the filename-table region.</summary>
    public NdsRegion FileNameTable { get; }

    /// <summary>Gets the file-allocation-table region.</summary>
    public NdsRegion FileAllocationTable { get; }

    /// <summary>Gets the ARM9 overlay-table region.</summary>
    public NdsRegion Arm9OverlayTable { get; }

    /// <summary>Gets the ARM7 overlay-table region.</summary>
    public NdsRegion Arm7OverlayTable { get; }

    /// <summary>Gets the normal card-control setting.</summary>
    public uint NormalCardControl { get; }

    /// <summary>Gets the secure card-control setting.</summary>
    public uint SecureCardControl { get; }

    /// <summary>Gets the banner offset, or zero when no banner is present.</summary>
    public uint BannerOffset { get; }

    /// <summary>Gets the stored secure-area checksum.</summary>
    public ushort SecureAreaCrc { get; }

    /// <summary>Gets the secure-area transfer timeout.</summary>
    public ushort SecureTransferTimeout { get; }

    /// <summary>Gets the ARM9 autoload address.</summary>
    public uint Arm9AutoLoad { get; }

    /// <summary>Gets the ARM7 autoload address.</summary>
    public uint Arm7AutoLoad { get; }

    /// <summary>Gets the secure-area disable value.</summary>
    public ulong SecureDisable { get; }

    /// <summary>Gets the used image size recorded in the header.</summary>
    public uint UsedImageSize { get; }

    /// <summary>Gets the declared header size.</summary>
    public uint HeaderSize { get; }

    /// <summary>Gets the stored Nintendo logo checksum.</summary>
    public ushort NintendoLogoCrc { get; }

    /// <summary>Gets the stored header checksum.</summary>
    public ushort HeaderCrc { get; }

    private static NdsImageKind ParseKind(byte value) => value switch
    {
        0 => NdsImageKind.NintendoDs,
        2 => NdsImageKind.NintendoDsiEnhanced,
        3 => NdsImageKind.NintendoDsiExclusive,
        _ => throw new InvalidDataException($"Unsupported Nintendo DS unit code 0x{value:X2}."),
    };

    private static NdsProgram ReadProgram(ReadOnlySpan<byte> data, NdsProcessor processor, int offset) =>
        new(
            processor,
            NdsRegion.FromUInt32(NdsBinary.ReadUInt32(data, offset), NdsBinary.ReadUInt32(data, offset + 12)),
            NdsBinary.ReadUInt32(data, offset + 4),
            NdsBinary.ReadUInt32(data, offset + 8));

    private static NdsProgram ReadDsiProgram(ReadOnlySpan<byte> data, NdsProcessor processor, int offset)
    {
        uint loadAddress = NdsBinary.ReadUInt32(data, offset + 8);
        return new(
            processor,
            NdsRegion.FromUInt32(NdsBinary.ReadUInt32(data, offset), NdsBinary.ReadUInt32(data, offset + 12)),
            loadAddress,
            loadAddress);
    }

    private static NdsRegion ReadRegion(ReadOnlySpan<byte> data, int offset) =>
        NdsRegion.FromUInt32(NdsBinary.ReadUInt32(data, offset), NdsBinary.ReadUInt32(data, offset + 4));
}
