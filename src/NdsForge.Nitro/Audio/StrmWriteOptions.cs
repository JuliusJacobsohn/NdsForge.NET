namespace NdsForge.Nitro.Audio;

/// <summary>Chooses exact layout retention or canonical stream reconstruction.</summary>
public sealed record StrmWriteOptions
{
    /// <summary>Gets whether to preserve source layout, length conventions, extensions, and padding; defaults to true.</summary>
    public bool PreserveSourceLayout { get; init; } = true;
    /// <summary>Gets the maximum complete output byte count; defaults to 64 MiB.</summary>
    public int MaximumOutputBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Checks output bounds before copying source bytes or composing a file.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumOutputBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumOutputBytes, Array.MaxLength);
    }
}
