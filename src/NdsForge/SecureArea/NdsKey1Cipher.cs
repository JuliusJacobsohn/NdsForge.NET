using System.Buffers.Binary;

namespace NdsForge;

/// <summary>Implements the format-mandated KEY1 schedule and 64-bit block permutation over caller-supplied tables.</summary>
internal static class NdsKey1Cipher
{
    /// <summary>Plain first word written after the original secure identifier is deliberately destroyed.</summary>
    public const uint DestroyedId = 0xE7FFDEFF;
    /// <summary>First identifier word recovered only after both KEY1 initialization levels are reversed.</summary>
    private const uint SecureId0 = 0x72636E65;
    /// <summary>Second identifier word completing the little-endian ASCII <c>encryObj</c> marker.</summary>
    private const uint SecureId1 = 0x6A624F79;

    /// <summary>Encrypts a decrypted 16 KiB interval while changing only its first 2 KiB.</summary>
    /// <param name="area">Exact secure-area copy beginning with two destroyed-ID words.</param>
    /// <param name="gameCode">Four raw ASCII product-code bytes interpreted little-endian.</param>
    /// <param name="keyTable">Caller-owned unexpanded schedule.</param>
    /// <returns>An encrypted independent copy.</returns>
    public static byte[] Encrypt(ReadOnlySpan<byte> area, uint gameCode, NdsKey1KeyTable keyTable)
    {
        ValidateArea(area);
        if (ReadWord(area, 0) != DestroyedId || ReadWord(area, 4) != DestroyedId)
        {
            throw new InvalidDataException("Decrypted secure-area bytes must begin with two 0xE7FFDEFF marker words.");
        }

        byte[] output = area.ToArray();
        (uint[] words, uint[] argument) = InitializeLevel1(keyTable, gameCode);
        argument[1] = unchecked(argument[1] << 1);
        argument[2] >>= 1;
        InitializeLevel2(words, argument);
        for (int offset = 8; offset < 0x800; offset += 8)
        {
            EncryptStoredBlock(words, output, offset);
        }

        WriteWord(output, 0, SecureId0);
        WriteWord(output, 4, SecureId1);
        EncryptStoredBlock(words, output, 0);
        (words, _) = InitializeLevel1(keyTable, gameCode);
        EncryptStoredBlock(words, output, 0);
        return output;
    }

    /// <summary>Decrypts and verifies an encrypted interval while changing only its first 2 KiB.</summary>
    /// <param name="area">Exact encrypted 16 KiB interval.</param>
    /// <param name="gameCode">Four raw ASCII product-code bytes interpreted little-endian.</param>
    /// <param name="keyTable">Caller-owned unexpanded schedule.</param>
    /// <returns>A decrypted independent copy beginning with destroyed-ID markers.</returns>
    public static byte[] Decrypt(ReadOnlySpan<byte> area, uint gameCode, NdsKey1KeyTable keyTable)
    {
        ValidateArea(area);
        byte[] output = area.ToArray();
        (uint[] words, uint[] argument) = InitializeLevel1(keyTable, gameCode);
        DecryptStoredBlock(words, output, 0);
        argument[1] = unchecked(argument[1] << 1);
        argument[2] >>= 1;
        InitializeLevel2(words, argument);
        DecryptStoredBlock(words, output, 0);
        if (ReadWord(output, 0) != SecureId0 || ReadWord(output, 4) != SecureId1)
        {
            throw new InvalidDataException("The supplied KEY1 table and game code do not recover a secure-area identifier.");
        }

        WriteWord(output, 0, DestroyedId);
        WriteWord(output, 4, DestroyedId);
        for (int offset = 8; offset < 0x800; offset += 8)
        {
            DecryptStoredBlock(words, output, offset);
        }

        return output;
    }

    /// <summary>Copies and expands the seed table through the two repeated level-one passes.</summary>
    /// <param name="keyTable">Immutable caller-supplied seed words.</param>
    /// <param name="gameCode">Little-endian product code.</param>
    /// <returns>The expanded words and still-live three-word initialization argument.</returns>
    private static (uint[] Words, uint[] Argument) InitializeLevel1(NdsKey1KeyTable keyTable, uint gameCode)
    {
        uint[] words = keyTable.CreateWorkingWords();
        uint[] argument = [gameCode, gameCode >> 1, unchecked(gameCode << 1)];
        InitializeLevel2(words, argument);
        InitializeLevel2(words, argument);
        return (words, argument);
    }

    /// <summary>Mutates one schedule using two encrypted argument pairs followed by full Blowfish-style expansion.</summary>
    /// <param name="words">Round and substitution words updated in place.</param>
    /// <param name="argument">Three game-code-derived words whose first eight memory bytes key the schedule.</param>
    private static void InitializeLevel2(uint[] words, uint[] argument)
    {
        EncryptBlock(words, ref argument[2], ref argument[1]);
        EncryptBlock(words, ref argument[1], ref argument[0]);
        UpdateSchedule(words, argument);
    }

