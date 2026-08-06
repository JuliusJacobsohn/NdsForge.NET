namespace NdsForge;

/// <summary>
/// Captures the AES-128 normal key and both HMAC-derived initial counters needed to transform DSi modcrypt areas.
/// Every byte array is copied so a reviewed context cannot change when its source buffers are reused or cleared.
/// </summary>
public sealed class NdsModcryptContext
{
    /// <summary>Owns the exact AES-128 normal key used by both independently countered areas.</summary>
    private readonly byte[] _key;
    /// <summary>Owns the initial counter associated with area one and the ARM9 HMAC.</summary>
    private readonly byte[] _area1Counter;
    /// <summary>Owns the initial counter associated with area two and the ARM7 HMAC.</summary>
    private readonly byte[] _area2Counter;

    /// <summary>Copies already resolved key and counter material after enforcing the fixed AES block boundary.</summary>
    /// <param name="key">Exactly sixteen bytes containing an AES-128 normal key, not a KeyX or KeyY component.</param>
    /// <param name="area1Counter">Exactly sixteen initial counter bytes for the first area.</param>
    /// <param name="area2Counter">Exactly sixteen initial counter bytes for the second area.</param>
    /// <param name="keyMode">Provenance label retained for diagnostics and policy decisions.</param>
    public NdsModcryptContext(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> area1Counter,
        ReadOnlySpan<byte> area2Counter,
        NdsModcryptKeyMode keyMode = NdsModcryptKeyMode.SecureNormalKey)
    {
        ValidateBlock(key, nameof(key));
        ValidateBlock(area1Counter, nameof(area1Counter));
        ValidateBlock(area2Counter, nameof(area2Counter));
        if (!Enum.IsDefined(keyMode))
        {
            throw new ArgumentOutOfRangeException(nameof(keyMode), keyMode, "Unknown modcrypt key provenance.");
        }

        _key = key.ToArray();
        _area1Counter = area1Counter.ToArray();
        _area2Counter = area2Counter.ToArray();
        KeyMode = keyMode;
    }

    /// <summary>Preserves whether the normal key was public header data or supplied by an external secure-key authority.</summary>
    public NdsModcryptKeyMode KeyMode { get; }

    /// <summary>Returns an independent copy of the resolved AES key so callers can persist or clear it on their own terms.</summary>
    public ReadOnlyMemory<byte> ExportKey() => _key.ToArray();

    /// <summary>Returns an independent copy of the initial counter selected for one declared modcrypt area.</summary>
    /// <param name="area">Header area whose HMAC-derived counter is required.</param>
    /// <returns>Sixteen counter bytes safe for caller mutation.</returns>
    public ReadOnlyMemory<byte> ExportCounter(NdsModcryptArea area) => GetCounter(area).ToArray();

    /// <summary>
    /// Resolves public header-key mode automatically and otherwise derives the normal key from the public modcrypt
    /// KeyX recipe and the stored ARM9i HMAC KeyY bytes. The result makes no authentication claim about that HMAC.
    /// </summary>
    /// <param name="header">Parsed DSi-family header containing flags, title bytes, and HMAC counters.</param>
    /// <returns>A detached context that remains valid after the image is disposed.</returns>
    public static NdsModcryptContext FromHeader(NdsHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        NdsDsiHeader dsi = header.Dsi ??
            throw new ArgumentException("Modcrypt requires a DSi-enhanced or DSi-exclusive header.", nameof(header));
        if (dsi.UsesInsecureModcryptKey)
        {
            return new(
                header.RawData.Span[..16],
                dsi.Arm9Hmac.Span[..16],
                dsi.Arm7Hmac.Span[..16],
                NdsModcryptKeyMode.InsecureHeaderKey);
        }

        return new(
            NdsDsiKeyScrambler.DeriveModcryptNormalKey(header),
            dsi.Arm9Hmac.Span[..16],
            dsi.Arm7Hmac.Span[..16],
            NdsModcryptKeyMode.SecureNormalKey);
    }

    /// <summary>
    /// Uses an explicitly resolved secure normal key instead of applying the built-in modcrypt KeyX/KeyY recipe.
    /// This is intended for independently verified vectors and future variants; insecure headers reject overrides.
    /// </summary>
    /// <param name="header">Parsed DSi-family header supplying both area counters.</param>
    /// <param name="secureNormalKey">Exactly sixteen already scrambled normal-key bytes.</param>
    /// <returns>A detached context retaining a copy of every cryptographic input.</returns>
    public static NdsModcryptContext FromHeader(NdsHeader header, ReadOnlySpan<byte> secureNormalKey)
    {
        ArgumentNullException.ThrowIfNull(header);
        NdsDsiHeader dsi = header.Dsi ??
            throw new ArgumentException("Modcrypt requires a DSi-enhanced or DSi-exclusive header.", nameof(header));
        if (dsi.UsesInsecureModcryptKey)
        {
            throw new ArgumentException(
                "A header selecting its public modcrypt key cannot accept a secure-key override.",
                nameof(header));
        }

        ValidateBlock(secureNormalKey, nameof(secureNormalKey));
        return new(
            secureNormalKey,
            dsi.Arm9Hmac.Span[..16],
            dsi.Arm7Hmac.Span[..16],
            NdsModcryptKeyMode.SecureNormalKey);
    }

    /// <summary>Shares retained key bytes only with transforms that cannot mutate the underlying array.</summary>
    internal ReadOnlyMemory<byte> Key => _key;

    /// <summary>Selects one internally owned initial counter and rejects undefined enum values.</summary>
    /// <param name="area">Area identity from public API input.</param>
    /// <returns>The retained counter span used to initialize a transformation.</returns>
    internal ReadOnlyMemory<byte> GetCounter(NdsModcryptArea area) => area switch
    {
        NdsModcryptArea.First => _area1Counter,
        NdsModcryptArea.Second => _area2Counter,
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown modcrypt area."),
    };

    /// <summary>Enforces the AES-128 key and counter width before cryptographic state is retained.</summary>
    /// <param name="value">Candidate key or counter bytes.</param>
    /// <param name="parameterName">Public argument name reported to the caller.</param>
    private static void ValidateBlock(ReadOnlySpan<byte> value, string parameterName)
    {
        if (value.Length != NdsModcrypt.BlockSize)
        {
            throw new ArgumentException("A modcrypt key or counter must contain exactly sixteen bytes.", parameterName);
        }
    }
}
