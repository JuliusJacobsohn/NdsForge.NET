namespace NdsForge;

/// <summary>Classifies the cartridge security interval without conflating encrypted bytes with malformed metadata.</summary>
public enum NdsSecureAreaState
{
    /// <summary>The ARM9 layout does not use the cartridge interval beginning at <c>0x4000</c>.</summary>
    Absent,

    /// <summary>The first two words contain the conventional destroyed secure-area identifier used by decrypted dumps.</summary>
    Decrypted,

    /// <summary>The first 2 KiB successfully decrypt to the secure-area identifier under the caller's KEY1 table.</summary>
    Encrypted,

    /// <summary>The interval begins with zero words used by multiboot-style images rather than a transformable secure payload.</summary>
    Multiboot,

    /// <summary>Bytes are present but neither plain markers nor a caller-key-verifiable encrypted identifier were found.</summary>
    Unrecognized,

    /// <summary>The header selects a secure interval that is truncated or otherwise structurally unavailable.</summary>
    Malformed,
}
