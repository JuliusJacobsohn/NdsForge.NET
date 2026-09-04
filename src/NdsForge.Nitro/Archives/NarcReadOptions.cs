namespace NdsForge.Nitro.Archives;

/// <summary>Bounds NARC metadata traversal before untrusted counts can drive allocations or recursion.</summary>
public sealed class NarcReadOptions
{
    /// <summary>Gets or initializes the largest accepted FAT file count.</summary>
    public int MaximumFileCount { get; init; } = 1_000_000;

    /// <summary>Gets or initializes the largest accepted FNT directory count.</summary>
    public int MaximumDirectoryCount { get; init; } = 4096;

    /// <summary>Gets or initializes the deepest accepted named directory path.</summary>
    public int MaximumDirectoryDepth { get; init; } = 64;

    /// <summary>Rejects nonpositive limits before parsing begins.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumFileCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDirectoryCount);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumDirectoryDepth);
    }
}
