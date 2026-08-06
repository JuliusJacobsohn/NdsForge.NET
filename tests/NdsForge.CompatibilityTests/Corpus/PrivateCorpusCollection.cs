namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Serializes high-volume corpus cases so several 512 MiB images are never mapped and hashed concurrently.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515", Justification = "xUnit requires collection definitions to be public.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "xUnit discovers this marker through reflection.")]
public sealed class PrivateCorpusSerialGroup
{
    /// <summary>Names the shared xUnit collection used by every opt-in private-ROM differential test.</summary>
    public const string Name = "Private Nintendo DS corpus";
}
