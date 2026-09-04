namespace NdsForge.Graphics.Fonts;

/// <summary>Describes the optional extended FINF font bounds.</summary>
/// <param name="FontHeight">Overall font height in pixels.</param>
/// <param name="FontWidth">Overall font width in pixels.</param>
/// <param name="BearingY">Signed vertical bearing in pixels.</param>
/// <param name="BearingX">Signed horizontal bearing in pixels.</param>
/// <returns>Extended font bounds and bearings.</returns>
public readonly record struct NftrExtendedMetrics(byte FontHeight, byte FontWidth, sbyte BearingY, sbyte BearingX);
