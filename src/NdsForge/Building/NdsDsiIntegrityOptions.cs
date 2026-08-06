namespace NdsForge;

/// <summary>
/// Defines which DSi authentication fields a build can honestly produce. Retail RSA signing is deliberately
/// absent until a caller supplies an explicit signing key and trust policy; component HMACs can use either a
/// caller-owned key or the published ndstool homebrew compatibility key.
/// </summary>
public sealed class NdsDsiIntegrityOptions
{
    /// <summary>Retains a copied key so later caller buffer mutation cannot change a reviewed build recipe.</summary>
    private readonly byte[]? _hmacKey;

    /// <summary>Uses zeroed HMAC and signature fields for a clearly unauthenticated DSi image.</summary>
    public static NdsDsiIntegrityOptions Unauthenticated { get; } = new(null, NdsDsiSignatureMode.Cleared);

    /// <summary>
    /// Reproduces the public 64-byte HMAC-SHA1 key and no$gba development marker used by modern ndstool builds.
    /// This is a compatibility identity, not a claim that Nintendo hardware will trust the resulting image.
    /// </summary>
    public static NdsDsiIntegrityOptions NdstoolHomebrew { get; } = new(
        [
            0x21, 0x06, 0xC0, 0xDE, 0xBA, 0x98, 0xCE, 0x3F, 0xA6, 0x92, 0xE3, 0x9D, 0x46, 0xF2, 0xED, 0x01,
            0x76, 0xE3, 0xCC, 0x08, 0x56, 0x23, 0x63, 0xFA, 0xCA, 0xD4, 0xEC, 0xDF, 0x9A, 0x62, 0x78, 0x34,
            0x8F, 0x6D, 0x63, 0x3C, 0xFE, 0x22, 0xCA, 0x92, 0x20, 0x88, 0x97, 0x23, 0xD2, 0xCF, 0xAE, 0xC2,
            0x32, 0x67, 0x8D, 0xFE, 0xCA, 0x83, 0x64, 0x98, 0xAC, 0xFD, 0x3E, 0x37, 0x87, 0x46, 0x58, 0x24,
        ],
        NdsDsiSignatureMode.NoGbaDevelopmentMarker);

    /// <summary>Captures the selected key and development-signature behavior after public factory validation.</summary>
    /// <param name="hmacKey">Copied HMAC-SHA1 key, or <see langword="null"/> to clear keyed hashes.</param>
    /// <param name="signatureMode">Honest unsigned or explicitly non-RSA development marker behavior.</param>
    private NdsDsiIntegrityOptions(byte[]? hmacKey, NdsDsiSignatureMode signatureMode)
    {
        _hmacKey = hmacKey;
        SignatureMode = signatureMode;
    }

    /// <summary>
    /// Copies an application-supplied HMAC-SHA1 key without assigning it any implicit provenance. The same key
    /// must be supplied to validation when callers want authentication fields checked rather than merely parsed.
    /// </summary>
    /// <param name="key">Non-empty HMAC key bytes owned by the calling application.</param>
    /// <param name="signatureMode">Whether the unrelated 128-byte signature field stays clear or carries a development marker.</param>
    /// <returns>An immutable build policy independent from the source key buffer.</returns>
    public static NdsDsiIntegrityOptions CreateHmacSha1(
        ReadOnlySpan<byte> key,
        NdsDsiSignatureMode signatureMode = NdsDsiSignatureMode.Cleared)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A DSi HMAC-SHA1 key cannot be empty.", nameof(key));
        }

        return new(key.ToArray(), signatureMode);
    }

    /// <summary>Indicates whether component HMAC fields are recomputed instead of deliberately cleared.</summary>
    public bool ComputesHmacSha1 => _hmacKey is not null;

    /// <summary>Controls only the signature field and never upgrades a development marker into an authenticity claim.</summary>
    public NdsDsiSignatureMode SignatureMode { get; }

    /// <summary>Exposes copied key material only to the internal serializer performing the requested HMAC operation.</summary>
    internal ReadOnlyMemory<byte> HmacKey => _hmacKey;
}
