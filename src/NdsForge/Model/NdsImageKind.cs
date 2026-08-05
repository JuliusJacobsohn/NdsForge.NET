namespace NdsForge;

/// <summary>Identifies the hardware family targeted by an image.</summary>
public enum NdsImageKind
{
    /// <summary>A Nintendo DS application.</summary>
    NintendoDs = 0,

    /// <summary>A Nintendo DS application with DSi-enhanced content.</summary>
    NintendoDsiEnhanced = 2,

    /// <summary>A DSi-exclusive application.</summary>
    NintendoDsiExclusive = 3,
}

