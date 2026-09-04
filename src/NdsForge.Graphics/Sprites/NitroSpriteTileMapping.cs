namespace NdsForge.Graphics.Sprites;

/// <summary>Identifies the NCER object-character mapping mode.</summary>
public enum NitroSpriteTileMapping
{
    /// <summary>One-dimensional mapping with a 32-byte boundary.</summary>
    OneDimensional32 = 0,
    /// <summary>One-dimensional mapping with a 64-byte boundary.</summary>
    OneDimensional64 = 1,
    /// <summary>One-dimensional mapping with a 128-byte boundary.</summary>
    OneDimensional128 = 2,
    /// <summary>One-dimensional mapping with a 256-byte boundary.</summary>
    OneDimensional256 = 3,
    /// <summary>Two-dimensional object-character mapping.</summary>
    TwoDimensional = 4,
}
