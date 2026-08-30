using System.Numerics;
using System.Security.Cryptography;

namespace NdsForge;

/// <summary>
/// Represents one explicitly trusted RSA-1024 public key for DSi header authentication. Trust is supplied by the
/// caller; constructing this value does not imply that a modulus belongs to Nintendo or any particular publisher.
/// </summary>
public sealed class NdsDsiRsaPublicKey
{
    /// <summary>Retains an independent big-endian modulus copy for per-call public arithmetic.</summary>
    private readonly byte[] _modulus;
    /// <summary>Retains the caller's positive big-endian public exponent without assuming 65537.</summary>
    private readonly byte[] _exponent;

    /// <summary>Copies raw parameters so trust configuration remains stable after caller buffers are reused or cleared.</summary>
    /// <param name="modulus">Exactly 128 bytes forming a 1024-bit modulus.</param>
    /// <param name="exponent">Non-empty positive public exponent, commonly <c>01 00 01</c>.</param>
    public NdsDsiRsaPublicKey(ReadOnlySpan<byte> modulus, ReadOnlySpan<byte> exponent)
    {
        if (modulus.Length != 128 || exponent.IsEmpty || exponent.Length > 128)
        {
            throw new ArgumentException("A DSi RSA key requires a 128-byte modulus and a non-empty exponent.");
        }

        var modulusValue = new BigInteger(modulus, isUnsigned: true, isBigEndian: true);
        var exponentValue = new BigInteger(exponent, isUnsigned: true, isBigEndian: true);
        if ((modulus[0] & 0x80) == 0 || modulusValue.IsEven || exponentValue.IsEven ||
            exponentValue <= BigInteger.One || exponentValue >= modulusValue)
        {
            throw new ArgumentException("A cartridge RSA key requires an odd 1024-bit modulus and a valid odd public exponent.");
        }

        _modulus = modulus.ToArray();
        _exponent = exponent.ToArray();
    }

    /// <summary>Snapshots the public portion of an existing RSA object after enforcing the DSi key size.</summary>
    /// <param name="rsa">RSA instance whose ownership and lifetime remain with the caller.</param>
    /// <returns>An independent immutable public-key value.</returns>
    public static NdsDsiRsaPublicKey FromRsa(RSA rsa)
    {
        ArgumentNullException.ThrowIfNull(rsa);
        if (rsa.KeySize != 1024)
        {
            throw new ArgumentException("DSi header signatures require a 1024-bit RSA key.", nameof(rsa));
        }

        RSAParameters parameters = rsa.ExportParameters(includePrivateParameters: false);
        return new(parameters.Modulus!, parameters.Exponent!);
    }

    /// <summary>Exports the conventional big-endian modulus without exposing internal mutable storage.</summary>
    public ReadOnlyMemory<byte> Modulus => _modulus.ToArray();

    /// <summary>Exports the conventional big-endian public exponent without exposing internal mutable storage.</summary>
    public ReadOnlyMemory<byte> Exponent => _exponent.ToArray();

    /// <summary>Verifies the cartridge's RSA type-one padded raw SHA-1 signature, without an ASN.1 DigestInfo wrapper.</summary>
    /// <param name="signedHeader">Exactly 0xE00 bytes from header offset zero.</param>
    /// <param name="signature">Exactly 128 on-disk signature bytes.</param>
    /// <returns><see langword="true"/> only when the signature matches this caller-trusted key.</returns>
    public bool VerifyHeader(ReadOnlySpan<byte> signedHeader, ReadOnlySpan<byte> signature)
    {
        ValidateBuffers(signedHeader, signature);
        var value = new BigInteger(signature, isUnsigned: true, isBigEndian: true);
        var modulus = new BigInteger(_modulus, isUnsigned: true, isBigEndian: true);
        if (value >= modulus)
        {
            return false;
        }

        var exponent = new BigInteger(_exponent, isUnsigned: true, isBigEndian: true);
        byte[] recovered = BigInteger.ModPow(value, exponent, modulus).ToByteArray(isUnsigned: true, isBigEndian: true);
        Span<byte> padded = stackalloc byte[128];
        padded.Clear();
        recovered.CopyTo(padded[(128 - recovered.Length)..]);
        return CryptographicOperations.FixedTimeEquals(padded, NdsRsaEncodedMessage.Create(signedHeader));
    }

    /// <summary>Applies exact structure sizes before decoding the signature.</summary>
    /// <param name="signedHeader">Candidate signed prefix.</param>
    /// <param name="signature">Candidate RSA field.</param>
    private static void ValidateBuffers(ReadOnlySpan<byte> signedHeader, ReadOnlySpan<byte> signature)
    {
        if (signedHeader.Length != 0xE00 || signature.Length != 128)
        {
            throw new ArgumentException("DSi RSA verification requires a 0xE00-byte header prefix and 128-byte signature.");
        }
    }
}
