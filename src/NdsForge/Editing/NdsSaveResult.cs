namespace NdsForge;

/// <summary>Summarizes a completed image save.</summary>
/// <param name="AppliedChanges">The number of replaced FAT payloads.</param>
/// <param name="RelocatedFiles">The number of payloads moved to new regions.</param>
/// <param name="UsedImageSize">The used image size written to the header.</param>
/// <param name="PhysicalImageSize">The final stream length.</param>
public sealed record NdsSaveResult(
    int AppliedChanges,
    int RelocatedFiles,
    long UsedImageSize,
    long PhysicalImageSize)
{
    /// <summary>Reports retained unverified authentication or unavailable signing authority without implying cryptographic trust.</summary>
    public IReadOnlyList<NdsDiagnostic> Diagnostics { get; internal init; } = Array.Empty<NdsDiagnostic>();
}
