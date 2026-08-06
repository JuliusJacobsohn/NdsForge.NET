namespace NdsForge;

/// <summary>Describes semantic preservation edits before any destination is created or truncated.</summary>
/// <param name="Files">FAT payload replacements with relocation requirements calculated from source capacity.</param>
/// <param name="HeaderFieldsChanged">Whether caller-edited identity or card-control values differ from the source.</param>
/// <param name="BannerReplaced">Whether banner bytes will be overwritten or relocated.</param>
/// <param name="Repairs">Independently named checksum repairs selected by the caller.</param>
public sealed record NdsEditPlan(
    IReadOnlyList<NdsFileChange> Files,
    bool HeaderFieldsChanged,
    bool BannerReplaced,
    NdsRepairKind Repairs)
{
    /// <summary>Allows automation to avoid writing when a session has no semantic changes or explicit repairs.</summary>
    public bool HasChanges => Files.Count != 0 || HeaderFieldsChanged || BannerReplaced || Repairs != NdsRepairKind.None;
}
