namespace NdsForge.Graphics.Sprites;

/// <summary>Identifies the hardware rendering mode stored in OAM attribute zero.</summary>
public enum NitroObjectMode
{
    /// <summary>Regular opaque object rendering.</summary>
    Normal = 0,

    /// <summary>Semi-transparent object rendering.</summary>
    SemiTransparent = 1,

    /// <summary>Object-window mask rendering.</summary>
    Window = 2,

    /// <summary>Bitmap object rendering.</summary>
    Bitmap = 3,
}
