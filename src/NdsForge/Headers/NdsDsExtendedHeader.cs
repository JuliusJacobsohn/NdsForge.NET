namespace NdsForge;

/// <summary>Models the authentication extension used by late-generation DS software on DSi systems.</summary>
public sealed class NdsDsExtendedHeader
{
    /// <summary>Retains bytes before the public extension because signature coverage begins at image offset zero.</summary>
    private readonly ReadOnlyMemory<byte> _rawHeader;

    /// <summary>Decodes extension fields from a complete 0x1000-byte late-DS header.</summary>
    /// <param name="rawHeader">Common header followed by the DSi-era authentication extension.</param>
    internal NdsDsExtendedHeader(ReadOnlyMemory<byte> rawHeader)
    {
        _rawHeader = rawHeader;
        ReadOnlySpan<byte> data = rawHeader.Span;
        RawData = rawHeader.Slice(0x200, 0xE00);
        Arm9ParametersOffset = NdsBinary.ReadUInt32(data, 0x88);
        Arm7ParametersOffset = NdsBinary.ReadUInt32(data, 0x8C);
        ProgramFeatures = (NdsProgramFeatures)data[0x1BF];
        BannerHmac = rawHeader.Slice(0x33C, 20);
        ProgramsHmac = rawHeader.Slice(0x378, 20);
        Arm9OverlaysHmac = rawHeader.Slice(0x38C, 20);
        RsaSignature = rawHeader.Slice(0xF80, 0x80);
    }

    /// <summary>Preserves bytes <c>0x200</c>-<c>0xFFF</c>, including reserved fields and stored authentication data.</summary>
    public ReadOnlyMemory<byte> RawData { get; }

    /// <summary>Gets the absolute image offset of the ARM9 SDK program-parameter table, or zero when absent.</summary>
    public uint Arm9ParametersOffset { get; }

    /// <summary>Gets the absolute image offset of the ARM7 SDK program-parameter table, or zero when absent.</summary>
    public uint Arm7ParametersOffset { get; }

    /// <summary>Interprets the shared feature byte that controls which late-DS authentication fields are meaningful.</summary>
    public NdsProgramFeatures ProgramFeatures { get; }

    /// <summary>Reports whether the header declares the phase-three banner HMAC.</summary>
    public bool HasBannerAuthentication =>
        (ProgramFeatures & NdsProgramFeatures.AuthenticatesBanner) != 0;

    /// <summary>Reports whether the header declares phase-one and phase-two HMACs and an RSA signature.</summary>
    public bool HasProgramAuthentication =>
        (ProgramFeatures & NdsProgramFeatures.AuthenticatesPrograms) != 0;

    /// <summary>Contains the stored phase-three banner HMAC without making a trust claim.</summary>
    public ReadOnlyMemory<byte> BannerHmac { get; }

    /// <summary>Contains the stored phase-one header, ARM9, and ARM7 HMAC without making a trust claim.</summary>
    public ReadOnlyMemory<byte> ProgramsHmac { get; }

    /// <summary>Contains the stored phase-two ARM9-overlay HMAC without making a trust claim.</summary>
    public ReadOnlyMemory<byte> Arm9OverlaysHmac { get; }

    /// <summary>Contains the 128-byte signature over header bytes <c>0x000</c>-<c>0xDFF</c>.</summary>
    public ReadOnlyMemory<byte> RsaSignature { get; }

    /// <summary>Verifies the stored signature against caller-selected RSA-1024 trust material.</summary>
    /// <param name="publicKey">Caller-trusted key; NdsForge does not bundle or infer one.</param>
    /// <returns><see langword="true"/> when the stored PKCS#1 v1.5 RSA-SHA1 value matches.</returns>
    public bool VerifyRsaSignature(NdsDsiRsaPublicKey publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        return publicKey.VerifyHeader(_rawHeader.Span[..0xE00], RsaSignature.Span);
    }
}
