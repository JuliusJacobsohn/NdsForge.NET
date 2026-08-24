namespace NdsForge.Graphics.Maps;

/// <summary>Identifies how an NSCR entry selects colors from one or more palettes.</summary>
public enum NitroPaletteSelection
{
    /// <summary>Sixteen palettes of sixteen colors, selected per map entry.</summary>
    SixteenBySixteen = 0,

    /// <summary>One 256-color palette.</summary>
    Single256 = 1,

    /// <summary>One of sixteen 256-color extended palettes, selected per map entry.</summary>
    Extended256 = 2,
}
