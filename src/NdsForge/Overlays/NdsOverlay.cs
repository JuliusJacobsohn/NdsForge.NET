namespace NdsForge;

/// <summary>Describes an ARM overlay-table entry and its referenced FAT payload.</summary>
public sealed class NdsOverlay
{
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

    /// <summary>Gets the processor that owns the overlay table.</summary>
    public NdsProcessor Processor { get; }

    /// <summary>Gets the logical overlay ID.</summary>
    public uint Id { get; }

    /// <summary>Gets the runtime load address.</summary>
    public uint LoadAddress { get; }

    /// <summary>Gets the runtime initialized-data size.</summary>
    public uint RamSize { get; }

    /// <summary>Gets the zero-initialized BSS size.</summary>
    public uint BssSize { get; }

    /// <summary>Gets the first static-initializer address.</summary>
    public uint StaticInitializerStart { get; }

    /// <summary>Gets the first address after the static-initializer list.</summary>
    public uint StaticInitializerEnd { get; }

    /// <summary>Gets the referenced FAT file ID, which is independent of <see cref="Id"/>.</summary>
    public uint FileId { get; }

    /// <summary>Gets the compressed payload size stored in the low 24 bits.</summary>
    public uint CompressedSize { get; }

    /// <summary>Gets the overlay flags stored in the high eight bits.</summary>
    public byte Flags { get; }

    /// <summary>Gets the resolved payload region, or <see langword="null"/> for an invalid file ID.</summary>
    public NdsRegion? Data { get; }

    /// <summary>Gets the named NitroFS file when the referenced FAT entry also appears in the FNT.</summary>
    public NdsFile? File { get; }
}

