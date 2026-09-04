namespace NdsForge.Graphics.Palettes;

/// <summary>Identifies the standard Nitro indexed-texture depth values serialized by NCLR and NCGR.</summary>
public enum NitroColorDepth
{
    /// <summary>No indexed color depth has been selected.</summary>
    None = 0,

    /// <summary>Four-bit indices select up to 16 colors per palette.</summary>
    Indexed4Bpp = 3,

    /// <summary>Eight-bit indices select up to 256 colors per palette.</summary>
    Indexed8Bpp = 4,
}
