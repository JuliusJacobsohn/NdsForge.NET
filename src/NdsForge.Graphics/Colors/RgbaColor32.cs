namespace NdsForge.Graphics.Colors;

/// <summary>Stores one dependency-free straight-alpha color using eight bits per channel.</summary>
public readonly record struct RgbaColor32
{
    /// <summary>Creates one RGBA color.</summary>
    /// <param name="red">Red channel.</param>
    /// <param name="green">Green channel.</param>
    /// <param name="blue">Blue channel.</param>
    /// <param name="alpha">Alpha channel, which defaults to fully opaque.</param>
    public RgbaColor32(byte red, byte green, byte blue, byte alpha = byte.MaxValue)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    /// <summary>Gets the red channel.</summary>
    public byte Red { get; }

    /// <summary>Gets the green channel.</summary>
    public byte Green { get; }

    /// <summary>Gets the blue channel.</summary>
    public byte Blue { get; }

    /// <summary>Gets the alpha channel.</summary>
    public byte Alpha { get; }
}
