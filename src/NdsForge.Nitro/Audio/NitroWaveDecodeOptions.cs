namespace NdsForge.Nitro.Audio;

/// <summary>Bounds sample allocation and selects explicit ADPCM saturation behavior.</summary>
public sealed record NitroWaveDecodeOptions
{
    /// <summary>Gets the largest permitted decoded sample-value array; defaults to sixteen mebisamples.</summary>
    public int MaximumSamples { get; init; } = 16 * 1024 * 1024;

    /// <summary>Gets the subtraction saturation convention; defaults to Nintendo DS behavior.</summary>
    public NitroAdpcmClipping AdpcmClipping { get; init; }

    /// <summary>Validates the managed-array limit and supported saturation policy.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumSamples);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumSamples, Array.MaxLength / 2);
        if (!Enum.IsDefined(AdpcmClipping)) { throw new ArgumentOutOfRangeException(nameof(AdpcmClipping)); }
    }
}
