namespace NdsForge;

/// <summary>Validates the ELF32 ARM envelope and bounded program-header table before any payload assembly begins.</summary>
internal static class NdsElfParser
{
    /// <summary>Minimum ELF32 file-header width defined by the generic ABI.</summary>
    private const int HeaderSize = 52;
    /// <summary>Exact generic ELF32 program-header width used by ARM toolchains.</summary>
    private const int ProgramHeaderSize = 32;

    /// <summary>Decodes a complete bounded input and rejects unsupported class, byte order, type, machine, or table layout.</summary>
    /// <param name="data">Complete ELF bytes retained for later bounded segment copies.</param>
    /// <param name="options">Validated resource limits.</param>
    /// <returns>A complete header projection; no payload bytes are copied yet.</returns>
    public static NdsElfFile Parse(ReadOnlyMemory<byte> data, NdsElfImportOptions options)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        if (bytes.Length < HeaderSize)
        {
            throw new InvalidDataException("The ELF file is shorter than its 52-byte ELF32 header.");
        }

        if (bytes[0] != 0x7F || !bytes.Slice(1, 3).SequenceEqual("ELF"u8))
        {
            throw new InvalidDataException("The input does not begin with the ELF magic number.");
        }

        if (bytes[4] != 1 || bytes[5] != 1 || bytes[6] != 1)
        {
            throw new InvalidDataException("NDS ingestion requires ELF32, little-endian encoding, and identification version one.");
        }

        if (NdsBinary.ReadUInt16(bytes, 16) != 2 || NdsBinary.ReadUInt16(bytes, 18) != 40 ||
            NdsBinary.ReadUInt32(bytes, 20) != 1 || NdsBinary.ReadUInt16(bytes, 40) != HeaderSize)
        {
            throw new InvalidDataException("NDS ingestion requires an executable ARM ELF with a canonical ELF32 header.");
        }

        uint tableOffset = NdsBinary.ReadUInt32(bytes, 28);
        ushort entrySize = NdsBinary.ReadUInt16(bytes, 42);
        ushort count = NdsBinary.ReadUInt16(bytes, 44);
        if (count == 0 || count > options.MaxProgramHeaders || entrySize != ProgramHeaderSize)
        {
            throw new InvalidDataException("The ELF program-header count or entry width is unsupported.");
        }

        long tableLength = checked((long)count * ProgramHeaderSize);
        if (tableOffset > bytes.Length || tableLength > bytes.Length - tableOffset)
        {
            throw new InvalidDataException("The ELF program-header table lies outside the input.");
        }

        var segments = new NdsElfSegment[count];
        for (int index = 0; index < count; index++)
        {
            int offset = checked((int)tableOffset + (index * ProgramHeaderSize));
            ReadOnlySpan<byte> item = bytes.Slice(offset, ProgramHeaderSize);
            var segment = new NdsElfSegment(
                NdsBinary.ReadUInt32(item, 0),
                NdsBinary.ReadUInt32(item, 4),
                NdsBinary.ReadUInt32(item, 8),
                NdsBinary.ReadUInt32(item, 12),
                NdsBinary.ReadUInt32(item, 16),
                NdsBinary.ReadUInt32(item, 20),
                NdsBinary.ReadUInt32(item, 24),
                NdsBinary.ReadUInt32(item, 28));
            ValidateSegment(segment, bytes.Length, index);
            segments[index] = segment;
        }

        return new(NdsBinary.ReadUInt32(bytes, 24), segments);
    }

    /// <summary>Checks file bounds, memory size, and ELF power-of-two alignment without interpreting custom flags.</summary>
    /// <param name="segment">Decoded program header.</param>
    /// <param name="fileLength">Physical input boundary.</param>
    /// <param name="index">Table index included in actionable format errors.</param>
    private static void ValidateSegment(NdsElfSegment segment, int fileLength, int index)
    {
        if (segment.IsLoadable && segment.MemorySize < segment.FileSize)
        {
            throw new InvalidDataException($"ELF program header {index} has a memory size smaller than its file image.");
        }

        if (segment.FileOffset > fileLength || segment.FileSize > fileLength - segment.FileOffset)
        {
            throw new InvalidDataException($"ELF program header {index} references bytes outside the input.");
        }

        if (segment.Alignment != 0 && (segment.Alignment & (segment.Alignment - 1)) != 0)
        {
            throw new InvalidDataException($"ELF program header {index} has a non-power-of-two alignment.");
        }

        if (segment.IsLoadable && segment.Alignment > 1 &&
            segment.VirtualAddress % segment.Alignment != segment.FileOffset % segment.Alignment)
        {
            throw new InvalidDataException($"ELF loadable program header {index} has incongruent file and virtual alignment.");
        }

        if (segment.IsLoadable &&
            ((ulong)segment.PhysicalAddress + segment.MemorySize > 0x1_0000_0000UL ||
             (ulong)segment.VirtualAddress + segment.MemorySize > 0x1_0000_0000UL))
        {
            throw new InvalidDataException($"ELF loadable program header {index} wraps the 32-bit address space.");
        }
    }
}
