namespace NdsForge.Nitro.Audio;

/// <summary>Controls preservation or canonical standalone wave reconstruction.</summary>
public sealed record SwavWriteOptions
{
    /// <summary>Gets whether to retain the source marker, header extension, sample padding, and outer padding; defaults to true.</summary>
    public bool PreserveSourceLayout { get; init; } = true;

    /// <summary>Gets the maximum complete output byte count; defaults to 64 MiB.</summary>
    public int MaximumOutputBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Checks the allocation limit before composing a file.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumOutputBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumOutputBytes, Array.MaxLength);
    }
}
