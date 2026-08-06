using System.Buffers.Binary;
using System.Text;

namespace NdsForge;

/// <summary>
/// Provides pure inspection and KEY1 transformations for the conventional <c>0x4000</c>-<c>0x7FFF</c> cartridge
/// interval. Operations return new buffers and never rewrite a source image, header checksum, or caller key table.
/// </summary>
public static class NdsSecureArea
{
    /// <summary>Absolute image offset at which the cartridge security interval begins.</summary>
    public const int Offset = 0x4000;
    /// <summary>CRC-covered interval length; KEY1 itself transforms only the first <c>0x800</c> bytes.</summary>
    public const int ByteLength = 0x4000;

    /// <summary>Inspects a loaded image and verifies encrypted state or CRC only when enough explicit key material exists.</summary>
    /// <param name="image">Read-only image supplying header metadata and exactly bounded bytes.</param>
    /// <param name="keyTable">Optional KEY1 schedule used to recognize encrypted content and reconstruct decrypted CRC input.</param>
    /// <returns>A state and checksum report; malformed data is represented without mutating or repairing it.</returns>
    public static NdsSecureAreaInspection Inspect(NdsImage image, NdsKey1KeyTable? keyTable = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Header.Arm9.Data.Offset < Offset)
        {
            return new(NdsSecureAreaState.Absent, default, image.Header.SecureAreaCrc, null);
        }

        var region = new NdsRegion(Offset, ByteLength);
        if (image.Length < region.End)
        {
            return new(NdsSecureAreaState.Malformed, region, image.Header.SecureAreaCrc, null);
        }

