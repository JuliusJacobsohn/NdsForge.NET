namespace NdsForge;

/// <summary>Classifies how one semantic or physical image value changed between manifest snapshots.</summary>
public enum NdsDifferenceKind
{
    /// <summary>A component or optional structure exists only in the right image.</summary>
    Added,
    /// <summary>A component or optional structure exists only in the left image.</summary>
    Removed,
    /// <summary>Content or metadata differs while the component retains the same logical identity.</summary>
    Modified,
    /// <summary>The same component occupies a different physical image interval.</summary>
    Relocated,
    /// <summary>The same logical component has a different FAT or similar numeric identifier.</summary>
    Renumbered,
    /// <summary>A uniquely content-matched file changed its canonical NitroFS path.</summary>
    Moved,
}
