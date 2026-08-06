namespace NdsForge;

/// <summary>Summarizes a completed extraction.</summary>
/// <param name="WrittenFiles">The number of files written.</param>
/// <param name="SkippedFiles">The number of existing files skipped by policy.</param>
/// <param name="WrittenBytes">The total number of bytes written.</param>
public sealed record NdsExtractionResult(int WrittenFiles, int SkippedFiles, long WrittenBytes);

