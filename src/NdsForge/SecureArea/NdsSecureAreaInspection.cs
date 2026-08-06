namespace NdsForge;

/// <summary>Reports secure-area presence, encryption state, and checksum evidence without modifying image bytes.</summary>
/// <param name="State">Detected structural or cryptographic state.</param>
/// <param name="Region">The conventional 16 KiB cartridge interval, or an empty region when absent.</param>
/// <param name="StoredCrc">Header checksum recorded at offset <c>0x6C</c>.</param>
/// <param name="CalculatedCrc">Checksum of the encrypted representation when it could be calculated.</param>
public sealed record NdsSecureAreaInspection(
    NdsSecureAreaState State,
    NdsRegion Region,
    ushort StoredCrc,
    ushort? CalculatedCrc)
{
    /// <summary>
    /// Distinguishes a proven match or mismatch from the unknown result produced when decrypted bytes are inspected
    /// without the KEY1 table required to reconstruct their encrypted checksum representation.
    /// </summary>
    public bool? IsCrcValid => CalculatedCrc is ushort calculated ? calculated == StoredCrc : null;

    /// <summary>Indicates that the interval can be passed to the matching encrypt or decrypt operation.</summary>
    public bool IsTransformable => State is NdsSecureAreaState.Decrypted or NdsSecureAreaState.Encrypted;
}
