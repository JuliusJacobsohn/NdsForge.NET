namespace NdsForge;

/// <summary>Describes an executable program region and its runtime addresses.</summary>
/// <param name="Processor">The processor and execution mode.</param>
/// <param name="Data">The program bytes in the image.</param>
/// <param name="EntryAddress">The initial instruction address.</param>
/// <param name="LoadAddress">The address at which the program is loaded.</param>
public sealed record NdsProgram(
    NdsProcessor Processor,
    NdsRegion Data,
    uint EntryAddress,
    uint LoadAddress)
{
    /// <summary>Identifies the optional 12-byte SDK footer recognized by its <c>0xDEC00621</c> marker after ARM9.</summary>
    public NdsRegion? Footer { get; internal set; }

    /// <summary>Extends the executable region through a recognized SDK footer for byte-compatible extraction.</summary>
    public NdsRegion CompleteData => Footer is null ? Data : new(Data.Offset, Data.Length + Footer.Value.Length);
}
