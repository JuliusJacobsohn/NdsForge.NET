namespace NdsForge;

/// <summary>Controls whether shrinking may remove unclassified bytes beyond all declared content.</summary>
public enum NdsTrailingDataPolicy
{
    /// <summary>Requires every removed byte to equal the selected padding byte before the destination can change.</summary>
    RequirePadding = 0,

    /// <summary>Explicitly discards the selected trailing interval regardless of its contents and reports a warning.</summary>
    Discard = 1,
}
