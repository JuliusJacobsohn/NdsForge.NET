namespace NdsForge;

/// <summary>Controls preservation-oriented image saves.</summary>
public sealed record NdsWriteOptions
{
    /// <summary>Gets the default safe write policy.</summary>
    public static NdsWriteOptions Default { get; } = new();

    /// <summary>Gets the alignment used when relocating enlarged FAT payloads.</summary>
    public int RelocatedFileAlignment { get; init; } = 0x200;

    /// <summary>Gets the byte used to initialize a newly created alignment gap.</summary>
    public byte PaddingByte { get; init; } = 0xFF;

    /// <summary>Gets whether the completed output is reopened and verified.</summary>
    public bool VerifyOutput { get; init; } = true;

    /// <summary>Gets whether a path save may atomically replace an existing regular file.</summary>
    public bool OverwriteDestination { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RelocatedFileAlignment);
        if ((RelocatedFileAlignment & (RelocatedFileAlignment - 1)) != 0)
        {
            throw new ArgumentException("Relocated file alignment must be a power of two.", nameof(RelocatedFileAlignment));
        }
    }
}

