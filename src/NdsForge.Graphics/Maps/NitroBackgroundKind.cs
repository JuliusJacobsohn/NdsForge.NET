namespace NdsForge.Graphics.Maps;

/// <summary>Identifies the NSCR hardware background map representation.</summary>
public enum NitroBackgroundKind
{
    /// <summary>Sixteen-bit text entries with tile, flip, and palette fields.</summary>
    Text = 0,

    /// <summary>Eight-bit affine entries containing tile numbers only.</summary>
    Affine = 1,

    /// <summary>Sixteen-bit extended entries.</summary>
    Extended = 2,
}
