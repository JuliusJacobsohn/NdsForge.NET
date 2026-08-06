namespace NdsForge;

/// <summary>
/// Supplies optional trust material and policy for validation checks that cannot be inferred from image bytes.
/// Structural, CRC, digest-table, and development-marker checks remain available without external keys.
/// </summary>
public sealed class NdsValidationOptions
{
    /// <summary>Retains a copied DSi HMAC key so validation cannot observe later caller buffer mutation.</summary>
    private byte[]? _dsiHmacKey;
    /// <summary>Retains the immutable caller table used to distinguish and checksum KEY1 secure-area states.</summary>
    private NdsKey1KeyTable? _secureAreaKeyTable;
    /// <summary>Retains immutable caller trust material for DSi header authenticity checks.</summary>
    private NdsDsiRsaPublicKey? _dsiRsaPublicKey;

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
    /// Enables secure-area identifier recovery and encrypted-form CRC validation. No default table is bundled;
    /// supplying one is an explicit trust and provenance decision separate from ordinary structural validation.
    /// </summary>
    /// <param name="keyTable">Complete immutable KEY1 seed schedule.</param>
    /// <returns>The same options object for fluent validation configuration.</returns>
    public NdsValidationOptions SetSecureAreaKeyTable(NdsKey1KeyTable keyTable)
    {
        _secureAreaKeyTable = keyTable ?? throw new ArgumentNullException(nameof(keyTable));
        return this;
    }

    /// <summary>
    /// Enables DSi header authenticity verification against one explicitly trusted RSA-1024 key. A mismatch becomes
    /// a validation error; the library never substitutes a built-in publisher trust store.
    /// </summary>
    /// <param name="publicKey">Immutable public key whose provenance the calling application has established.</param>
    /// <returns>The same options object for fluent validation configuration.</returns>
    public NdsValidationOptions SetDsiRsaPublicKey(NdsDsiRsaPublicKey publicKey)
    {
        _dsiRsaPublicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
        return this;
    }

    /// <summary>
    /// Verifies a recognizable no$gba development marker against the finalized 0xE00-byte header input. Unknown
    /// nonzero signatures remain unverified rather than being mislabeled invalid without a trusted RSA key.
    /// </summary>
    public bool ValidateDsiDevelopmentSignature { get; init; } = true;

    /// <summary>
    /// Bounds each materialized DSi digest table while sector contents themselves remain streamed. The default
    /// 64 MiB permits very large legitimate images without accepting attacker-controlled unbounded allocation.
    /// </summary>
    public int MaxDsiDigestTableBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Caps individual sector/block mismatch diagnostics while retaining a final truncation warning.</summary>
    public int MaxDsiDigestFailures { get; init; } = 64;

    /// <summary>Exposes optional copied key bytes only to the internal integrity validator.</summary>
    internal ReadOnlyMemory<byte> DsiHmacKey => _dsiHmacKey;

    /// <summary>Exposes optional KEY1 material only to the internal secure-area validator.</summary>
    internal NdsKey1KeyTable? SecureAreaKeyTable => _secureAreaKeyTable;

    /// <summary>Exposes optional DSi authenticity trust material only to the internal integrity validator.</summary>
    internal NdsDsiRsaPublicKey? DsiRsaPublicKey => _dsiRsaPublicKey;

    /// <summary>Rejects resource limits that would disable bounded validation or overflow managed buffers.</summary>
    internal void Validate()
    {
        if (MaxDsiDigestTableBytes < 20 || MaxDsiDigestFailures < 1)
        {
            throw new ArgumentException("DSi digest validation limits must allow at least one 20-byte entry and one finding.");
        }
    }
}
