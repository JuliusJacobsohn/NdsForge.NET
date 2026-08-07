namespace NdsForge;

/// <summary>Identifies a bounded range of bytes in an image.</summary>
/// <param name="Offset">The zero-based byte offset.</param>
/// <param name="Length">The number of bytes in the region.</param>
#if DOXYGEN
public record NdsRegion(long Offset, long Length)
#else
public readonly record struct NdsRegion(long Offset, long Length)
#endif
{
    /// <summary>Computes the exclusive end offset used by FAT records and overflow-safe bounds checks.</summary>
    public long End => checked(Offset + Length);

    /// <summary>Gets whether the region contains no bytes.</summary>
    public bool IsEmpty => Length == 0;

    /// <summary>Widens the unsigned 32-bit offset/length pairs stored in DS headers without sign conversion.</summary>
    /// <param name="offset">Little-endian on-cartridge offset already decoded as unsigned.</param>
    /// <param name="length">Little-endian on-cartridge byte count already decoded as unsigned.</param>
    /// <returns>A region usable by the library's 64-bit source abstraction.</returns>
    internal static NdsRegion FromUInt32(uint offset, uint length) => new(offset, length);
}
