namespace NdsForge;

/// <summary>Separates late-DS preservation, deliberate authentication removal, and keyed regeneration.</summary>
public sealed class NdsDsIntegrityOptions
{
    /// <summary>Owns an independent program/aggregate credential, never the ARM9 per-overlay key.</summary>
    private readonly byte[] _programKey;
    /// <summary>Owns an independent banner credential, which may differ from every other key.</summary>
    private readonly byte[] _bannerKey;

    /// <summary>Explicitly retains stored fields while reporting that they are unverified and may be stale.</summary>
    public static NdsDsIntegrityOptions PreserveStored { get; } = new(NdsDsAuthenticationWriteMode.PreserveStored);

    /// <summary>Explicitly removes late-DS HMACs, RSA bytes, and their declaration bits.</summary>
    public static NdsDsIntegrityOptions Unauthenticated { get; } = new(NdsDsAuthenticationWriteMode.Clear);

    /// <summary>Captures immutable credentials and caller-owned provider references after factory validation.</summary>
    private NdsDsIntegrityOptions(
        NdsDsAuthenticationWriteMode mode,
        ReadOnlySpan<byte> programKey = default,
        ReadOnlySpan<byte> bannerKey = default,
        NdsKey1KeyTable? secureAreaKeyTable = null,
        INdsDsiSignatureProvider? signatureProvider = null,
        NdsDsiRsaPublicKey? signaturePublicKey = null)
    {
        Mode = mode;
        _programKey = programKey.ToArray();
        _bannerKey = bannerKey.ToArray();
        SecureAreaKeyTable = secureAreaKeyTable;
        SignatureProvider = signatureProvider;
        SignaturePublicKey = signaturePublicKey;
    }

    /// <summary>Selects whether writes retain unverified bytes, remove authentication declarations, or regenerate keyed fields.</summary>
    public NdsDsAuthenticationWriteMode Mode { get; }

    /// <summary>
    /// Copies separate HMAC credentials for the declared fields. Program authentication also requires KEY1
    /// material. Without a signing provider, regenerated HMACs are accompanied by a cleared RSA field and warning.
    /// </summary>
    /// <param name="programKey">Program/aggregate key, or empty when only banner authentication is declared.</param>
    /// <param name="bannerKey">Banner key, or empty when banner authentication is not declared.</param>
    /// <param name="secureAreaKeyTable">KEY1 table required for canonical program authentication.</param>
    /// <param name="signatureProvider">Optional caller-owned native-format RSA signer, retained but never disposed by this policy.</param>
    /// <param name="signaturePublicKey">Required when a signer is supplied, so its output can be independently checked before publication.</param>
    /// <returns>An immutable regeneration policy whose HMAC arrays are copied.</returns>
    public static NdsDsIntegrityOptions CreateHmacSha1(
        ReadOnlySpan<byte> programKey,
        ReadOnlySpan<byte> bannerKey,
        NdsKey1KeyTable? secureAreaKeyTable = null,
        INdsDsiSignatureProvider? signatureProvider = null,
        NdsDsiRsaPublicKey? signaturePublicKey = null)
    {
        if (programKey.IsEmpty && bannerKey.IsEmpty)
        {
            throw new ArgumentException("Late-DS regeneration requires at least one explicit HMAC credential.");
        }

        if ((signatureProvider is null) != (signaturePublicKey is null))
        {
            throw new ArgumentException("A late-DS signing provider and its verification key must be supplied together.");
        }

        return new(NdsDsAuthenticationWriteMode.Regenerate, programKey, bannerKey,
            secureAreaKeyTable, signatureProvider, signaturePublicKey);
    }

    /// <summary>Exposes copied program credentials only to internal write and verification code.</summary>
    internal ReadOnlyMemory<byte> ProgramKey => _programKey;
    /// <summary>Exposes copied banner credentials only to internal write and verification code.</summary>
    internal ReadOnlyMemory<byte> BannerKey => _bannerKey;
    /// <summary>Retains immutable explicit KEY1 material for secure-area normalization.</summary>
    internal NdsKey1KeyTable? SecureAreaKeyTable { get; }
    /// <summary>Retains the caller's signing authority without transferring ownership.</summary>
    internal INdsDsiSignatureProvider? SignatureProvider { get; }
    /// <summary>Retains the explicit verification key for generated signatures.</summary>
    internal NdsDsiRsaPublicKey? SignaturePublicKey { get; }

    /// <summary>Rejects missing declared credentials before a destination is opened or truncated.</summary>
    internal void Validate(NdsProgramFeatures features, bool hasBanner)
    {
        if (Mode != NdsDsAuthenticationWriteMode.Regenerate)
        {
            return;
        }

        if ((features & NdsProgramFeatures.AuthenticatesPrograms) != 0 && (_programKey.Length == 0 || SecureAreaKeyTable is null))
        {
            throw new InvalidDataException("Late-DS program authentication requires a program/aggregate HMAC key and a KEY1 table.");
        }

        if ((features & NdsProgramFeatures.AuthenticatesBanner) != 0 && (_bannerKey.Length == 0 || !hasBanner))
        {
            throw new InvalidDataException("Late-DS banner authentication requires a separate banner HMAC key and a complete banner.");
        }
    }

    /// <summary>Builds validation trust from exactly the credentials used for generation.</summary>
    internal void ApplyValidation(NdsValidationOptions options)
    {
        if (Mode != NdsDsAuthenticationWriteMode.Regenerate)
        {
            return;
        }

        if (_programKey.Length != 0) { options.SetDsProgramHmacKey(_programKey); }
        if (_bannerKey.Length != 0) { options.SetDsBannerHmacKey(_bannerKey); }
        if (SecureAreaKeyTable is not null) { options.SetSecureAreaKeyTable(SecureAreaKeyTable); }
        if (SignaturePublicKey is not null) { options.SetDsRsaPublicKey(SignaturePublicKey); }
    }
}
