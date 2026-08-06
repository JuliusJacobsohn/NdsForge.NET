using System.Text;

namespace NdsForge;

/// <summary>
/// Implements the DSi 128-bit KeyX/KeyY scrambler in cartridge-register byte order. It contains only the public
/// arithmetic and modcrypt slot prefix; callers remain responsible for the provenance of arbitrary key components.
/// </summary>
public static class NdsDsiKeyScrambler
{
    /// <summary>Addition constant applied after XOR and before the 42-bit rotation, stored in conventional display order.</summary>
    private static readonly byte[] ScramblerConstant =
        Convert.FromHexString("FFFEFB4E295902582A680F5F1A4F3E79");

    /// <summary>
    /// Derives the normal AES key produced by DSi hardware from one KeyX/KeyY pair. Input and output arrays use the
    /// little-endian register order found in cartridge headers and hardware-facing documentation.
    /// </summary>
    /// <param name="keyX">Exactly sixteen bytes of slot-specific KeyX material.</param>
    /// <param name="keyY">Exactly sixteen bytes of title- or content-specific KeyY material.</param>
    /// <returns>A new sixteen-byte normal key; neither component is retained or modified.</returns>
    public static byte[] DeriveNormalKey(ReadOnlySpan<byte> keyX, ReadOnlySpan<byte> keyY)
    {
        ValidateKey(keyX, nameof(keyX));
        ValidateKey(keyY, nameof(keyY));
        Span<byte> sum = stackalloc byte[16];
        int carry = 0;
        for (int index = 0; index < sum.Length; index++)
        {
            int value = (keyX[index] ^ keyY[index]) + ScramblerConstant[^(index + 1)] + carry;
            sum[index] = (byte)value;
            carry = value >> 8;
        }

        return RotateLeft(sum, 42);
    }

    /// <summary>
    /// Constructs the modcrypt slot's public KeyX from its fixed eight-byte platform prefix followed by the game
    /// code in forward and reverse order. No case folding is performed because product codes are binary identity.
    /// </summary>
    /// <param name="gameCode">Exactly four printable ASCII characters from common header offset <c>0x0C</c>.</param>
    /// <returns>A new sixteen-byte KeyX in DSi register order.</returns>
    public static byte[] CreateModcryptKeyX(string gameCode)
    {
        ArgumentNullException.ThrowIfNull(gameCode);
        if (gameCode.Length != 4 || gameCode.Any(static value => value is < ' ' or > '~'))
        {
            throw new ArgumentException("A modcrypt game code must contain exactly four printable ASCII characters.", nameof(gameCode));
        }

        var keyX = new byte[16];
        "Nintendo"u8.CopyTo(keyX);
        Encoding.ASCII.GetBytes(gameCode, keyX.AsSpan(8, 4));
        for (int index = 0; index < 4; index++)
        {
            keyX[12 + index] = keyX[11 - index];
        }

        return keyX;
    }

    /// <summary>
    /// Derives the secure modcrypt normal key from the common game code and the first sixteen ARM9i HMAC bytes.
    /// The HMAC is consumed as stored; this operation does not establish that it was authenticated by a trusted key.
    /// </summary>
    /// <param name="header">Parsed DSi-family header supplying both public KeyX identity and KeyY bytes.</param>
    /// <returns>A new sixteen-byte normal key suitable for <see cref="NdsModcryptContext"/>.</returns>
    public static byte[] DeriveModcryptNormalKey(NdsHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        NdsDsiHeader dsi = header.Dsi ??
            throw new ArgumentException("Secure modcrypt key derivation requires a DSi-family header.", nameof(header));
        return DeriveNormalKey(CreateModcryptKeyX(header.GameCode), dsi.Arm9iHmac.Span[..16]);
    }

    /// <summary>Rotates one 128-bit little-endian value left without relying on machine-width overflow behavior.</summary>
    /// <param name="value">Exactly sixteen bytes of intermediate scrambler state.</param>
    /// <param name="bits">Rotation distance from the hardware algorithm.</param>
    /// <returns>A separately allocated rotated value.</returns>
    private static byte[] RotateLeft(ReadOnlySpan<byte> value, int bits)
    {
        int byteShift = bits / 8;
        int bitShift = bits % 8;
        var output = new byte[16];
        for (int index = 0; index < output.Length; index++)
        {
            int lowIndex = (index - byteShift) & 15;
            int highIndex = (lowIndex - 1) & 15;
            output[index] = (byte)((value[lowIndex] << bitShift) | (value[highIndex] >> (8 - bitShift)));
        }

        return output;
    }

    /// <summary>Enforces fixed 128-bit components before arithmetic can silently truncate malformed material.</summary>
    /// <param name="value">Candidate key component.</param>
    /// <param name="parameterName">Caller-facing argument name used by the exception.</param>
    private static void ValidateKey(ReadOnlySpan<byte> value, string parameterName)
    {
        if (value.Length != 16)
        {
            throw new ArgumentException("A DSi scrambler key component must contain exactly sixteen bytes.", parameterName);
        }
    }
}
