namespace NdsForge;

/// <summary>Identifies defined DSi execution and modcrypt bits in common-header byte <c>0x1C</c>.</summary>
[Flags]
public enum NdsDsiCryptoPolicy
{
    /// <summary>No defined DSi execution or cryptography bit is set.</summary>
    None = 0,

    /// <summary>The image contains a DSi-exclusive execution region.</summary>
    HasDsiRegion = 1 << 0,

    /// <summary>At least one header-declared area uses modcrypt.</summary>
    UsesModcrypt = 1 << 1,

    /// <summary>Modcrypt uses the public development-key derivation.</summary>
    UsesDevelopmentModcryptKey = 1 << 2,

    /// <summary>The header requests disabled debugging behavior.</summary>
    DisablesDebugging = 1 << 3,
}
