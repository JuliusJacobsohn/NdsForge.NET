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
    uint LoadAddress);

