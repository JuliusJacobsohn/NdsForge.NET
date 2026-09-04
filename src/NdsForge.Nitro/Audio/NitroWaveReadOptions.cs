namespace NdsForge.Nitro.Audio;

/// <summary>Bounds stored wave bytes and declared decoded sample counts before copying or decoding.</summary>
public sealed record NitroWaveReadOptions
{
    /// <summary>Gets the maximum input length, including preserved padding; defaults to 64 MiB.</summary>
    public int MaximumInputBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Gets the maximum declared mono sample count; defaults to sixteen mebisamples.</summary>
    public int MaximumSamples { get; init; } = 16 * 1024 * 1024;

    /// <summary>Checks allocation bounds independently from file contents.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumInputBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumInputBytes, Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumSamples);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumSamples, Array.MaxLength / 2);
    }
}
