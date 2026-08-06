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
    /// <summary>Stores an optional copied SDK footer separately so it never inflates the header-declared Program size.</summary>
    private ReadOnlyMemory<byte> _footer;

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

    /// <summary>Contains the optional 12-byte ARM9 SDK footer beginning with marker <c>0xDEC00621</c>.</summary>
    public ReadOnlyMemory<byte> Footer => _footer;

    /// <summary>Attaches a recognized SDK footer immediately after ARM9 while retaining its separate structural identity.</summary>
    /// <param name="footer">Exactly 12 bytes whose first little-endian word is <c>0xDEC00621</c>.</param>
    /// <returns>The same definition for fluent Build Recipe construction.</returns>
    /// <exception cref="InvalidDataException">The processor is not ARM9 or the bytes do not match the recognized footer form.</exception>
    public NdsProgramDefinition SetFooter(ReadOnlySpan<byte> footer)
    {
        if (Processor != NdsProcessor.Arm9 || footer.Length != 12 || NdsBinary.ReadUInt32(footer, 0) != 0xDEC00621)
        {
            throw new InvalidDataException("An SDK footer must be a 12-byte ARM9 footer beginning with 0xDEC00621.");
        }

        _footer = footer.ToArray();
        return this;
    }
}
