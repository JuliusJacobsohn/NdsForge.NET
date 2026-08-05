namespace NdsForge;

/// <summary>Indicates the impact of a validation finding.</summary>
public enum NdsDiagnosticSeverity
{
    /// <summary>Additional information that does not indicate malformed data.</summary>
    Information,

    /// <summary>Suspicious data that can still be interpreted safely.</summary>
    Warning,

    /// <summary>Malformed or inconsistent data that prevents a reliable operation.</summary>
    Error,
}

