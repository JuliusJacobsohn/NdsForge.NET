namespace NdsForge;

/// <summary>Holds the validated ELF entrypoint and immutable program-header projections used by assemblers.</summary>
/// <param name="EntryPoint">Virtual entrypoint from the ELF file header.</param>
/// <param name="Segments">All decoded program headers in original table order.</param>
internal sealed record NdsElfFile(uint EntryPoint, IReadOnlyList<NdsElfSegment> Segments);

/// <summary>Projects the eight fixed ELF32 program-header words without exposing an incomplete general ELF model.</summary>
/// <param name="Type">Segment type; value one denotes loadable content.</param>
/// <param name="FileOffset">Absolute offset of file-backed bytes.</param>
/// <param name="VirtualAddress">Address used by link-time references and entrypoint mapping.</param>
/// <param name="PhysicalAddress">Runtime load address represented in the cartridge payload.</param>
/// <param name="FileSize">Bytes physically present in the ELF.</param>
/// <param name="MemorySize">Initialized plus zero-filled runtime extent.</param>
/// <param name="Flags">Standard access flags plus ndstool toolchain classification bits.</param>
/// <param name="Alignment">Declared segment congruence, retained for validation.</param>
internal sealed record NdsElfSegment(
    uint Type,
    uint FileOffset,
    uint VirtualAddress,
    uint PhysicalAddress,
    uint FileSize,
    uint MemorySize,
    uint Flags,
    uint Alignment)
{
    /// <summary>Identifies ELF <c>PT_LOAD</c> content eligible for a cartridge executable.</summary>
    public bool IsLoadable => Type == 1;

    /// <summary>Identifies the devkitPro program-header extension used for Overlay tables and payloads.</summary>
    public bool IsOverlay => (Flags & 0x0020_0000) != 0;

    /// <summary>Identifies DSi-mode rather than original DS program content.</summary>
    public bool IsTwl => (Flags & 0x0010_0000) != 0;
}
