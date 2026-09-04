namespace NdsForge;

/// <summary>Decodes the optional twelve-byte ARM9 SDK footer immediately following stored program bytes.</summary>
/// <param name="Data">Absolute image region containing the complete footer.</param>
/// <param name="ParametersOffset">ARM9-relative offset of the SDK program-parameter table.</param>
/// <param name="OverlayHmacTableOffset">ARM9-relative offset of the Download Play overlay-HMAC table.</param>
public sealed record NdsProgramFooter(
    NdsRegion Data,
    uint ParametersOffset,
    uint OverlayHmacTableOffset);
