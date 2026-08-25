namespace NdsForge.Graphics.Fonts;

/// <summary>Describes the horizontal placement and advance of an NFTR glyph.</summary>
/// <param name="BearingX">Signed left-side bearing in pixels.</param>
/// <param name="GlyphWidth">Visible glyph width in pixels.</param>
/// <param name="AdvanceWidth">Cursor advance after drawing the glyph.</param>
/// <returns>Horizontal placement and advance metrics.</returns>
public readonly record struct NftrGlyphMetrics(sbyte BearingX, byte GlyphWidth, byte AdvanceWidth);
