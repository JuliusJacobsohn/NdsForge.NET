namespace NdsForge.Nitro.Archives;

/// <summary>Separates exact source-layout preservation from explicitly bounded canonical archive placement.</summary>
public sealed record WifiUtilityWriteOptions
{
    /// <summary>Retains all source bytes for no-op and compatible same-sized payload edits; other edits rebuild canonically.</summary>
    public bool PreserveSourceLayout { get; init; } = true;
    /// <summary>Aligns each canonical payload and the output end; must be a positive power of two.</summary>
    public int FileAlignment { get; init; } = 4;
    /// <summary>Aligns the canonical FAT start independently from payloads; must be a power of two of at least four.</summary>
    public int TableAlignment { get; init; } = 4;
    /// <summary>Fills canonical table and payload alignment gaps without altering retained filename-table bytes.</summary>
    public byte PaddingByte { get; init; }
    /// <summary>Limits the complete contiguous output before allocation.</summary>
    public int MaximumOutputBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Rejects contradictory alignment and memory policies.</summary>
    internal void Validate()
    {
        if (FileAlignment <= 0 || (FileAlignment & (FileAlignment - 1)) != 0)
        {
            throw new ArgumentException("Utility payload alignment must be a positive power of two.");
        }
        if (TableAlignment < 4 || (TableAlignment & (TableAlignment - 1)) != 0)
        {
            throw new ArgumentException("Utility table alignment must be a power of two of at least four.");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumOutputBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumOutputBytes, Array.MaxLength);
    }
}
