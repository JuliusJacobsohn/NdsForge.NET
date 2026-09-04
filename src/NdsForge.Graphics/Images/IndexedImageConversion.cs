using NdsForge.Graphics.Colors;

namespace NdsForge.Graphics.Images;

/// <summary>Builds a bounded color histogram, chooses colors, and maps each original pixel once.</summary>
internal static class IndexedImageConversion
{
    /// <summary>Validates all allocation limits before creating histogram or output buffers.</summary>
    internal static IndexedImage4 Convert(int width, int height, ReadOnlySpan<RgbaColor32> pixels,
        ReadOnlySpan<ushort> suppliedPalette, IndexedConversionOptions? options)
    {
        options ??= new();
        options.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        long pixelCount = (long)width * height;
        if (pixelCount > options.MaximumPixels) { throw new ArgumentOutOfRangeException(nameof(pixels), "The pixel limit was exceeded."); }
        if (pixels.Length != pixelCount) { throw new ArgumentException("Pixel count does not match dimensions.", nameof(pixels)); }
        if (suppliedPalette.Length > options.MaximumColors) { throw new ArgumentException("The supplied palette exceeds the color limit.", nameof(suppliedPalette)); }

        int firstOpaque = options.ReserveTransparentIndex ? 1 : 0;
        int[] counts = new int[32768];
        var colors = new List<ushort>();
        foreach (RgbaColor32 pixel in pixels)
        {
            if (options.ReserveTransparentIndex && pixel.Alpha <= options.AlphaThreshold) { continue; }
            ushort color = options.Pack(pixel);
            if (counts[color]++ == 0) { colors.Add(color); }
        }

        ushort[] palette = new ushort[16];
        int colorCount;
        if (!suppliedPalette.IsEmpty)
        {
            if (suppliedPalette.Length <= firstOpaque && colors.Count != 0)
            {
                throw new ArgumentException("The palette has no opaque color slot.", nameof(suppliedPalette));
            }
            suppliedPalette.CopyTo(palette);
            colorCount = suppliedPalette.Length;
        }
        else
        {
            int available = options.MaximumColors - firstOpaque;
            if (colors.Count > available)
            {
                if (options.PaletteOverflow == IndexedPaletteOverflow.Reject)
                {
                    throw new ArgumentException("The image has too many distinct opaque BGR555 colors.", nameof(pixels));
                }
                colors = IndexedPaletteReduction.Reduce(counts, available);
            }
            colors.CopyTo(palette, firstOpaque);
            colorCount = firstOpaque + colors.Count;
        }

        byte[] mapping = new byte[32768];
        bool reduced = false;
        for (int color = 0; color < counts.Length; color++)
        {
            if (counts[color] == 0) { continue; }
            int index = firstOpaque + IndexedPaletteReduction.Nearest((ushort)color, palette.AsSpan(firstOpaque, colorCount - firstOpaque));
            bool changed = color != (palette[index] & 0x7FFF);
            if (changed && options.PaletteOverflow == IndexedPaletteOverflow.Reject)
            {
                throw new ArgumentException("An opaque pixel has no exact packed-color match in the supplied palette.", nameof(suppliedPalette));
            }
            reduced |= changed;
            mapping[color] = (byte)index;
        }

        byte[] indices = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            RgbaColor32 pixel = pixels[i];
            if (!options.ReserveTransparentIndex || pixel.Alpha > options.AlphaThreshold) { indices[i] = mapping[options.Pack(pixel)]; }
        }
        return new(width, height, indices, palette, colorCount, options.ReserveTransparentIndex, reduced);
    }
}
