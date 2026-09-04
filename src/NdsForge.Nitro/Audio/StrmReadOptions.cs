namespace NdsForge.Nitro.Audio;

/// <summary>Bounds complete stream input, decoded sample values, and per-channel block traversal.</summary>
public sealed record StrmReadOptions
{
    /// <summary>Gets the maximum stored input including allocation padding; defaults to 64 MiB.</summary>
    public int MaximumInputBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Gets the maximum total decoded values across all channels; defaults to thirty-two mebisamples.</summary>
    public int MaximumSampleValues { get; init; } = 32 * 1024 * 1024;

    /// <summary>Gets the maximum number of blocks in each channel; defaults to one mebiblock.</summary>
    public int MaximumBlocksPerChannel { get; init; } = 1024 * 1024;

    /// <summary>Validates limits before inspecting untrusted declarations.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumInputBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumInputBytes, Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumSampleValues);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumSampleValues, Array.MaxLength / 2);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumBlocksPerChannel);
    }
}
