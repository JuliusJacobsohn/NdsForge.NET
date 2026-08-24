namespace NdsForge;

/// <summary>Describes Nintendo SDK initialization and compression metadata embedded in a processor program.</summary>
public sealed class NdsProgramParameters
{
    /// <summary>Decodes the fixed parameter prefix only after its complete interval has been proven to lie inside the program.</summary>
    internal NdsProgramParameters(NdsRegion data, uint relativeOffset, uint loadAddress, ReadOnlySpan<byte> bytes)
    {
        Data = data;
        RelativeOffset = relativeOffset;
        TcmBlockInfoStartAddress = NdsBinary.ReadUInt32(bytes, 0x00);
        TcmBlockInfoEndAddress = NdsBinary.ReadUInt32(bytes, 0x04);
        TcmInputDataAddress = NdsBinary.ReadUInt32(bytes, 0x08);
        BssStartAddress = NdsBinary.ReadUInt32(bytes, 0x0C);
        BssEndAddress = NdsBinary.ReadUInt32(bytes, 0x10);
        CompressedEndAddress = NdsBinary.ReadUInt32(bytes, 0x14);
        SdkVersion = NdsSdkVersion.FromPacked(NdsBinary.ReadUInt32(bytes, 0x18));
        LittleEndianMarker = NdsBinary.ReadUInt32(bytes, 0x1C);
        BigEndianMarker = NdsBinary.ReadUInt32(bytes, 0x20);
        CompressedLength = CompressedEndAddress == 0 || CompressedEndAddress < loadAddress
            ? null
            : CompressedEndAddress - loadAddress;
    }

    /// <summary>Locates the fixed 36-byte parameter prefix in the source image.</summary>
    public NdsRegion Data { get; }

    /// <summary>Locates the parameter prefix relative to the beginning of its program payload.</summary>
    public uint RelativeOffset { get; }

    /// <summary>Locates the first runtime descriptor used to copy initialized bytes into tightly coupled memory.</summary>
    public uint TcmBlockInfoStartAddress { get; }

    /// <summary>Locates the exclusive end of the runtime TCM descriptor array so its entry count can be validated.</summary>
    public uint TcmBlockInfoEndAddress { get; }

    /// <summary>Locates the consecutive runtime source bytes consumed by the TCM relocation descriptors.</summary>
    public uint TcmInputDataAddress { get; }

    /// <summary>Gets the first runtime byte of the program's zero-initialized BSS range.</summary>
    public uint BssStartAddress { get; }

    /// <summary>Locates the exclusive runtime end of bytes cleared before transferring control to the program.</summary>
    public uint BssEndAddress { get; }

    /// <summary>Gets the runtime end address of compressed stored ARM9 data, or zero for an uncompressed program.</summary>
    public uint CompressedEndAddress { get; }

    /// <summary>Gets the stored compressed length derived from the load address, or <see langword="null"/> for absent or reversed metadata.</summary>
    public uint? CompressedLength { get; }

    /// <summary>Reports whether a nonzero compressed-end address declares a compressed stored program.</summary>
    public bool IsCompressed => CompressedEndAddress != 0;

    /// <summary>Identifies the Nintendo SDK generation whose layout and padding conventions produced the program.</summary>
    public NdsSdkVersion SdkVersion { get; }

    /// <summary>Preserves the expected little-endian SDK structure marker for validation.</summary>
    public uint LittleEndianMarker { get; }

    /// <summary>Preserves the expected byte-reversed SDK structure marker for validation.</summary>
    public uint BigEndianMarker { get; }
}
