namespace NdsForge;

/// <summary>Contains all diagnostics produced by a validation pass.</summary>
public sealed class NdsValidationResult
{
    /// <summary>Freezes one validation pass so later callers cannot reorder or mutate its deterministic findings.</summary>
    /// <param name="diagnostics">Findings already ordered by validation phase and component.</param>
    internal NdsValidationResult(IEnumerable<NdsDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics.ToArray();
    }

    /// <summary>Retains stable validation order so command-line output, tests, and build logs remain reproducible.</summary>
    public IReadOnlyList<NdsDiagnostic> Diagnostics { get; }

    /// <summary>Gets whether validation completed without errors.</summary>
    public bool IsValid => Diagnostics.All(static diagnostic => diagnostic.Severity != NdsDiagnosticSeverity.Error);
}
