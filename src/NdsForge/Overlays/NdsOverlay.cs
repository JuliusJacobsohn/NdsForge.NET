namespace NdsForge;

/// <summary>Describes an ARM overlay-table entry and its referenced FAT payload.</summary>
public sealed class NdsOverlay
{
    /// <summary>Decodes one 32-byte table record and resolves its FAT identifier without confusing it with the overlay ID.</summary>
    /// <param name="processor">Processor whose separate overlay table contained the record.</param>
    /// <param name="data">Exactly one little-endian 32-byte overlay record.</param>
    /// <param name="fileSystem">FAT allocations and optional FNT names used to resolve the payload.</param>
    internal NdsOverlay(NdsProcessor processor, ReadOnlySpan<byte> data, NdsFileSystem fileSystem)
    {
        Processor = processor;
        Id = NdsBinary.ReadUInt32(data, 0x00);
        LoadAddress = NdsBinary.ReadUInt32(data, 0x04);
        RamSize = NdsBinary.ReadUInt32(data, 0x08);
        BssSize = NdsBinary.ReadUInt32(data, 0x0C);
        StaticInitializerStart = NdsBinary.ReadUInt32(data, 0x10);
        StaticInitializerEnd = NdsBinary.ReadUInt32(data, 0x14);
        FileId = NdsBinary.ReadUInt32(data, 0x18);
        uint flagsAndSize = NdsBinary.ReadUInt32(data, 0x1C);
        CompressedSize = flagsAndSize & 0x00FF_FFFF;
        Flags = (byte)(flagsAndSize >> 24);

        if (FileId < fileSystem.Allocations.Count)
        {
            Data = fileSystem.Allocations[checked((int)FileId)].Data;
            fileSystem.TryGetFile(checked((int)FileId), out NdsFile? namedFile);
            File = namedFile;
        }
    }

    /// <summary>Distinguishes the independent ARM9 and ARM7 overlay namespaces and load environments.</summary>
    public NdsProcessor Processor { get; }

    /// <summary>Identifies runtime overlay metadata and is not required to equal the payload's <see cref="FileId"/>.</summary>
    public uint Id { get; }

    /// <summary>Specifies the ARM memory address at which the overlay loader places initialized payload bytes.</summary>
    public uint LoadAddress { get; }

    /// <summary>Declares initialized runtime bytes after decompression, so it may exceed the stored payload length.</summary>
    public uint RamSize { get; }

    /// <summary>Reserves additional zero-filled runtime memory that is absent from the cartridge payload.</summary>
    public uint BssSize { get; }

    /// <summary>Marks the inclusive start of the runtime constructor pointer list for this overlay.</summary>
    public uint StaticInitializerStart { get; }

    /// <summary>Marks the exclusive end of the constructor pointer list and should not precede its start.</summary>
    public uint StaticInitializerEnd { get; }

    /// <summary>Gets the referenced FAT file ID, which is independent of <see cref="Id"/>.</summary>
    public uint FileId { get; }

    /// <summary>Decodes the low 24 bits of the packed control word; zero commonly indicates an uncompressed payload.</summary>
    public uint CompressedSize { get; }

    /// <summary>Preserves the packed control word's high byte, including compression and authentication-related bits.</summary>
    public byte Flags { get; }

    /// <summary>Reports whether bit zero of the control byte declares BLZ-compressed stored data.</summary>
    public bool IsCompressed => (Flags & 0x01) != 0;

    /// <summary>Reports whether bit one declares an entry in the ARM9 Download Play authentication table.</summary>
    public bool IsAuthenticated => (Flags & 0x02) != 0;

    /// <summary>Preserves currently undefined control bits independently from the two standardized flags.</summary>
    public byte ReservedFlags => (byte)(Flags & 0xFC);

    /// <summary>Gets the resolved payload region, or <see langword="null"/> for an invalid file ID.</summary>
    public NdsRegion? Data { get; }

    /// <summary>Links to a navigable FNT entry only when the FAT payload is also named; valid overlays may remain unnamed.</summary>
    public NdsFile? File { get; }
}
