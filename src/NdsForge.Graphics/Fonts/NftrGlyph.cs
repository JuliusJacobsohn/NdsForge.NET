namespace NdsForge.Graphics.Fonts;

/// <summary>Models one indexed NFTR glyph in stored pixel order.</summary>
public sealed class NftrGlyph
{
    internal NftrGlyph(int index, IReadOnlyList<byte> storedPixels, NftrGlyphMetrics metrics)
    {
        Index = index;
        StoredPixels = Array.AsReadOnly(storedPixels.ToArray());
        Metrics = metrics;
    }

    /// <summary>Gets the zero-based glyph index.</summary>
    public int Index { get; }

    /// <summary>Gets row-major color indices before applying the preserved rotation flags.</summary>
    public IReadOnlyList<byte> StoredPixels { get; }

    /// <summary>Gets the glyph's placement and advance metrics.</summary>
    public NftrGlyphMetrics Metrics { get; }
}