        var data = new byte[ByteLength];
        using Stream stream = image.OpenRead(region);
        stream.ReadExactly(data);
        return Inspect(data, image.Header.GameCode, image.Header.SecureAreaCrc, keyTable);
    }

    /// <summary>Classifies one isolated interval using caller-supplied identity and stored checksum context.</summary>
    /// <param name="area">Exactly 16 KiB beginning at image offset <c>0x4000</c>.</param>
    /// <param name="gameCode">Exactly four ASCII bytes used by KEY1 initialization.</param>
    /// <param name="storedCrc">Header checksum to compare with the encrypted representation.</param>
    /// <param name="keyTable">Optional schedule enabling cryptographic recognition.</param>
    /// <returns>A state and nullable checksum conclusion.</returns>
    public static NdsSecureAreaInspection Inspect(
        ReadOnlySpan<byte> area,
        string gameCode,
        ushort storedCrc,
        NdsKey1KeyTable? keyTable = null)
    {
        ValidateArea(area);
        uint first = BinaryPrimitives.ReadUInt32LittleEndian(area);
        uint second = BinaryPrimitives.ReadUInt32LittleEndian(area[4..]);
        var region = new NdsRegion(Offset, ByteLength);
        if (first == 0 && second == 0)
        {
            return new(NdsSecureAreaState.Multiboot, region, storedCrc, NdsChecksums.ComputeCrc16(area));
        }

        if (first == NdsKey1Cipher.DestroyedId && second == NdsKey1Cipher.DestroyedId)
        {
            ushort? calculated = keyTable is null
                ? null
                : NdsChecksums.ComputeCrc16(NdsKey1Cipher.Encrypt(area, ParseGameCode(gameCode), keyTable));
            return new(NdsSecureAreaState.Decrypted, region, storedCrc, calculated);
        }

        if (keyTable is not null)
        {
            try
            {
                _ = NdsKey1Cipher.Decrypt(area, ParseGameCode(gameCode), keyTable);
                return new(
                    NdsSecureAreaState.Encrypted,
                    region,
                    storedCrc,
                    NdsChecksums.ComputeCrc16(area));
            }
            catch (InvalidDataException)
            {
                // Failed identifier recovery is an inspection result rather than an exceptional control path.
            }
        }

        return new(
            NdsSecureAreaState.Unrecognized,
            region,
            storedCrc,
            keyTable is null ? null : NdsChecksums.ComputeCrc16(area));
    }

    /// <summary>Encrypts a decrypted interval for the supplied product identity and returns an independent copy.</summary>
    /// <param name="area">Exactly 16 KiB beginning with two destroyed-ID marker words.</param>
    /// <param name="gameCode">Exactly four ASCII product-code bytes.</param>
    /// <param name="keyTable">Explicit seed schedule from an authorized source.</param>
    /// <returns>Encrypted bytes suitable for writing at image offset <c>0x4000</c>.</returns>
    public static byte[] Encrypt(ReadOnlySpan<byte> area, string gameCode, NdsKey1KeyTable keyTable)
    {
        ArgumentNullException.ThrowIfNull(keyTable);
        return NdsKey1Cipher.Encrypt(area, ParseGameCode(gameCode), keyTable);
    }

    /// <summary>Decrypts a key-verifiable interval and replaces its secure identifier with conventional destroyed markers.</summary>
    /// <param name="area">Exactly 16 KiB whose first 2 KiB use KEY1.</param>
    /// <param name="gameCode">Exactly four ASCII product-code bytes.</param>
    /// <param name="keyTable">Explicit seed schedule from an authorized source.</param>
    /// <returns>Decrypted bytes; the last 14 KiB remain byte-identical to input.</returns>
    public static byte[] Decrypt(ReadOnlySpan<byte> area, string gameCode, NdsKey1KeyTable keyTable)
    {
        ArgumentNullException.ThrowIfNull(keyTable);
        return NdsKey1Cipher.Decrypt(area, ParseGameCode(gameCode), keyTable);
    }

    /// <summary>
    /// Reads one interval at the source's current position, encrypts it, and writes exactly 16 KiB without closing
    /// either caller-owned stream. Buffering the fixed-size area prevents a partially transformed destination when
    /// the source is truncated or the identifier is invalid.
    /// </summary>
    /// <param name="source">Readable stream positioned at decrypted secure-area byte zero.</param>
    /// <param name="destination">Writable stream receiving encrypted bytes at its current position.</param>
    /// <param name="gameCode">Exactly four ASCII product-code bytes.</param>
    /// <param name="keyTable">Explicit seed schedule from an authorized source.</param>
    /// <param name="cancellationToken">Cancels input and output I/O.</param>
    public static ValueTask EncryptAsync(
        Stream source,
        Stream destination,
        string gameCode,
        NdsKey1KeyTable keyTable,
        CancellationToken cancellationToken = default) =>
        TransformAsync(source, destination, gameCode, keyTable, encrypt: true, cancellationToken);

    /// <summary>
    /// Reads one encrypted interval and emits a verified decrypted copy without closing caller-owned streams. No
    /// destination bytes are written unless both KEY1 initialization levels recover the secure identifier.
    /// </summary>
    /// <param name="source">Readable stream positioned at encrypted secure-area byte zero.</param>
    /// <param name="destination">Writable stream receiving decrypted bytes at its current position.</param>
    /// <param name="gameCode">Exactly four ASCII product-code bytes.</param>
    /// <param name="keyTable">Explicit seed schedule from an authorized source.</param>
    /// <param name="cancellationToken">Cancels input and output I/O.</param>
    public static ValueTask DecryptAsync(
        Stream source,
        Stream destination,
        string gameCode,
        NdsKey1KeyTable keyTable,
        CancellationToken cancellationToken = default) =>
        TransformAsync(source, destination, gameCode, keyTable, encrypt: false, cancellationToken);

    /// <summary>Shares bounded stream validation and mutation ordering between asynchronous transformations.</summary>
    /// <param name="source">Caller-owned readable input.</param>
    /// <param name="destination">Caller-owned writable output.</param>
    /// <param name="gameCode">KEY1 product identity.</param>
    /// <param name="keyTable">Explicit seed schedule.</param>
    /// <param name="encrypt">Selects forward encryption when true and verified decryption otherwise.</param>
    /// <param name="cancellationToken">Cancels reads or writes.</param>
    private static async ValueTask TransformAsync(
        Stream source,
        Stream destination,
        string gameCode,
        NdsKey1KeyTable keyTable,
        bool encrypt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!source.CanRead)
        {
            throw new ArgumentException("The secure-area source must be readable.", nameof(source));
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The secure-area destination must be writable.", nameof(destination));
        }

        var input = new byte[ByteLength];
        await source.ReadExactlyAsync(input, cancellationToken).ConfigureAwait(false);
        byte[] output = encrypt
            ? Encrypt(input, gameCode, keyTable)
            : Decrypt(input, gameCode, keyTable);
        await destination.WriteAsync(output, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Converts four strict ASCII code units to the little-endian word used by KEY1 initialization.</summary>
    /// <param name="gameCode">Header identity, commonly three title characters plus a region suffix.</param>
    /// <returns>The raw four-byte word with no case normalization.</returns>
    private static uint ParseGameCode(string gameCode)
    {
        ArgumentNullException.ThrowIfNull(gameCode);
        if (gameCode.Length != 4 || gameCode.Any(static value => value is < ' ' or > '~'))
        {
            throw new ArgumentException("A KEY1 game code must contain exactly four printable ASCII characters.", nameof(gameCode));
        }

        Span<byte> bytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(gameCode, bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    /// <summary>Rejects partial buffers before any marker or checksum interpretation.</summary>
    private static void ValidateArea(ReadOnlySpan<byte> area)
    {
        if (area.Length != ByteLength)
        {
            throw new ArgumentException($"A secure area must contain exactly 0x{ByteLength:X} bytes.", nameof(area));
        }
    }
}
