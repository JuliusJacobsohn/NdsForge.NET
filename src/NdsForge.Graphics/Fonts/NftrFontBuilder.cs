using System.Buffers.Binary;

namespace NdsForge.Graphics.Fonts;

/// <summary>Edits NFTR glyph pixels and metrics with exact preservation or deterministic reconstruction.</summary>
public sealed class NftrFontBuilder
{
    private readonly NftrFont _source;
    private readonly byte[][] _pixels;
    private readonly NftrGlyphMetrics[] _metrics;
    private readonly HashSet<int> _changedPixels = [];
    private readonly HashSet<int> _changedMetrics = [];

    internal NftrFontBuilder(NftrFont source)
    {
        _source = source;
        _pixels = source.Glyphs.Select(static glyph => glyph.StoredPixels.ToArray()).ToArray();
        _metrics = source.Glyphs.Select(static glyph => glyph.Metrics).ToArray();
    }

    /// <summary>Replaces one glyph's row-major indices in stored orientation.</summary>
    /// <param name="glyphIndex">Zero-based glyph index.</param>
    /// <param name="pixels">Exactly <c>CellWidth * CellHeight</c> indices.</param>
    /// <returns>This builder.</returns>
    public NftrFontBuilder ReplaceGlyphPixels(int glyphIndex, IReadOnlyList<byte> pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ValidateGlyphIndex(glyphIndex);
        if (pixels.Count != _source.CellWidth * _source.CellHeight)
            throw new ArgumentException("The replacement pixel count does not match the NFTR cell.", nameof(pixels));
        int colorCount = 1 << _source.BitsPerPixel;
        if (pixels.Any(value => value >= colorCount))
            throw new ArgumentException("A replacement index exceeds the NFTR bit depth.", nameof(pixels));
        _pixels[glyphIndex] = pixels.ToArray();
        _changedPixels.Add(glyphIndex);
        return this;
    }

    /// <summary>Replaces one glyph's placement and advance metrics.</summary>
    /// <param name="glyphIndex">Zero-based glyph index.</param>
    /// <param name="metrics">Replacement placement and advance metrics.</param>
    /// <returns>This builder.</returns>
    public NftrFontBuilder ReplaceGlyphMetrics(int glyphIndex, NftrGlyphMetrics metrics)
    {
        ValidateGlyphIndex(glyphIndex);
        _metrics[glyphIndex] = metrics;
        _changedMetrics.Add(glyphIndex);
        return this;
    }

    /// <summary>Writes an exact layout-preserving edit by default or a canonical NFTR.</summary>
    /// <param name="preserveSourceLayout">Patches only edited glyphs and metrics when true.</param>
    /// <returns>Complete NFTR bytes.</returns>
    public byte[] Build(bool preserveSourceLayout = true)
    {
        if (!preserveSourceLayout) return NftrFontWriter.Write(_source, _pixels, _metrics);
        (byte[] source, int[] glyphOffsets, int[] metricOffsets) = _source.GetPreservationData();
        byte[] result = source.ToArray();
        foreach (int glyph in _changedPixels)
            EncodePixels(_pixels[glyph], _source.BitsPerPixel, result.AsSpan(glyphOffsets[glyph], _source.GlyphDataLength));
        foreach (int glyph in _changedMetrics)
        {
            if (metricOffsets[glyph] < 0)
                throw new InvalidOperationException("The source layout has no explicit metric record for this glyph.");
            WriteMetrics(result.AsSpan(metricOffsets[glyph]), _metrics[glyph]);
        }
        return result;
    }

    internal static void EncodePixels(IReadOnlyList<byte> pixels, int depth, Span<byte> target)
    {
        target.Clear();
        int bit = 0;
        foreach (byte pixel in pixels)
        {
            for (int plane = depth - 1; plane >= 0; plane--, bit++)
                target[bit >> 3] |= (byte)(((pixel >> plane) & 1) << (7 - (bit & 7)));
        }
    }

    internal static void WriteMetrics(Span<byte> target, NftrGlyphMetrics metrics)
    {
        target[0] = (byte)metrics.BearingX;
        target[1] = metrics.GlyphWidth;
        target[2] = metrics.AdvanceWidth;
    }

    private void ValidateGlyphIndex(int glyphIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(glyphIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(glyphIndex, _pixels.Length);
    }
}
