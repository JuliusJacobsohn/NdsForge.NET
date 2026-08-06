namespace NdsForge;

/// <summary>
/// Supplies one executable payload and the CPU addresses required to place it in a newly built image.
/// </summary>
/// <remarks>
/// The definition copies program bytes at construction, so later mutation of the caller's buffer cannot
/// make a previously reviewed Build Recipe nondeterministic. ROM offsets are deliberately absent: the
/// Layout assigns those after every component size and alignment policy is known.
/// </remarks>
public sealed class NdsProgramDefinition
{
    /// <summary>Captures exact executable bytes together with their runtime identity and addresses.</summary>
    /// <param name="processor">The execution target; DS builds currently require ARM9 and ARM7 definitions.</param>
    /// <param name="contents">Raw executable bytes as they should appear in the Image.</param>
    /// <param name="loadAddress">First CPU address populated when the payload is loaded.</param>
    /// <param name="entryAddress">CPU address at which execution begins.</param>
    public NdsProgramDefinition(
        NdsProcessor processor,
        ReadOnlySpan<byte> contents,
        uint loadAddress,
        uint entryAddress)
    {
        Processor = processor;
        Contents = contents.ToArray();
        LoadAddress = loadAddress;
        EntryAddress = entryAddress;
    }

    /// <summary>Distinguishes ARM9, ARM7, ARM9i, and ARM7i address spaces and header tuples.</summary>
    public NdsProcessor Processor { get; }

    /// <summary>Contains the exact cartridge payload in definition-owned, externally immutable memory.</summary>
    public ReadOnlyMemory<byte> Contents { get; }

    /// <summary>Specifies the first runtime address receiving payload bytes; it is not a ROM offset.</summary>
    public uint LoadAddress { get; }

    /// <summary>Specifies the first instruction address and must be meaningful for the selected processor mode.</summary>
    public uint EntryAddress { get; }
}
