using System.Numerics;
using System.Security.Cryptography;

namespace NdsForge;

/// <summary>Applies caller-owned RSA private parameters to the cartridge's encoded digest with randomized blinding.</summary>
internal static class NdsRsaPrivateOperation
{
    /// <summary>Blinds the input and exponent, checks the result with the public key, then publishes a fixed-width signature.</summary>
    internal static void Sign(ReadOnlySpan<byte> encoded, RSAParameters parameters, Span<byte> destination)
    {
        var modulus = new BigInteger(parameters.Modulus!, isUnsigned: true, isBigEndian: true);
        var exponent = new BigInteger(parameters.Exponent!, isUnsigned: true, isBigEndian: true);
        var privateExponent = new BigInteger(parameters.D!, isUnsigned: true, isBigEndian: true);
        var firstPrime = new BigInteger(parameters.P!, isUnsigned: true, isBigEndian: true);
        var secondPrime = new BigInteger(parameters.Q!, isUnsigned: true, isBigEndian: true);
        var input = new BigInteger(encoded, isUnsigned: true, isBigEndian: true);
        if (encoded.Length != 128 || destination.Length != 128 || input >= modulus ||
            firstPrime * secondPrime != modulus)
        {
            throw new CryptographicException("The cartridge signing parameters or encoded digest are inconsistent.");
        }

        BigInteger factor = CreateBlindingFactor(modulus);
        BigInteger blindedInput = input * BigInteger.ModPow(factor, exponent, modulus) % modulus;
        Span<byte> random = stackalloc byte[32];
        RandomNumberGenerator.Fill(random);
        random[0] |= 0x80;
        var exponentFactor = new BigInteger(random, isUnsigned: true, isBigEndian: true);
        CryptographicOperations.ZeroMemory(random);
        BigInteger blindedExponent = privateExponent + exponentFactor * (firstPrime - 1) * (secondPrime - 1);
        BigInteger blindedResult = BigInteger.ModPow(blindedInput, blindedExponent, modulus);
        BigInteger signature = blindedResult * Invert(factor, modulus) % modulus;
        if (BigInteger.ModPow(signature, exponent, modulus) != input)
        {
            throw new CryptographicException("The cartridge RSA result failed its public-key consistency check.");
        }

        byte[] bytes = signature.ToByteArray(isUnsigned: true, isBigEndian: true);
        destination.Clear();
        bytes.CopyTo(destination[(128 - bytes.Length)..]);
    }

    /// <summary>Samples a fresh invertible input mask without accepting zero or a factor outside the modulus.</summary>
    private static BigInteger CreateBlindingFactor(BigInteger modulus)
    {
        Span<byte> random = stackalloc byte[128];
        for (int attempt = 0; attempt < 128; attempt++)
        {
            RandomNumberGenerator.Fill(random);
            var factor = new BigInteger(random, isUnsigned: true, isBigEndian: true);
            if (factor > BigInteger.One && factor < modulus && BigInteger.GreatestCommonDivisor(factor, modulus).IsOne)
            {
                CryptographicOperations.ZeroMemory(random);
                return factor;
            }
        }

        CryptographicOperations.ZeroMemory(random);
        throw new CryptographicException("A fresh RSA input-blinding factor could not be generated.");
    }

    /// <summary>Removes the random input mask with the extended Euclidean inverse over the public modulus.</summary>
    private static BigInteger Invert(BigInteger value, BigInteger modulus)
    {
        BigInteger remainder = modulus;
        BigInteger nextRemainder = value;
        BigInteger coefficient = BigInteger.Zero;
        BigInteger nextCoefficient = BigInteger.One;
        while (!nextRemainder.IsZero)
        {
            BigInteger quotient = remainder / nextRemainder;
            (remainder, nextRemainder) = (nextRemainder, remainder - quotient * nextRemainder);
            (coefficient, nextCoefficient) = (nextCoefficient, coefficient - quotient * nextCoefficient);
        }

        if (!remainder.IsOne)
        {
            throw new CryptographicException("The RSA input mask has no modular inverse.");
        }

        return (coefficient % modulus + modulus) % modulus;
    }
}
