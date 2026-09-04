namespace NdsForge;

/// <summary>Identifies defined DSi launch-policy bits in common-header byte <c>0x1D</c>.</summary>
[Flags]
public enum NdsDsiLaunchPolicy
{
    /// <summary>No defined launch-policy bit is set.</summary>
    None = 0,

    /// <summary>The launcher retains the title for a subsequent direct jump.</summary>
    Jump = 1 << 0,

    /// <summary>The launcher permits the temporary-jump behavior.</summary>
    TemporaryJump = 1 << 1,
}
