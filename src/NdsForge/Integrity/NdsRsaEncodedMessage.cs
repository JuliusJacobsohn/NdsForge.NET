using System.Security.Cryptography;

namespace NdsForge;

/// <summary>Encodes the cartridge's raw SHA-1 digest in a fixed-width RSA type-one block without ASN.1 metadata.</summary>
internal static class NdsRsaEncodedMessage
{
    /// <summary>Constructs the exact 128-byte representative from the signed 0xE00-byte header prefix.</summary>
    internal static byte[] Create(ReadOnlySpan<byte> header)
    {
        if (header.Length != 0xE00)
        {
            throw new ArgumentException("A cartridge RSA input requires exactly 0xE00 header bytes.", nameof(header));
        }

        byte[] encoded = new byte[128];
        encoded[1] = 1;
        encoded.AsSpan(2, 105).Fill(0xFF);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(header);
        if (!hash.TryGetHashAndReset(encoded.AsSpan(108), out int written) || written != 20)
        {
            throw new CryptographicException("The cartridge digest must contain exactly twenty bytes.");
        }

        return encoded;
    }
}
