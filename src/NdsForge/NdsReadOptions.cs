namespace NdsForge;

/// <summary>Controls resource limits applied while parsing an image.</summary>
public sealed record NdsReadOptions
{
    /// <summary>Gets the default safe parsing limits.</summary>
    public static NdsReadOptions Default { get; } = new();

    /// <summary>Gets the maximum accepted filename-table size.</summary>
    public int MaximumFileNameTableBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Gets the maximum accepted file-allocation-table size.</summary>
    public int MaximumFileAllocationTableBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Gets the maximum accepted NitroFS directory count.</summary>
    public int MaximumDirectoryCount { get; init; } = 4096;

    /// <summary>Gets the maximum accepted NitroFS directory nesting depth.</summary>
    public int MaximumDirectoryDepth { get; init; } = 128;

    /// <summary>Gets the maximum accepted entries in each overlay table.</summary>
    public int MaximumOverlayCount { get; init; } = 65_536;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumFileNameTableBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumFileAllocationTableBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDirectoryCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDirectoryDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumOverlayCount);
    }
}
