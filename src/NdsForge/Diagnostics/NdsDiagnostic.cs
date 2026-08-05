namespace NdsForge;

/// <summary>A structured image validation finding.</summary>
/// <param name="Code">A stable machine-readable finding code.</param>
/// <param name="Severity">The impact of the finding.</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="Region">The associated image region, when known.</param>
public sealed record NdsDiagnostic(
    string Code,
    NdsDiagnosticSeverity Severity,
    string Message,
    NdsRegion? Region = null);

