using NdsForge.Graphics.Colors;

namespace NdsForge.Graphics.Images;

/// <summary>Converts the fixed monochrome cartridge-header image without embedding any proprietary image data.</summary>
/// <remarks>Correct conversion and checksums do not establish acceptance by retail firmware. Host image-file codecs remain separate.</remarks>
public sealed class CartridgeLogo
{
    private readonly byte[] _data;
    private readonly byte[] _pixels;

    private CartridgeLogo(byte[] data, byte[] pixels, int encodedBitLength)
    {
        _data = data;
        _pixels = pixels;
        EncodedBitLength = encodedBitLength;
    }

    /// <summary>Gets the required image width in pixels.</summary>
    public const int Width = 104;

    /// <summary>Gets the required image height in pixels.</summary>
    public const int Height = 16;

    /// <summary>Gets the fixed number of bytes in the encoded header field.</summary>
    public const int EncodedByteLength = 156;

    /// <summary>Gets a detached source block, including unused tail bits.</summary>
    public ReadOnlyMemory<byte> RawData => _data;

    /// <summary>Gets 1,664 row-major values: zero for background and one for foreground.</summary>
    public ReadOnlyMemory<byte> Pixels => _pixels;

    /// <summary>Gets the significant encoded bit count, excluding unused tail bits.</summary>
    public int EncodedBitLength { get; }

    /// <summary>Decodes exactly one header field while retaining all source bytes, including nonzero unused tail bits.</summary>
    /// <param name="data">Exactly 156 encoded bytes, normally at DS header offset 0xC0.</param>
    /// <returns>A detached source-preserving image.</returns>
    /// <exception cref="FormatException">The field length, filter header, or bounded compressed stream is invalid.</exception>
    public static CartridgeLogo Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != EncodedByteLength) { throw new FormatException("A cartridge logo field must contain exactly 156 bytes."); }
        byte[] pixels = CartridgeLogoEncoding.Decode(data, out int bits);
        return new(data.ToArray(), pixels, bits);
    }

    /// <summary>Creates canonical zero-padded encoding from an exact monochrome pixel plane.</summary>
    /// <param name="pixels">Exactly 1,664 row-major values, each zero for background or one for foreground.</param>
    /// <returns>A detached encoded image.</returns>
    /// <exception cref="ArgumentException">The pixel count or a pixel value is invalid.</exception>
    /// <exception cref="InvalidOperationException">The lossless encoding exceeds the 1,248-bit field capacity.</exception>
    public static CartridgeLogo FromPixels(ReadOnlySpan<byte> pixels)
    {
        ValidatePixels(pixels);
        byte[] data = CartridgeLogoEncoding.Encode(pixels, out int bits);
        return new(data, pixels.ToArray(), bits);
    }

    /// <summary>Imports an exact two-color raster without thresholding, quantization, or implicit alpha compositing.</summary>
    /// <param name="pixels">Exactly 1,664 row-major straight-alpha pixels.</param>
    /// <param name="foreground">The exact color, including alpha, to map to one.</param>
    /// <param name="background">A distinct exact color, including alpha, to map to zero.</param>
    /// <returns>A detached canonically encoded image.</returns>
    /// <exception cref="ArgumentException">The count, color pair, or an input color is invalid.</exception>
    /// <exception cref="InvalidOperationException">The lossless encoding exceeds field capacity.</exception>
    public static CartridgeLogo FromRgba32(ReadOnlySpan<RgbaColor32> pixels, RgbaColor32 foreground, RgbaColor32 background)
    {
        if (pixels.Length != Width * Height) { throw new ArgumentException("A cartridge logo requires exactly 104 by 16 pixels.", nameof(pixels)); }
        if (foreground == background) { throw new ArgumentException("Foreground and background colors must be distinct.", nameof(background)); }
        byte[] plane = new byte[Width * Height];
        for (int i = 0; i < plane.Length; i++)
        {
            if (pixels[i] == foreground) { plane[i] = 1; }
            else if (pixels[i] != background) { throw new ArgumentException("Every pixel must exactly match the selected foreground or background color.", nameof(pixels)); }
        }
        return FromPixels(plane);
    }

    /// <summary>Measures lossless storage before attempting creation, including the fixed filter header.</summary>
    /// <param name="pixels">Exactly 1,664 row-major zero-or-one values.</param>
    /// <returns>The required bit count; values above 1,248 cannot fit and are not truncated.</returns>
    /// <exception cref="ArgumentException">The pixel count or a pixel value is invalid.</exception>
    public static int MeasureEncodedBitLength(ReadOnlySpan<byte> pixels)
    {
        ValidatePixels(pixels);
        return CartridgeLogoEncoding.Measure(pixels);
    }

    /// <summary>Renders foreground and background with caller-selected straight-alpha colors.</summary>
    /// <param name="foreground">Color for one-valued pixels.</param>
    /// <param name="background">Color for zero-valued pixels.</param>
    /// <returns>A detached 104-by-16 raster, suitable for a separate host-codec adapter.</returns>
    public RgbaImage32 Render(RgbaColor32 foreground, RgbaColor32 background)
    {
        var colors = new RgbaColor32[_pixels.Length];
        for (int i = 0; i < colors.Length; i++) { colors[i] = _pixels[i] == 0 ? background : foreground; }
        return new(Width, Height, colors);
    }

    /// <summary>Encodes 26 row-major 8-by-8 one-bit tiles, with each row's left pixel in bit zero.</summary>
    /// <returns>A detached 208-byte packed plane.</returns>
    public byte[] EncodeTiles() => CartridgeLogoEncoding.PackPixels(_pixels);

    /// <summary>Copies the complete original field, retaining even unused nonzero tail bits.</summary>
    /// <returns>Exactly 156 bytes.</returns>
    public byte[] WritePreserved() => (byte[])_data.Clone();

    /// <summary>Re-encodes the image deterministically and clears all unused tail bits.</summary>
    /// <returns>Exactly 156 canonical bytes; this is not a source-preservation operation.</returns>
    public byte[] WriteCanonical() => CartridgeLogoEncoding.Encode(_pixels, out _);

    private static void ValidatePixels(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != Width * Height) { throw new ArgumentException("A cartridge logo requires exactly 104 by 16 pixels.", nameof(pixels)); }
        foreach (byte pixel in pixels)
        {
            if (pixel > 1) { throw new ArgumentException("Monochrome pixels must be zero or one.", nameof(pixels)); }
        }
    }
}
