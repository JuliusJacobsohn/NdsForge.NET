namespace NdsForge;

/// <summary>Models per-overlay Download Play authentication records embedded in the decoded ARM9 program.</summary>
public sealed class NdsOverlayAuthenticationTable
{
    /// <summary>Retains decoded bytes privately for key discovery, validation, and detached build import.</summary>
    private readonly ReadOnlyMemory<byte> _decodedProgram;

    /// <summary>Creates either a complete decoded table or a diagnostic structural placeholder.</summary>
    internal NdsOverlayAuthenticationTable(
        NdsOverlayAuthenticationTableState state,
        uint relativeOffset,
        int decodedProgramLength,
        NdsProgramStorageEncoding programStorage,
        int uncompressedPrefixLength,
        IReadOnlyList<NdsOverlayAuthenticationRecord> records,
        ReadOnlyMemory<byte> decodedProgram)
    {
        State = state;
        RelativeOffset = relativeOffset;
        DecodedProgramLength = decodedProgramLength;
        ProgramStorage = programStorage;
        UncompressedPrefixLength = uncompressedPrefixLength;
        Records = records;
        _decodedProgram = decodedProgram;
    }

    /// <summary>Reports whether the footer pointer and complete record interval were resolved.</summary>
    public NdsOverlayAuthenticationTableState State { get; }

    /// <summary>Gets the table start relative to decoded ARM9 byte zero, or zero when no pointer was present.</summary>
    public uint RelativeOffset { get; }

    /// <summary>Bounds relative table pointers against the complete runtime ARM9 representation.</summary>
    public int DecodedProgramLength { get; }

    /// <summary>Identifies whether the source ARM9 bytes were plain or bottom-up LZ encoded.</summary>
    public NdsProgramStorageEncoding ProgramStorage { get; }

    /// <summary>Gets the verbatim prefix retained by BLZ storage, or the complete decoded length for plain storage.</summary>
    public int UncompressedPrefixLength { get; }

    /// <summary>Gets one positional record per ARM9 overlay when <see cref="State"/> is complete.</summary>
    public IReadOnlyList<NdsOverlayAuthenticationRecord> Records { get; }

    /// <summary>Combines the decoded-relative pointer and fixed record widths into one half-open interval.</summary>
    public NdsRegion DecodedProgramRegion => new(RelativeOffset, checked(Records.Count * 20L));

    /// <summary>Exposes decoded bytes only to integrity and build stages that already validated the public state.</summary>
    internal ReadOnlyMemory<byte> DecodedProgram => _decodedProgram;
}
