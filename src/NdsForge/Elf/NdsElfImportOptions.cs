namespace NdsForge;

/// <summary>Bounds ELF ingestion and controls whether ndstool-style Overlay program headers become definitions.</summary>
public sealed class NdsElfImportOptions
{
    /// <summary>Uses conservative limits suitable for ordinary SDK outputs while still allowing large homebrew programs.</summary>
    public static NdsElfImportOptions Default => new();

    /// <summary>Limits buffered ELF input so non-seekable or hostile streams cannot force unbounded allocation.</summary>
    public long MaxInputBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>Limits the contiguous cartridge program produced after physical-address gaps are zero-filled.</summary>
    public int MaxProgramBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>Limits table iteration and allocation independently from the ELF format's 16-bit header count.</summary>
    public int MaxProgramHeaders { get; set; } = 4096;

    /// <summary>Limits private Overlay payloads constructed from toolchain-specific flagged program headers.</summary>
    public int MaxOverlays { get; set; } = 4096;

    /// <summary>Controls whether Overlay segments are decoded or merely reported as present in the result.</summary>
    public bool ImportOverlays { get; set; } = true;

    /// <summary>Rejects nonsensical resource bounds before input bytes are read or allocated.</summary>
    internal void Validate()
    {
        if (MaxInputBytes < 52 || MaxProgramBytes <= 0 || MaxProgramHeaders <= 0 || MaxOverlays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxInputBytes), "ELF resource limits must all be positive and permit one header.");
        }
    }
}
