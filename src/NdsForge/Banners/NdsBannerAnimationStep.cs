namespace NdsForge;

/// <summary>
/// Describes one timed DSi menu-icon pose. Tile and palette frames are selected independently so an animation can
/// recolor existing artwork without duplicating its 512-byte tile data; a zero packed word is reserved as the
/// hardware sequence terminator and therefore cannot represent a step.
/// </summary>
/// <param name="Duration">Display time in 60 Hz ticks, from 1 through 255.</param>
/// <param name="TileFrame">Animated tile slot from zero through seven.</param>
/// <param name="PaletteFrame">Animated palette slot from zero through seven.</param>
/// <param name="FlipHorizontal">Mirrors the selected tile frame around its vertical axis.</param>
/// <param name="FlipVertical">Mirrors the selected tile frame around its horizontal axis.</param>
#if DOXYGEN
public record NdsBannerAnimationStep(
#else
public readonly record struct NdsBannerAnimationStep(
#endif
    byte Duration,
    byte TileFrame,
    byte PaletteFrame,
    bool FlipHorizontal = false,
    bool FlipVertical = false)
{
    /// <summary>Converts the typed pose to the bit layout stored in a DSi banner sequence entry.</summary>
    /// <returns>A nonzero little-endian word suitable for the sequence table.</returns>
    /// <exception cref="InvalidDataException">The duration is zero or either frame index is outside zero through seven.</exception>
    public ushort Pack()
    {
        Validate();
        return (ushort)(
            Duration |
            (TileFrame << 8) |
            (PaletteFrame << 11) |
            (FlipHorizontal ? 1 << 14 : 0) |
            (FlipVertical ? 1 << 15 : 0));
    }

    /// <summary>Interprets one nonzero on-disk sequence word without needing a containing banner.</summary>
    /// <param name="value">Packed duration, frame selectors, and flip flags.</param>
    /// <returns>The equivalent strongly typed pose.</returns>
    /// <exception cref="InvalidDataException">The word is zero, which denotes the end of the sequence rather than a pose.</exception>
    public static NdsBannerAnimationStep FromPacked(ushort value)
    {
        if (value == 0)
        {
            throw new InvalidDataException("A zero DSi animation word terminates the sequence and is not a step.");
        }

        return new(
            (byte)value,
            (byte)((value >> 8) & 0x07),
            (byte)((value >> 11) & 0x07),
            (value & (1 << 14)) != 0,
            (value & (1 << 15)) != 0);
    }

    /// <summary>Rejects values that would overflow their assigned sequence bitfields or collide with the terminator.</summary>
    internal void Validate()
    {
        if (Duration == 0)
        {
            throw new InvalidDataException("A DSi animation step must last at least one 60 Hz tick.");
        }

        if (TileFrame > 7 || PaletteFrame > 7)
        {
            throw new InvalidDataException("DSi animation tile and palette frame indices must be between zero and seven.");
        }
    }
}
