namespace NdsForge;

/// <summary>Identifies a bounded range of bytes in an image.</summary>
/// <param name="Offset">The zero-based byte offset.</param>
/// <param name="Length">The number of bytes in the region.</param>
public readonly record struct NdsRegion(long Offset, long Length)
{
    /// <summary>Gets the first offset after the region.</summary>
    public long End => checked(Offset + Length);

    /// <summary>Gets whether the region contains no bytes.</summary>
    public bool IsEmpty => Length == 0;

    internal static NdsRegion FromUInt32(uint offset, uint length) => new(offset, length);
}

