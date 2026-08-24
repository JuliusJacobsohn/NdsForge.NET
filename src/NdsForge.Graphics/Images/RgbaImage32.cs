using NdsForge.Graphics.Colors;

namespace NdsForge.Graphics.Images;

/// <summary>Stores a dependency-free row-major RGBA raster for optional host-codec adapters.</summary>
public sealed class RgbaImage32
{
    private readonly RgbaColor32[] _pixels;

    /// <summary>Creates an immutable-sized raster detached from the caller's collection.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="pixels">Exactly <paramref name="width"/> times <paramref name="height"/> row-major pixels.</param>
    public RgbaImage32(int width, int height, IReadOnlyList<RgbaColor32> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Count != checked(width * height))
        {
            throw new ArgumentException("The pixel count does not match the image dimensions.", nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels.ToArray();
        Pixels = Array.AsReadOnly(_pixels);
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets row-major pixels.</summary>
    public IReadOnlyList<RgbaColor32> Pixels { get; }

    /// <summary>Gets one pixel with coordinate validation.</summary>
    /// <param name="x">Zero-based horizontal coordinate.</param>
    /// <param name="y">Zero-based vertical coordinate.</param>
    /// <returns>The requested pixel.</returns>
    public RgbaColor32 GetPixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return _pixels[(y * Width) + x];
    }
}
