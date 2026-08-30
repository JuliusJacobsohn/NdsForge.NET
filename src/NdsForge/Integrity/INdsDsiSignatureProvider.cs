namespace NdsForge;

/// <summary>
/// Abstracts DSi header signing so build pipelines may use a managed private key, hardware-backed key, or remote
/// signing authority without exposing private material to the builder. Implementations must produce the format's
/// 128-byte RSA-1024 type-one padded signature over the raw SHA-1 of the exact supplied 0xE00 bytes,
/// without the ASN.1 DigestInfo wrapper used by conventional PKCS#1 signing APIs.
/// </summary>
public interface INdsDsiSignatureProvider
{
    /// <summary>Signs a finalized DSi header prefix into fixed signature-field storage.</summary>
    /// <param name="signedHeader">Exactly bytes <c>0x000</c>-<c>0xDFF</c> in their final serialized form.</param>
    /// <param name="destination">Exactly 128 bytes corresponding to header offsets <c>0xF80</c>-<c>0xFFF</c>.</param>
    void SignHeader(ReadOnlySpan<byte> signedHeader, Span<byte> destination);
}
