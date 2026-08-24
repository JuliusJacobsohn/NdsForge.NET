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
        : this(
            processor,
            id,
            contents.ToArray(),
            linkedFilePath: null,
            linkedFile: null,
            loadAddress,
            ramSize,
            bssSize,
            staticInitializerStart,
            staticInitializerEnd,
            compressedSize,
            flags)
    {
    }

    /// <summary>Links an Overlay record to a named NitroFS payload so both identities share one FAT Allocation.</summary>
    /// <param name="processor">ARM9 or ARM7 table receiving the record.</param>
    /// <param name="id">Runtime Overlay identity, independent from the linked file's generated File ID.</param>
    /// <param name="filePath">Canonical or root-relative NitroFS path that must exist when the recipe is built.</param>
    /// <param name="loadAddress">First runtime address populated by the Overlay loader.</param>
    /// <param name="ramSize">Initialized runtime size after any decompression.</param>
    /// <param name="bssSize">Additional zero-initialized bytes absent from the stored payload.</param>
    /// <param name="staticInitializerStart">Inclusive runtime start of the constructor pointer list.</param>
    /// <param name="staticInitializerEnd">Exclusive runtime end of the constructor pointer list.</param>
    /// <param name="compressedSize">Low 24 bits of the packed control word.</param>
    /// <param name="flags">High eight bits of the packed control word.</param>
    /// <returns>An immutable record definition that allocates no duplicate payload.</returns>
    public static NdsOverlayDefinition LinkToFile(
        NdsProcessor processor,
        uint id,
        string filePath,
        uint loadAddress,
        uint ramSize,
        uint bssSize = 0,
        uint staticInitializerStart = 0,
        uint staticInitializerEnd = 0,
        uint compressedSize = 0,
        byte flags = 0) => new(
            processor,
            id,
            contents: null,
            NdsFileSystemBuilder.NormalizePath(filePath, allowRoot: false),
            linkedFile: null,
            loadAddress,
            ramSize,
            bssSize,
            staticInitializerStart,
            staticInitializerEnd,
            compressedSize,
            flags);

    /// <summary>Links to a builder-owned file object so directory or file moves automatically retain the Overlay relationship.</summary>
    /// <param name="processor">ARM9 or ARM7 table receiving the record.</param>
    /// <param name="id">Runtime Overlay identity, independent from generated File ID.</param>
    /// <param name="file">Payload owned by the same filesystem builder used in the final recipe.</param>
    /// <param name="loadAddress">First runtime address populated by the Overlay loader.</param>
    /// <param name="ramSize">Initialized runtime size after any decompression.</param>
    /// <param name="bssSize">Additional zero-initialized bytes absent from storage.</param>
    /// <param name="staticInitializerStart">Inclusive constructor-list start.</param>
    /// <param name="staticInitializerEnd">Exclusive constructor-list end.</param>
    /// <param name="compressedSize">Low 24 bits of the packed control word.</param>
    /// <param name="flags">High eight bits of the packed control word.</param>
    /// <returns>An immutable definition whose effective path follows <paramref name="file"/>.</returns>
    public static NdsOverlayDefinition LinkToFile(
        NdsProcessor processor,
        uint id,
        NdsBuildFile file,
        uint loadAddress,
        uint ramSize,
        uint bssSize = 0,
        uint staticInitializerStart = 0,
        uint staticInitializerEnd = 0,
        uint compressedSize = 0,
        byte flags = 0) => new(
            processor,
            id,
            contents: null,
            linkedFilePath: null,
            file ?? throw new ArgumentNullException(nameof(file)),
            loadAddress,
            ramSize,
            bssSize,
            staticInitializerStart,
            staticInitializerEnd,
            compressedSize,
            flags);

    /// <summary>Initializes shared metadata after selecting exactly one private or linked payload source.</summary>
    /// <param name="processor">Validated table ownership.</param>
    /// <param name="id">Runtime Overlay identity.</param>
    /// <param name="contents">Private payload copy, or <see langword="null"/> for a named link.</param>
    /// <param name="linkedFilePath">Canonical named-file path, or <see langword="null"/> for a private Allocation.</param>
    /// <param name="linkedFile">Builder-owned payload whose mutable path supersedes fixed path text.</param>
    /// <param name="loadAddress">Runtime payload start.</param>
    /// <param name="ramSize">Initialized runtime byte count.</param>
    /// <param name="bssSize">Zero-filled runtime byte count.</param>
    /// <param name="staticInitializerStart">Inclusive constructor-list start.</param>
    /// <param name="staticInitializerEnd">Exclusive constructor-list end.</param>
    /// <param name="compressedSize">Packed low 24-bit stored size.</param>
    /// <param name="flags">Packed high control byte.</param>
    private NdsOverlayDefinition(
        NdsProcessor processor,
        uint id,
        byte[]? contents,
        string? linkedFilePath,
        NdsBuildFile? linkedFile,
        uint loadAddress,
        uint ramSize,
        uint bssSize,
        uint staticInitializerStart,
        uint staticInitializerEnd,
        uint compressedSize,
        byte flags)
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
        Contents = contents ?? [];
        LinkedFilePath = linkedFilePath;
        LinkedFile = linkedFile;
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

    /// <summary>Contains definition-owned stored bytes for a private Allocation, or empty memory for a named-file link.</summary>
    public ReadOnlyMemory<byte> Contents { get; }

    /// <summary>Identifies a shared named NitroFS payload, or remains <see langword="null"/> for a private Allocation.</summary>
    public string? LinkedFilePath { get; }

    /// <summary>Retains builder-owned payload identity so structural moves do not break the Overlay link.</summary>
    internal NdsBuildFile? LinkedFile { get; }

    /// <summary>Resolves the current object path when available, otherwise the fixed link supplied by path.</summary>
    internal string? EffectiveLinkedFilePath => LinkedFile?.Path ?? LinkedFilePath;

    /// <summary>Distinguishes payloads that add a FAT record from records that reuse an existing named File ID.</summary>
    internal bool HasPrivateAllocation => EffectiveLinkedFilePath is null;

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

    /// <summary>Reports whether the output table marks the payload as BLZ-compressed.</summary>
    public bool IsCompressed => (Flags & 0x01) != 0;

    /// <summary>Reports whether the output table marks the overlay for Download Play authentication.</summary>
    public bool IsAuthenticated => (Flags & 0x02) != 0;
}
