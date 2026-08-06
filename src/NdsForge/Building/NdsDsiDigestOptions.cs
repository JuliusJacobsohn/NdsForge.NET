namespace NdsForge;

/// <summary>
/// Configures the optional DSi hierarchical HMAC-SHA1 tables that cover common NTR content and DSi-mode TWL
/// Programs. Table sizes and offsets remain layout-owned so callers cannot supply internally inconsistent values.
/// </summary>
public sealed record NdsDsiDigestOptions
{
    /// <summary>Uses the conventional 1 KiB content sector and 32 sector hashes per second-level block.</summary>
    public static NdsDsiDigestOptions Default { get; } = new();

    /// <summary>
    /// Splits each covered content range into independently authenticated chunks. The value must be a power of
    /// two between 512 bytes and 16 MiB so hostile recipes cannot force pathological buffers.
    /// </summary>
    public int SectorSize { get; init; } = 0x400;

    /// <summary>
    /// Groups this many consecutive 20-byte sector HMACs into one block-table HMAC. Values from one through
    /// 65,536 are supported; 32 matches common retail metadata.
    /// </summary>
    public int BlockSectorCount { get; init; } = 0x20;

    /// <summary>Enforces format-compatible granularity and resource bounds before layout arithmetic begins.</summary>
    /// <exception cref="ArgumentException">A size is non-power-of-two, excessive, or otherwise unsupported.</exception>
    internal void Validate()
    {
        if (SectorSize < 0x200 || SectorSize > 16 * 1024 * 1024 || (SectorSize & (SectorSize - 1)) != 0)
        {
            throw new ArgumentException("DSi digest sectors must be a power of two from 512 bytes through 16 MiB.");
        }

        if (BlockSectorCount is < 1 or > 65_536)
        {
            throw new ArgumentException("A DSi digest block must contain from one through 65,536 sector hashes.");
        }
    }
}
