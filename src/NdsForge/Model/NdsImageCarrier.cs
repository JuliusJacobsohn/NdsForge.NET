namespace NdsForge;

/// <summary>Identifies the storage form independently from the processor execution mode and filename extension.</summary>
public enum NdsImageCarrier
{
    /// <summary>Indicates contradictory or unsupported carrier declarations that require explicit investigation.</summary>
    Unknown,
    /// <summary>Identifies an image with cartridge storage conventions, including ordinary DS homebrew.</summary>
    Cartridge,
    /// <summary>Identifies executable title content intended for DSi internal storage or an SD card.</summary>
    DigitalSrl,
}
