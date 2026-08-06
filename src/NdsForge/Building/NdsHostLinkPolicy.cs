namespace NdsForge;

/// <summary>Controls treatment of host reparse points and symbolic links during bounded directory ingestion.</summary>
public enum NdsHostLinkPolicy
{
    /// <summary>Rejects the complete import when any traversed entry redirects outside ordinary directory structure.</summary>
    Reject,
    /// <summary>Omits linked files or directory subtrees and reports how many entries were skipped.</summary>
    Skip,
}
