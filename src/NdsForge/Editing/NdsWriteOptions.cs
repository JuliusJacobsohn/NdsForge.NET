namespace NdsForge;

/// <summary>Controls preservation-oriented image saves.</summary>
public sealed record NdsWriteOptions
{
    /// <summary>Uses 512-byte relocation alignment, <c>0xFF</c> padding, verification, and no implicit path overwrite.</summary>
    public static NdsWriteOptions Default { get; } = new();

    /// <summary>Aligns newly appended payload starts; it must be a positive power of two and defaults to one ROM sector.</summary>
    public int RelocatedFileAlignment { get; init; } = 0x200;

    /// <summary>Fills bytes between the prior used end and a relocated payload, commonly <c>0xFF</c> for cartridge padding.</summary>
    public byte PaddingByte { get; init; } = 0xFF;

    /// <summary>Gets whether the completed output is reopened and verified.</summary>
    public bool VerifyOutput { get; init; } = true;

    /// <summary>Gets whether a path save may atomically replace an existing regular file.</summary>
    public bool OverwriteDestination { get; init; }

    /// <summary>Rejects alignments incompatible with the editor's overflow-safe bitwise rounding operation.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Alignment is zero or negative.</exception>
    /// <exception cref="ArgumentException">Alignment is not a power of two.</exception>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RelocatedFileAlignment);
        if ((RelocatedFileAlignment & (RelocatedFileAlignment - 1)) != 0)
        {
            throw new ArgumentException("Relocated file alignment must be a power of two.", nameof(RelocatedFileAlignment));
        }
    }
}