    /// <summary>Applies the repeated eight-byte game-code key and regenerates every schedule word.</summary>
    /// <param name="words">Mutable 0x412-word schedule.</param>
    /// <param name="argument">Initialization words represented in platform-independent little-endian order.</param>
    private static void UpdateSchedule(uint[] words, uint[] argument)
    {
        Span<byte> argumentBytes = stackalloc byte[12];
        for (int index = 0; index < argument.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(argumentBytes[(index * 4)..], argument[index]);
        }

        for (int round = 0; round < 18; round++)
        {
            uint value = 0;
            for (int index = 0; index < 4; index++)
            {
                value = unchecked((value << 8) | argumentBytes[((round * 4) + index) & 7]);
            }

            words[round] ^= value;
        }

        uint first = 0;
        uint second = 0;
        for (int index = 0; index < words.Length; index += 2)
        {
            EncryptBlock(words, ref first, ref second);
            words[index] = first;
            words[index + 1] = second;
        }
    }

    /// <summary>Encrypts one pair using sixteen Feistel rounds and the schedule's native argument ordering.</summary>
    /// <param name="words">Expanded round and substitution words.</param>
    /// <param name="first">First algorithm argument updated in place.</param>
    /// <param name="second">Second algorithm argument updated in place.</param>
    private static void EncryptBlock(uint[] words, ref uint first, ref uint second)
    {
        uint a = first;
        uint b = second;
        for (int round = 0; round < 16; round++)
        {
            uint c = words[round] ^ a;
            a = b ^ Lookup(words, c);
            b = c;
        }

        second = a ^ words[16];
        first = b ^ words[17];
    }

    /// <summary>Reverses one pair using the round words in descending order.</summary>
    /// <param name="words">Expanded round and substitution words.</param>
    /// <param name="first">First algorithm argument updated in place.</param>
    /// <param name="second">Second algorithm argument updated in place.</param>
    private static void DecryptBlock(uint[] words, ref uint first, ref uint second)
    {
        uint a = first;
        uint b = second;
        for (int round = 17; round > 1; round--)
        {
            uint c = words[round] ^ a;
            a = b ^ Lookup(words, c);
            b = c;
        }

        first = b ^ words[0];
        second = a ^ words[1];
    }

    /// <summary>Combines four schedule-box lookups under the DS KEY1 arithmetic expression.</summary>
    /// <param name="words">Expanded table containing all four boxes after the 18 round words.</param>
    /// <param name="value">Round value whose bytes select box entries from most to least significant.</param>
    /// <returns>The unchecked 32-bit nonlinear result.</returns>
    private static uint Lookup(uint[] words, uint value)
    {
        uint a = words[18 + (value >> 24)];
        uint b = words[18 + 256 + ((value >> 16) & 0xFF)];
        uint c = words[18 + 512 + ((value >> 8) & 0xFF)];
        uint d = words[18 + 768 + (value & 0xFF)];
        return unchecked(d + (c ^ (b + a)));
    }

    /// <summary>Adapts two little-endian stored words to the algorithm's reversed pointer order.</summary>
    /// <param name="words">Expanded schedule.</param>
    /// <param name="data">Mutable secure-area copy.</param>
    /// <param name="offset">Eight-byte-aligned block offset.</param>
    private static void EncryptStoredBlock(uint[] words, Span<byte> data, int offset)
    {
        uint first = ReadWord(data, offset + 4);
        uint second = ReadWord(data, offset);
        EncryptBlock(words, ref first, ref second);
        WriteWord(data, offset + 4, first);
        WriteWord(data, offset, second);
    }

    /// <summary>Adapts one stored block to inverse algorithm pointer order.</summary>
    /// <param name="words">Expanded schedule.</param>
    /// <param name="data">Mutable secure-area copy.</param>
    /// <param name="offset">Eight-byte-aligned block offset.</param>
    private static void DecryptStoredBlock(uint[] words, Span<byte> data, int offset)
    {
        uint first = ReadWord(data, offset + 4);
        uint second = ReadWord(data, offset);
        DecryptBlock(words, ref first, ref second);
        WriteWord(data, offset + 4, first);
        WriteWord(data, offset, second);
    }

    /// <summary>Reads one native KEY1 word explicitly as little-endian.</summary>
    private static uint ReadWord(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    /// <summary>Writes one native KEY1 word explicitly as little-endian.</summary>
    private static void WriteWord(Span<byte> data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);

    /// <summary>Requires the complete CRC-covered 16 KiB interval even though only the first 2 KiB are transformed.</summary>
    private static void ValidateArea(ReadOnlySpan<byte> area)
    {
        if (area.Length != NdsSecureArea.ByteLength)
        {
            throw new ArgumentException($"A secure area must contain exactly 0x{NdsSecureArea.ByteLength:X} bytes.", nameof(area));
        }
    }
}
