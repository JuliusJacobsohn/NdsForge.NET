namespace NdsForge.Nitro.Audio;

/// <summary>Controls raw sample encoding, with explicit ADPCM initial state and output allocation limits.</summary>
public sealed record NitroWaveEncodeOptions
{
    /// <summary>Gets the ADPCM initial predictor; null uses the first input sample, or zero for empty input.</summary>
    public short? InitialPredictor { get; init; }

    /// <summary>Gets the ADPCM initial step index, zero through eighty-eight; defaults to zero.</summary>
    public int InitialStepIndex { get; init; }

    /// <summary>Gets the ADPCM subtraction saturation convention; defaults to Nintendo DS behavior.</summary>
    public NitroAdpcmClipping AdpcmClipping { get; init; }

    /// <summary>Gets the maximum encoded byte array, including an ADPCM header; defaults to 64 MiB.</summary>
    public int MaximumOutputBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Checks explicit state and output limits before encoding.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(InitialStepIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(InitialStepIndex, 88);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumOutputBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumOutputBytes, Array.MaxLength);
        if (!Enum.IsDefined(AdpcmClipping)) { throw new ArgumentOutOfRangeException(nameof(AdpcmClipping)); }
    }
}
