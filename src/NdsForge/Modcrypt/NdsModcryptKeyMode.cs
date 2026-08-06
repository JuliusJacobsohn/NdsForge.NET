namespace NdsForge;

/// <summary>Records whether a modcrypt context came from public header bytes or externally derived secure material.</summary>
public enum NdsModcryptKeyMode
{
    /// <summary>The first sixteen header bytes are the normal AES key because either insecure-key flag is set.</summary>
    InsecureHeaderKey,

    /// <summary>The AES normal key was supplied explicitly after any platform-specific key scrambling occurred elsewhere.</summary>
    SecureNormalKey,
}
