namespace NdsForge;

/// <summary>
/// Supplies optional trust material and policy for validation checks that cannot be inferred from image bytes.
/// Structural, CRC, digest-table, and development-marker checks remain available without external keys.
/// </summary>
public sealed class NdsValidationOptions
{
    /// <summary>Retains a copied DSi HMAC key so validation cannot observe later caller buffer mutation.</summary>
    private byte[]? _dsiHmacKey;

    /// <summary>Returns a fresh keyless policy suitable for structural validation without shared mutable state.</summary>
    public static NdsValidationOptions Default => new();

    /// <summary>
    /// Recomputes DSi component HMAC-SHA1 fields with caller-supplied trust material. Merely storing a matching
    /// digest proves key possession and byte integrity; it does not establish the provenance of that key.
    /// </summary>
    /// <param name="key">Non-empty HMAC key expected to have produced the image's component fields.</param>
    /// <returns>The same options object for fluent validation configuration.</returns>
    public NdsValidationOptions SetDsiHmacKey(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A DSi validation HMAC key cannot be empty.", nameof(key));
        }

        _dsiHmacKey = key.ToArray();
        return this;
    }

    /// <summary>
    /// Verifies a recognizable no$gba development marker against the finalized 0xE00-byte header input. Unknown
    /// nonzero signatures remain unverified rather than being mislabeled invalid without a trusted RSA key.
    /// </summary>
    public bool ValidateDsiDevelopmentSignature { get; init; } = true;

    /// <summary>Exposes optional copied key bytes only to the internal integrity validator.</summary>
    internal ReadOnlyMemory<byte> DsiHmacKey => _dsiHmacKey;
}
