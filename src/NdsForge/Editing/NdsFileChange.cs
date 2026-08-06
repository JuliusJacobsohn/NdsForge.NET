namespace NdsForge;

/// <summary>Describes a pending FAT payload replacement.</summary>
/// <param name="FileId">The stable FAT file ID.</param>
/// <param name="Path">The NitroFS path when the allocation is named.</param>
/// <param name="OriginalLength">The original payload length.</param>
/// <param name="ReplacementLength">The replacement payload length.</param>
/// <param name="RequiresRelocation">Whether the replacement cannot reuse its original region.</param>
public sealed record NdsFileChange(
    int FileId,
    string? Path,
    long OriginalLength,
    long ReplacementLength,
    bool RequiresRelocation);

