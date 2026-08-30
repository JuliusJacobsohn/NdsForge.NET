namespace NdsForge;

/// <summary>Selects a source-preserving physical-length operation without editing header fields or relocating components.</summary>
public enum NdsImageResizeMode
{
    /// <summary>Copies the complete physical input, including all unclassified trailing material.</summary>
    Preserve = 0,

    /// <summary>Removes only bytes after the independently declared content extent, subject to the trailing-data policy.</summary>
    Trim = 1,

    /// <summary>Expands a cartridge to its existing header capacity; an input already larger than that capacity is rejected.</summary>
    PadToDeviceCapacity = 2,

    /// <summary>Uses an explicit physical length without changing the header capacity or any declared content.</summary>
    ExactLength = 3,
}
