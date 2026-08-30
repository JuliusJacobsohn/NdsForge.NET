namespace NdsForge;

/// <summary>Snapshots common header identity, execution policy, and size claims in serialization-stable scalar fields.</summary>
public sealed class NdsManifestHeader
{
    /// <summary>Preserves the visible fixed-width cartridge title after format padding is removed.</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>Preserves the exact four-character product identity used by tools and cryptographic derivations.</summary>
    public string GameCode { get; init; } = string.Empty;
    /// <summary>Preserves the two-character publisher identity without resolving an external company database.</summary>
    public string MakerCode { get; init; } = string.Empty;
    /// <summary>Records the DS, DSi-enhanced, or DSi-exclusive unit-code interpretation.</summary>
    public NdsImageKind Kind { get; init; }
    /// <summary>Records the publisher-controlled software revision byte rather than any structure version.</summary>
    public byte Version { get; init; }
    /// <summary>Records the raw region byte whose interpretation depends on execution mode.</summary>
    public byte RegionCode { get; init; }
    /// <summary>Records the complete DSi execution and modcrypt policy byte, including unassigned bits.</summary>
    public byte DsiFlags { get; init; }
    /// <summary>Records the complete boot-control byte, including reserved bits retained by the parser.</summary>
    public byte AutoStart { get; init; }
    /// <summary>Separates the meaningful image extent claimed by the header from physical padding.</summary>
    public uint UsedImageSize { get; init; }
    /// <summary>Records the raw capacity byte without losing unrepresentable declarations.</summary>
    public byte DeviceCapacityExponent { get; init; }
    /// <summary>Records the represented power-of-two capacity, or zero when it is not a positive 64-bit byte length.</summary>
    public long DeviceCapacityBytes { get; init; }
    /// <summary>Records raw NAND ROM-end units, or null for older manifests that did not project NAND fields.</summary>
    public ushort? NandRomEndUnits { get; init; }
    /// <summary>Records raw NAND writable-start units, or null together with the ROM-end projection in older manifests.</summary>
    public ushort? NandWritableStartUnits { get; init; }
    /// <summary>Records ordinary cartridge bus timing and control bits as an uninterpreted hardware word.</summary>
    public uint NormalCardControl { get; init; }
    /// <summary>Records secure-transfer cartridge bus timing and control bits as an uninterpreted hardware word.</summary>
    public uint SecureCardControl { get; init; }
    /// <summary>Records the optional debug program's absolute source offset.</summary>
    public uint DebugRomOffset { get; init; }
    /// <summary>Records the optional debug program's header-declared byte length.</summary>
    public uint DebugRomSize { get; init; }
    /// <summary>Records the optional debug program's runtime load address.</summary>
    public uint DebugLoadAddress { get; init; }
    /// <summary>Hashes optional debug executable bytes, or remains absent when no region is declared.</summary>
    public string? DebugRomSha256 { get; init; }
    /// <summary>Hashes every parsed common or extended header byte, including reserved fields, with SHA-256.</summary>
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>Snapshots one executable's physical placement, runtime mapping, and content identity.</summary>
public sealed class NdsManifestProgram
{
    /// <summary>Separates original and DSi execution modes as well as ARM9 and ARM7 address spaces.</summary>
    public NdsProcessor Processor { get; init; }
    /// <summary>Records the first cartridge byte of the header-declared executable.</summary>
    public long Offset { get; init; }
    /// <summary>Records header-declared executable bytes and intentionally excludes an optional SDK footer.</summary>
    public long Length { get; init; }
    /// <summary>Records the CPU address populated from the first executable byte.</summary>
    public uint LoadAddress { get; init; }
    /// <summary>Records the initial instruction address after any ELF virtual-to-physical translation.</summary>
    public uint EntryAddress { get; init; }
    /// <summary>Hashes only the header-declared executable region with SHA-256.</summary>
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>Snapshots one named NitroFS allocation so path, numeric identity, layout, and payload can change independently.</summary>
public sealed class NdsManifestFile
{
    /// <summary>Records the canonical case-sensitive slash-delimited NitroFS identity.</summary>
    public string Path { get; init; } = string.Empty;
    /// <summary>Records the FAT identifier referenced by FNT order and possibly Overlay metadata.</summary>
    public int FileId { get; init; }
    /// <summary>Records the first physical payload byte independently from path and File ID.</summary>
    public long Offset { get; init; }
    /// <summary>Records stored payload bytes without inferring compression or game-specific structure.</summary>
    public long Length { get; init; }
    /// <summary>Hashes the exact FAT allocation bytes with SHA-256.</summary>
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>Snapshots every FAT record, including unnamed private Overlay payloads and otherwise unreferenced allocations.</summary>
public sealed class NdsManifestAllocation
{
    /// <summary>Records the zero-based FAT slot independent from Overlay IDs and optional FNT names.</summary>
    public int FileId { get; init; }
    /// <summary>Records the first physical byte addressed by the FAT record.</summary>
    public long Offset { get; init; }
    /// <summary>Records the FAT end-minus-start length, including valid zero-byte allocations.</summary>
    public long Length { get; init; }
    /// <summary>Hashes the exact allocation bytes with SHA-256 so unnamed payload changes remain visible.</summary>
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>Snapshots one Overlay record and its resolved payload identity without conflating Overlay and File IDs.</summary>
public sealed class NdsManifestOverlay
{
    /// <summary>Separates the independent ARM9 and ARM7 Overlay namespaces.</summary>
    public NdsProcessor Processor { get; init; }
    /// <summary>Records runtime Overlay identity rather than table position or FAT identity.</summary>
    public uint OverlayId { get; init; }
    /// <summary>Records the referenced FAT allocation, which may be unnamed.</summary>
    public uint FileId { get; init; }
    /// <summary>Records a resolved NitroFS path only when the allocation also has an FNT name.</summary>
    public string? FilePath { get; init; }
    /// <summary>Records the first payload byte, or remains absent for an invalid FAT reference.</summary>
    public long? Offset { get; init; }
    /// <summary>Records stored payload length, or remains absent for an invalid FAT reference.</summary>
    public long? Length { get; init; }
    /// <summary>Records the first runtime address populated by the Overlay loader.</summary>
    public uint LoadAddress { get; init; }
    /// <summary>Records initialized runtime bytes after any decompression.</summary>
    public uint RamSize { get; init; }
    /// <summary>Records additional zero-filled bytes absent from the cartridge payload.</summary>
    public uint BssSize { get; init; }
    /// <summary>Records the inclusive constructor-pointer-list start.</summary>
    public uint StaticInitializerStart { get; init; }
    /// <summary>Records the exclusive constructor-pointer-list end.</summary>
    public uint StaticInitializerEnd { get; init; }
    /// <summary>Records the packed control word's low 24-bit stored-size field.</summary>
    public uint CompressedSize { get; init; }
    /// <summary>Records the packed control word's high byte without inventing unsupported flag meanings.</summary>
    public byte Flags { get; init; }
    /// <summary>Hashes resolved payload bytes, or remains absent when the File ID cannot be resolved.</summary>
    public string? Sha256 { get; init; }
}
