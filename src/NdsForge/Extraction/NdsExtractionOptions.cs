namespace NdsForge;

/// <summary>Controls component and NitroFS extraction.</summary>
public sealed record NdsExtractionOptions
{
    /// <summary>Gets the default extraction policy.</summary>
    public static NdsExtractionOptions Default { get; } = new();

    /// <summary>Gets the image components to export.</summary>
    public NdsImageComponent Components { get; init; } = NdsImageComponent.All;

    /// <summary>Gets the existing-file policy.</summary>
    public NdsOverwritePolicy OverwritePolicy { get; init; } = NdsOverwritePolicy.Fail;

    /// <summary>Gets an optional predicate selecting named NitroFS files.</summary>
    public Func<NdsFile, bool>? FileFilter { get; init; }
}

