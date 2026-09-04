namespace NdsForge;

/// <summary>Bounds workspace payload materialization independently from the streamed preservation snapshot.</summary>
public sealed record NdsWorkspaceImportOptions
{
    /// <summary>Uses 256 MiB per component and 1 GiB of aggregate native input bytes.</summary>
    public static NdsWorkspaceImportOptions Default { get; } = new();

    /// <summary>Limits both original and edited component lengths before allocating component buffers.</summary>
    public int MaximumAssetBytes { get; init; } = 256 * 1024 * 1024;

    /// <summary>Limits the sum of each component's larger original/edited length; this is not a process peak-memory guarantee.</summary>
    public long MaximumTotalAssetBytes { get; init; } = 1024 * 1024 * 1024;

    /// <summary>Rejects disabled limits or a single-component limit exceeding managed contiguous-array support.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumAssetBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumAssetBytes, Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumTotalAssetBytes);
    }
}
