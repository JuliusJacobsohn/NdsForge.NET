namespace NdsForge;

/// <summary>Selects a documented Layout personality without contaminating the logical Build Recipe.</summary>
public enum NdsImageBuildProfile
{
    /// <summary>Uses NdsForge's dependency-free stable ordering, explicit metadata, and configurable deterministic padding.</summary>
    Deterministic,

    /// <summary>Reproduces characterized Nintendo DS creation conventions from ndstool 1.50.3 for byte-level interoperability.</summary>
    Ndstool1503,
}
