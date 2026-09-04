namespace NdsForge.Audio.Wav;

/// <summary>Bounds complete WAV input, sample-value allocation, chunk traversal, and sampler loops.</summary>
public sealed record WavReadOptions
{
    /// <summary>Gets the complete input limit, including retained trailing bytes; defaults to 128 MiB.</summary>
    public int MaximumInputBytes { get; init; } = 128 * 1024 * 1024;
    /// <summary>Gets the maximum decoded values across all channels; defaults to thirty-two mebisamples.</summary>
    public int MaximumSampleValues { get; init; } = 32 * 1024 * 1024;
    /// <summary>Gets the maximum number of direct RIFF chunks; defaults to 4096.</summary>
    public int MaximumChunks { get; init; } = 4096;
    /// <summary>Gets the maximum number of sampler loops; defaults to 1024.</summary>
    public int MaximumLoops { get; init; } = 1024;
    /// <summary>Gets whether to accept a final odd-sized chunk whose RIFF declaration omits its pad byte; defaults to true.</summary>
    public bool AllowMissingFinalPadding { get; init; } = true;

    /// <summary>Checks all caller-selected bounds before examining file declarations.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumInputBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumInputBytes, Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumSampleValues);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumSampleValues, Array.MaxLength / 2);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumChunks);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumLoops);
    }
}
