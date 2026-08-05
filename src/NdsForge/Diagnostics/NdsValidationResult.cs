namespace NdsForge;

/// <summary>Contains all diagnostics produced by a validation pass.</summary>
public sealed class NdsValidationResult
{
    internal NdsValidationResult(IEnumerable<NdsDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics.ToArray();
    }

    /// <summary>Gets the diagnostics in deterministic validation order.</summary>
    public IReadOnlyList<NdsDiagnostic> Diagnostics { get; }

    /// <summary>Gets whether validation completed without errors.</summary>
    public bool IsValid => Diagnostics.All(static diagnostic => diagnostic.Severity != NdsDiagnosticSeverity.Error);
}

