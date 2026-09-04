using System.Buffers.Binary;
using NdsForge.Graphics.Colors;

namespace NdsForge.Graphics.Images;

/// <summary>Stores row-major four-bit indices and a padded sixteen-word BGR555 palette, independent of image-file codecs.</summary>
public sealed class IndexedImage4
{
    private readonly byte[] _indices;
    private readonly ushort[] _palette;

    /// <summary>Takes ownership of validated private arrays produced by conversion.</summary>
    internal IndexedImage4(int width, int height, byte[] indices, ushort[] palette, int colorCount,
        bool transparent, bool reduced)
    {
        Width = width;
        Height = height;
        _indices = indices;
        _palette = palette;
        ColorCount = colorCount;
        HasTransparentIndex = transparent;
        WasColorReduced = reduced;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets one byte per row-major index, each between zero and fifteen.</summary>
    public ReadOnlyMemory<byte> PaletteIndices => _indices;

    /// <summary>Gets sixteen little-endian-ready BGR555 words; unused entries are zero.</summary>
    public ReadOnlyMemory<ushort> Palette => _palette;

    /// <summary>Gets populated palette entries, including the reserved transparent slot when enabled.</summary>
    public int ColorCount { get; }

    /// <summary>Gets whether palette index zero renders transparently, even when no source pixel uses it.</summary>
    public bool HasTransparentIndex { get; }

    /// <summary>Gets whether any packed opaque source color changed to fit the selected palette.</summary>
    public bool WasColorReduced { get; }

    /// <summary>Converts straight RGBA pixels to a deterministic palette and indices. Banner inputs must be 32 by 32.</summary>
    /// <param name="width">Positive width.</param>
    /// <param name="height">Positive height.</param>
    /// <param name="pixels">Exactly width times height row-major pixels.</param>
    /// <param name="options">Palette, transparency, reduction, and input limits.</param>
    /// <returns>A detached indexed image with no more than sixteen populated colors.</returns>
    public static IndexedImage4 FromRgba32(int width, int height, ReadOnlySpan<RgbaColor32> pixels,
        IndexedConversionOptions? options = null) => IndexedImageConversion.Convert(width, height, pixels, [], options);

    /// <summary>Maps pixels to an explicit ordered palette; equal distances choose the lowest eligible index.</summary>
    /// <param name="width">Positive width.</param>
    /// <param name="height">Positive height.</param>
    /// <param name="pixels">Exactly width times height row-major pixels.</param>
    /// <param name="palette">One to sixteen BGR555 words. High bits are retained but ignored for distance.</param>
    /// <param name="options">Policies; reject mode requires an exact packed-color match for every opaque pixel.</param>
    /// <returns>A detached image retaining the palette order, duplicates, and unused supplied colors.</returns>
    public static IndexedImage4 MapToPalette(int width, int height, ReadOnlySpan<RgbaColor32> pixels,
        ReadOnlySpan<ushort> palette, IndexedConversionOptions? options = null)
    {
        if (palette.IsEmpty) { throw new ArgumentException("An explicit palette cannot be empty.", nameof(palette)); }
        return IndexedImageConversion.Convert(width, height, pixels, palette, options);
    }

    /// <summary>Renders using full-range BGR555 expansion and the recorded index-zero policy.</summary>
    /// <returns>A detached straight-alpha raster.</returns>
    public RgbaImage32 Render()
    {
        var pixels = new RgbaColor32[_indices.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte index = _indices[i];
            pixels[i] = HasTransparentIndex && index == 0 ? default : new NitroColor555(_palette[index]).ToRgba32();
        }
        return new(Width, Height, pixels);
    }

    /// <summary>Encodes complete 8-by-8 tiles in row-major tile order, with the left pixel in the low nibble.</summary>
    /// <returns>Half as many bytes as pixels; dimensions must both be multiples of eight.</returns>
    public byte[] EncodeTiles()
    {
        if (Width % 8 != 0 || Height % 8 != 0)
        {
            throw new InvalidOperationException("Tile encoding requires dimensions that are multiples of eight.");
        }
        byte[] data = new byte[_indices.Length / 2];
        int offset = 0;
        for (int tileY = 0; tileY < Height; tileY += 8)
        {
            for (int tileX = 0; tileX < Width; tileX += 8)
            {
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x += 2)
                    {
                        int source = ((tileY + y) * Width) + tileX + x;
                        data[offset++] = (byte)(_indices[source] | (_indices[source + 1] << 4));
                    }
                }
            }
        }
        return data;
    }

    /// <summary>Encodes all sixteen palette entries, including padding, as thirty-two little-endian bytes.</summary>
    /// <returns>A detached palette byte array.</returns>
    public byte[] EncodePalette()
    {
        byte[] bytes = new byte[32];
        for (int i = 0; i < _palette.Length; i++) { BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), _palette[i]); }
        return bytes;
    }
}
