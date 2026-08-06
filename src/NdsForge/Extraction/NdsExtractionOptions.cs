namespace NdsForge;

/// <summary>Controls component and NitroFS extraction.</summary>
public sealed record NdsExtractionOptions
{
    /// <summary>Exports every supported component, rejects existing targets, and applies no NitroFS predicate.</summary>
    public static NdsExtractionOptions Default { get; } = new();

    /// <summary>Selects independent raw and structured component groups through combinable <see cref="NdsImageComponent"/> flags.</summary>
    public NdsImageComponent Components { get; init; } = NdsImageComponent.All;

    /// <summary>Determines whether a pre-existing regular target aborts, is atomically replaced, or counts as skipped.</summary>
    public NdsOverwritePolicy OverwritePolicy { get; init; } = NdsOverwritePolicy.Fail;

    /// <summary>Filters named NitroFS payloads only; headers, tables, overlays, and banners remain controlled by component flags.</summary>
    public Func<NdsFile, bool>? FileFilter { get; init; }
}
