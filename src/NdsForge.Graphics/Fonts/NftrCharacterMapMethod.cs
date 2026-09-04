namespace NdsForge.Graphics.Fonts;

/// <summary>Identifies how one NFTR CMAP block encodes character-to-glyph mappings.</summary>
public enum NftrCharacterMapMethod
{
    /// <summary>A contiguous character range maps to a contiguous glyph range.</summary>
    Direct = 0,

    /// <summary>A character range stores one explicit glyph index per character.</summary>
    Table = 1,

    /// <summary>The block stores a sparse list of character and glyph pairs.</summary>
    Scan = 2,
}
