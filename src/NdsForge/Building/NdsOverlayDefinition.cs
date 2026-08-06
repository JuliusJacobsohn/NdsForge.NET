namespace NdsForge;

/// <summary>Describes one Overlay record and its private Allocation when constructing a new Image.</summary>
/// <remarks>
/// Overlay ID is runtime metadata, whereas the writer assigns a separate File ID after named NitroFS files.
/// Keeping those identities distinct prevents the historical extraction error of indexing the FAT by Overlay ID.
/// </remarks>
public sealed class NdsOverlayDefinition
{
    /// <summary>Copies an Overlay payload and captures every field required by its fixed 32-byte table record.</summary>
    /// <param name="processor">ARM9 or ARM7 table receiving the record.</param>
    /// <param name="id">Runtime Overlay identity, which need not be contiguous or equal its eventual File ID.</param>
    /// <param name="contents">Stored payload bytes, compressed or plain according to <paramref name="flags"/>.</param>
    /// <param name="loadAddress">First runtime address populated by the Overlay loader.</param>
    /// <param name="ramSize">Initialized runtime size after any decompression.</param>
    /// <param name="bssSize">Additional zero-initialized bytes absent from the stored payload.</param>
    /// <param name="staticInitializerStart">Inclusive runtime start of the constructor pointer list.</param>
    /// <param name="staticInitializerEnd">Exclusive runtime end of the constructor pointer list.</param>
    /// <param name="compressedSize">Low 24 bits of the packed control word; zero commonly means uncompressed.</param>
    /// <param name="flags">High eight bits of the packed control word.</param>
    public NdsOverlayDefinition(
        NdsProcessor processor,
        uint id,
        ReadOnlySpan<byte> contents,
        uint loadAddress,
        uint ramSize,
        uint bssSize = 0,
        uint staticInitializerStart = 0,
        uint staticInitializerEnd = 0,
        uint compressedSize = 0,
        byte flags = 0)
    {
        if (processor is not NdsProcessor.Arm9 and not NdsProcessor.Arm7)
        {
            throw new ArgumentOutOfRangeException(nameof(processor), "DS overlay tables belong to ARM9 or ARM7.");
        }

        if (staticInitializerEnd < staticInitializerStart || compressedSize > 0x00FF_FFFF)
        {
            throw new ArgumentException("Overlay initializer bounds or compressed size are invalid.");
        }

        Processor = processor;
        Id = id;
        Contents = contents.ToArray();
        LoadAddress = loadAddress;
        RamSize = ramSize;
        BssSize = bssSize;
        StaticInitializerStart = staticInitializerStart;
        StaticInitializerEnd = staticInitializerEnd;
        CompressedSize = compressedSize;
        Flags = flags;
    }

    /// <summary>Selects the independent ARM9 or ARM7 Overlay table and execution environment.</summary>
    public NdsProcessor Processor { get; }

    /// <summary>Identifies the Overlay to runtime code without determining its FAT File ID.</summary>
    public uint Id { get; }

    /// <summary>Contains definition-owned stored bytes placed in an unnamed FAT Allocation.</summary>
    public ReadOnlyMemory<byte> Contents { get; }

    /// <summary>Specifies the first runtime address receiving initialized Overlay bytes.</summary>
    public uint LoadAddress { get; }

    /// <summary>Declares initialized runtime bytes after optional decompression.</summary>
    public uint RamSize { get; }

    /// <summary>Declares zero-filled runtime bytes that do not occupy cartridge storage.</summary>
    public uint BssSize { get; }

    /// <summary>Marks the inclusive start of the runtime constructor pointer list.</summary>
    public uint StaticInitializerStart { get; }

    /// <summary>Marks the exclusive end of the runtime constructor pointer list.</summary>
    public uint StaticInitializerEnd { get; }

    /// <summary>Occupies the low 24 bits of the packed table control word.</summary>
    public uint CompressedSize { get; }

    /// <summary>Occupies the high byte of the packed table control word without reinterpretation.</summary>
    public byte Flags { get; }
}
