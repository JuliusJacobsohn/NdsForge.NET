namespace NdsForge.Graphics.Fonts;

/// <summary>Associates one encoded character value with a glyph index.</summary>
/// <param name="CharacterCode">Stored character code.</param>
/// <param name="GlyphIndex">Zero-based glyph index.</param>
/// <returns>A character-to-glyph association.</returns>
public readonly record struct NftrCharacterMapping(ushort CharacterCode, ushort GlyphIndex);
