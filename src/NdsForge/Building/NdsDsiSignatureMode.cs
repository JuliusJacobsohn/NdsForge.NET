namespace NdsForge;

/// <summary>Selects how a DSi build populates the 128-byte signature field under an explicit authenticity policy.</summary>
public enum NdsDsiSignatureMode
{
    /// <summary>Leaves all signature bytes zero so consumers cannot mistake the image for authenticated retail content.</summary>
    Cleared,

    /// <summary>
    /// Writes the development marker used by ndstool and no$gba: fixed framing bytes plus SHA-1 of header bytes
    /// <c>0x000</c>-<c>0xDFF</c>. This aids homebrew interoperability but is not an RSA signature.
    /// </summary>
    NoGbaDevelopmentMarker,

    /// <summary>Uses an application-supplied provider to produce a real RSA-1024 SHA-1 PKCS#1 v1.5 signature.</summary>
    RsaSha1,
}
