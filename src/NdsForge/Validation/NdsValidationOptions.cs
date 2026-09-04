namespace NdsForge;

/// <summary>
/// Supplies optional trust material and policy for validation checks that cannot be inferred from image bytes.
/// Structural, CRC, digest-table, and development-marker checks remain available without external keys.
/// </summary>
public sealed class NdsValidationOptions
{
    /// <summary>Retains a copied DSi HMAC key so validation cannot observe later caller buffer mutation.</summary>
    private byte[]? _dsiHmacKey;
    /// <summary>Retains an optional classic-DS per-overlay key for layouts whose program does not expose it conventionally.</summary>
    private byte[]? _arm9OverlayHmacKey;
    /// <summary>Retains the late-DS program/overlay credential independently from the other HMAC keys.</summary>
    private byte[]? _dsProgramHmacKey;
    /// <summary>Retains the late-DS banner key without substituting either other HMAC credential.</summary>
    private byte[]? _dsBannerHmacKey;
    /// <summary>Retains explicitly selected late-DS RSA trust independently from DSi validation settings.</summary>
    private NdsDsiRsaPublicKey? _dsRsaPublicKey;
    /// <summary>Retains the immutable caller table used to distinguish and checksum KEY1 secure-area states.</summary>
    private NdsKey1KeyTable? _secureAreaKeyTable;
    /// <summary>Retains immutable caller trust material for DSi header authenticity checks.</summary>
    private NdsDsiRsaPublicKey? _dsiRsaPublicKey;

    /// <summary>Returns a fresh keyless policy suitable for structural validation without shared mutable state.</summary>
    public static NdsValidationOptions Default => new();

    /// <summary>
    /// Requests late-DS authentication checks and explicit missing-credential findings. Supplying any late-DS
    /// HMAC or RSA credential also enables these checks. The default keyless structural policy leaves them off.
    /// </summary>
    public bool ValidateDsAuthentication { get; init; }

    /// <summary>Copies the late-DS program/aggregate HMAC key and enables late-DS authentication validation.</summary>
    /// <param name="key">Non-empty caller credentials, distinct from the classic per-overlay key.</param>
    /// <returns>This validation policy.</returns>
    public NdsValidationOptions SetDsProgramHmacKey(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A late-DS program HMAC key cannot be empty.", nameof(key));
        }

        _dsProgramHmacKey = key.ToArray();
        return this;
    }

    /// <summary>Copies the separate late-DS banner HMAC key and enables late-DS authentication validation.</summary>
    /// <param name="key">Non-empty caller credentials for the banner field.</param>
    /// <returns>This validation policy.</returns>
    public NdsValidationOptions SetDsBannerHmacKey(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A late-DS banner HMAC key cannot be empty.", nameof(key));
        }

        _dsBannerHmacKey = key.ToArray();
        return this;
    }

    /// <summary>Selects an explicitly trusted late-DS RSA key and enables late-DS authentication validation.</summary>
    /// <param name="publicKey">Caller-trusted RSA-1024 parameters; no publisher key is inferred.</param>
    /// <returns>This validation policy.</returns>
    public NdsValidationOptions SetDsRsaPublicKey(NdsDsiRsaPublicKey publicKey)
    {
        _dsRsaPublicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
        return this;
    }

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
    /// Verifies classic-DS Download Play records with an explicit HMAC-SHA1 key instead of discovering a matching
    /// 64-byte key block inside decoded ARM9 bytes.
    /// </summary>
    /// <param name="key">Non-empty caller-owned key bytes, copied immediately.</param>
    /// <returns>The same options object for fluent validation configuration.</returns>
    public NdsValidationOptions SetArm9OverlayHmacKey(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("An ARM9 overlay validation HMAC key cannot be empty.", nameof(key));
        }

        _arm9OverlayHmacKey = key.ToArray();
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

    /// <summary>Exposes optional classic-DS overlay key bytes only to the format-specific validator.</summary>
    internal ReadOnlyMemory<byte> Arm9OverlayHmacKey => _arm9OverlayHmacKey;

    /// <summary>Enables checks only after the caller selects late-DS validation or supplies a related credential.</summary>
    internal bool RequiresDsAuthentication => ValidateDsAuthentication ||
        _dsProgramHmacKey is not null || _dsBannerHmacKey is not null || _dsRsaPublicKey is not null;

    /// <summary>Exposes the copied late-DS program/overlay credential only to integrity calculations.</summary>
    internal ReadOnlyMemory<byte> DsProgramHmacKey => _dsProgramHmacKey;

    /// <summary>Exposes the copied late-DS banner credential only to integrity calculations.</summary>
    internal ReadOnlyMemory<byte> DsBannerHmacKey => _dsBannerHmacKey;

    /// <summary>Exposes immutable explicitly selected late-DS RSA trust.</summary>
    internal NdsDsiRsaPublicKey? DsRsaPublicKey => _dsRsaPublicKey;

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
