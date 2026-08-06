namespace NdsForge;

/// <summary>Controls resource limits applied while parsing an image.</summary>
public sealed record NdsReadOptions
{
    /// <summary>Supplies conservative allocation and recursion ceilings suitable for untrusted retail-sized images.</summary>
    public static NdsReadOptions Default { get; } = new();

    /// <summary>Limits bytes allocated for the NitroFS FNT before any names or directory links are decoded.</summary>
    /// <remarks>The 16 MiB default is intentionally far above normal retail tables while bounding hostile length fields.</remarks>
    public int MaximumFileNameTableBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Limits bytes allocated for the FAT, whose eight-byte records map file IDs to image regions.</summary>
    public int MaximumFileAllocationTableBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Caps reachable FNT directory records and may not exceed the format's 4,096-ID namespace.</summary>
    public int MaximumDirectoryCount { get; init; } = 4096;

    /// <summary>Bounds recursive FNT traversal so cyclic or adversarial directory trees cannot exhaust the call stack.</summary>
    public int MaximumDirectoryDepth { get; init; } = 128;

    /// <summary>Bounds entries decoded independently from each ARM9 or ARM7 overlay table.</summary>
    public int MaximumOverlayCount { get; init; } = 65_536;

    /// <summary>Bounds banner allocation before its version field selects a smaller, exact supported layout.</summary>
    public int MaximumBannerBytes { get; init; } = 0x23C0;

    /// <summary>Rejects disabled or nonsensical limits before an image source is opened or retained.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Any configured ceiling is zero or negative.</exception>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumFileNameTableBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumFileAllocationTableBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDirectoryCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDirectoryDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumOverlayCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumBannerBytes);
    }
}
