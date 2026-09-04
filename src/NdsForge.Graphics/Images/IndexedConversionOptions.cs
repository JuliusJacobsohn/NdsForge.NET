using NdsForge.Graphics.Colors;

namespace NdsForge.Graphics.Images;

/// <summary>Controls deterministic four-bit conversion without host image codecs or implicit alpha blending.</summary>
public sealed record IndexedConversionOptions
{
    /// <summary>Gets the maximum palette entries, including a reserved transparent entry; defaults to sixteen.</summary>
    public int MaximumColors { get; init; } = 16;

    /// <summary>Gets whether index zero is always reserved for transparency, as required by banners; defaults to true.</summary>
    public bool ReserveTransparentIndex { get; init; } = true;

    /// <summary>Gets the inclusive alpha threshold for transparency; defaults to zero. Higher alpha becomes opaque.</summary>
    public byte AlphaThreshold { get; init; }

    /// <summary>Gets the RGB packing rule; defaults to discarding the low three bits of each channel.</summary>
    public NitroColorReduction ColorReduction { get; init; } = NitroColorReduction.DiscardLowBits;

    /// <summary>Gets whether excess packed colors are reduced or rejected; defaults to reduction.</summary>
    public IndexedPaletteOverflow PaletteOverflow { get; init; } = IndexedPaletteOverflow.Reduce;

    /// <summary>Gets the maximum input pixels accepted before allocation; defaults to sixteen mebipixels.</summary>
    public int MaximumPixels { get; init; } = 16 * 1024 * 1024;

    /// <summary>Validates palette, enum, and allocation limits before reading pixels.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumColors, ReserveTransparentIndex ? 2 : 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumColors, 16);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPixels);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumPixels, Array.MaxLength / 4);
        if (!Enum.IsDefined(ColorReduction)) { throw new ArgumentOutOfRangeException(nameof(ColorReduction)); }
        if (!Enum.IsDefined(PaletteOverflow)) { throw new ArgumentOutOfRangeException(nameof(PaletteOverflow)); }
    }

    /// <summary>Reduces straight RGB without alpha multiplication or retained high palette bits.</summary>
    internal ushort Pack(RgbaColor32 color) => ColorReduction == NitroColorReduction.Nearest
        ? NitroColor555.FromRgba32(color).PackedValue
        : (ushort)((color.Red >> 3) | ((color.Green >> 3) << 5) | ((color.Blue >> 3) << 10));
}

/// <summary>Selects how an eight-bit RGB channel becomes a five-bit channel.</summary>
public enum NitroColorReduction
{
    /// <summary>Discards the three least significant bits: values zero through seven become zero.</summary>
    DiscardLowBits,

    /// <summary>Rounds full-range RGB to the nearest value, consistently with <see cref="NitroColor555.FromRgba32"/>.</summary>
    Nearest,
}

/// <summary>Selects behavior when an image has more packed opaque colors than available palette slots.</summary>
public enum IndexedPaletteOverflow
{
    /// <summary>Uses deterministic, frequency-weighted color reduction in five-bit RGB space.</summary>
    Reduce,

    /// <summary>Rejects input that cannot retain all packed colors exactly.</summary>
    Reject,
}
