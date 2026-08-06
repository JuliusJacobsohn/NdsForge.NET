using System.Security.Cryptography;

namespace NdsForge;

/// <summary>
/// Snapshots a managed RSA-1024 private key for deterministic DSi header signing. Dispose the provider after its
/// build recipes finish so copied private parameters are cleared; the source <see cref="RSA"/> remains caller-owned.
/// </summary>
public sealed class NdsDsiRsaSignatureProvider : INdsDsiSignatureProvider, IDisposable
{
    /// <summary>Contains independent private parameters until disposal clears every component array.</summary>
    private RSAParameters _parameters;
    /// <summary>Prevents signing after private material has been erased.</summary>
    private bool _disposed;

    /// <summary>Copies a complete DSi-sized private key rather than retaining the caller's cryptographic object.</summary>
    /// <param name="rsa">RSA-1024 instance capable of exporting private parameters.</param>
    public NdsDsiRsaSignatureProvider(RSA rsa)
    {
        ArgumentNullException.ThrowIfNull(rsa);
        if (rsa.KeySize != 1024)
        {
            throw new ArgumentException("DSi header signatures require a 1024-bit RSA key.", nameof(rsa));
        }

        _parameters = rsa.ExportParameters(includePrivateParameters: true);
        if (_parameters.D is null)
        {
            throw new ArgumentException("The RSA object does not expose a private signing key.", nameof(rsa));
        }
    }

    /// <inheritdoc />
    public void SignHeader(ReadOnlySpan<byte> signedHeader, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (signedHeader.Length != 0xE00 || destination.Length != 128)
        {
            throw new ArgumentException("DSi signing requires a 0xE00-byte header prefix and 128-byte destination.");
        }

        using RSA rsa = RSA.Create();
        rsa.ImportParameters(_parameters);
#pragma warning disable CA5350, CA5387 // The legacy DSi signature format fixes SHA-1 and PKCS#1 v1.5.
        if (!rsa.TrySignData(signedHeader, destination, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1, out int written) ||
            written != destination.Length)
#pragma warning restore CA5350, CA5387
        {
            throw new CryptographicException("The RSA provider did not produce the required 128-byte DSi signature.");
        }
    }

    /// <summary>Clears all copied private and public RSA components and permanently disables signing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear(_parameters.D);
        Clear(_parameters.DP);
        Clear(_parameters.DQ);
        Clear(_parameters.Exponent);
        Clear(_parameters.InverseQ);
        Clear(_parameters.Modulus);
        Clear(_parameters.P);
        Clear(_parameters.Q);
        _parameters = default;
        _disposed = true;
    }

    /// <summary>Erases one optional RSA component using a primitive resistant to dead-store removal.</summary>
    /// <param name="value">Parameter array or <see langword="null"/> when absent.</param>
    private static void Clear(byte[]? value)
    {
        if (value is not null)
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }
}
