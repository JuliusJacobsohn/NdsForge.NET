namespace NdsForge.Nitro.Archives;

/// <summary>Bounds a utility archive before copying its bytes or walking its directory graph.</summary>
public sealed record WifiUtilityReadOptions
{
    /// <summary>Limits the complete stored archive, including opaque gaps and trailing bytes.</summary>
    public int MaximumArchiveBytes { get; init; } = 64 * 1024 * 1024;
    /// <summary>Limits the allocation count independently from the number of named files.</summary>
    public int MaximumFileCount { get; init; } = 61440;
    /// <summary>Limits the number of directory records; the native identifier space also limits this to 4096.</summary>
    public int MaximumDirectoryCount { get; init; } = 4096;
    /// <summary>Limits child-directory depth below the root, whose depth is zero.</summary>
    public int MaximumDirectoryDepth { get; init; } = 64;

    /// <summary>Rejects invalid allocation and graph limits before reading untrusted fields.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumArchiveBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumArchiveBytes, Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumFileCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumFileCount, 61440);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDirectoryCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumDirectoryCount, 4096);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumDirectoryDepth);
    }
}
