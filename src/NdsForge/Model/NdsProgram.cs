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
    /// <summary>Gets the optional 12-byte Nitro code footer following an ARM9 program.</summary>
    public NdsRegion? Footer { get; internal set; }

    /// <summary>Gets the program region including its recognized footer.</summary>
    public NdsRegion CompleteData => Footer is null ? Data : new(Data.Offset, Data.Length + Footer.Value.Length);
}
