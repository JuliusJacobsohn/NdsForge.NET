namespace NdsForge;

/// <summary>Associates a stable NitroFS file ID with its physical image region.</summary>
/// <param name="FileId">The zero-based file ID.</param>
/// <param name="Data">The allocated image region.</param>
public sealed record NdsFileAllocation(int FileId, NdsRegion Data);

