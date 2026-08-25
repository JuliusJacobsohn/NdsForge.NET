namespace NdsForge;

/// <summary>Supplies an optional debug executable and its runtime load address for deterministic image building.</summary>
public sealed class NdsDebugProgramDefinition
{
    /// <summary>Copies the debug executable so later caller-buffer changes cannot alter a reviewed recipe.</summary>
    /// <param name="contents">Non-empty bytes written to the header-declared debug region.</param>
    /// <param name="loadAddress">Runtime address receiving the first debug executable byte.</param>
    public NdsDebugProgramDefinition(ReadOnlySpan<byte> contents, uint loadAddress)
    {
        if (contents.IsEmpty)
        {
            throw new ArgumentException("A debug program must contain at least one byte.", nameof(contents));
        }

        Contents = contents.ToArray();
        LoadAddress = loadAddress;
    }

    /// <summary>Contains definition-owned executable bytes in their exact stored order.</summary>
    public ReadOnlyMemory<byte> Contents { get; }

    /// <summary>Specifies the runtime address receiving the first executable byte.</summary>
    public uint LoadAddress { get; }
}
