namespace NdsForge.Graphics.Tiles;

/// <summary>Identifies the mapping boundary value stored in an NCGR CHAR block.</summary>
public enum NitroTileMapping
{
    /// <summary>Two-dimensional tile addressing.</summary>
    TwoDimensional = 0,

    /// <summary>One-dimensional addressing with a 32 KiB boundary.</summary>
    OneDimensional32K = 0x00000010,

    /// <summary>One-dimensional addressing with a 64 KiB boundary.</summary>
    OneDimensional64K = 0x00100010,

    /// <summary>One-dimensional addressing with a 128 KiB boundary.</summary>
    OneDimensional128K = 0x00200010,

    /// <summary>One-dimensional addressing with a 256 KiB boundary.</summary>
    OneDimensional256K = 0x00300010,
}
