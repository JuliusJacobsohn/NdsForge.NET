using System.Security.Cryptography;

#pragma warning disable CA5358 // CTR mode requires raw AES block encryption; ECB is not used to encrypt payload blocks.

namespace NdsForge;

/// <summary>Maintains one forward-only AES-CTR cursor so large streams need neither alignment nor whole-file buffering.</summary>
internal sealed class NdsModcryptTransform : IDisposable
{
    /// <summary>Owns the configured platform AES primitive for the cursor lifetime.</summary>
    private readonly Aes _aes;
    /// <summary>Encrypts successive counter blocks without allocating a new transform per block.</summary>
    private readonly ICryptoTransform _encryptor;
    /// <summary>Owns the byte-reversed AES-provider key required by the DSi register representation.</summary>
    private readonly byte[] _aesKey;
    /// <summary>Tracks the next little-endian counter value, matching DSi modcrypt byte ordering.</summary>
    private readonly byte[] _counter;
    /// <summary>Adapts the little-endian DSi counter to the standards-library AES block representation.</summary>
    private readonly byte[] _aesCounter = new byte[NdsModcrypt.BlockSize];
    /// <summary>Retains the current encrypted counter block until all sixteen keystream bytes are consumed.</summary>
    private readonly byte[] _keyStream = new byte[NdsModcrypt.BlockSize];
    /// <summary>Indexes the current keystream block; the block size sentinel requests another AES operation.</summary>
    private int _keyStreamIndex = NdsModcrypt.BlockSize;
    /// <summary>Prevents accidental use after sensitive transform state has been released.</summary>
    private bool _disposed;

    /// <summary>Positions a new cursor at an arbitrary byte offset by adding whole blocks to its little-endian counter.</summary>
    /// <param name="key">Validated AES-128 normal key.</param>
    /// <param name="initialCounter">Validated area counter before any offset adjustment.</param>
    /// <param name="byteOffset">Non-negative position relative to the start of the selected modcrypt area.</param>
    internal NdsModcryptTransform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> initialCounter, long byteOffset)
    {
        _aes = Aes.Create();
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
        _aesKey = key.ToArray();
        Array.Reverse(_aesKey);
        _aes.Key = _aesKey;
        _encryptor = _aes.CreateEncryptor();
        _counter = initialCounter.ToArray();
        AddBlocks(_counter, (ulong)(byteOffset / NdsModcrypt.BlockSize));
        int skip = (int)(byteOffset % NdsModcrypt.BlockSize);
        if (skip != 0)
        {
            GenerateKeyStream();
            _keyStreamIndex = skip;
        }
    }

    /// <summary>XORs bytes with the cursor's keystream; the identical operation encrypts and decrypts.</summary>
    /// <param name="source">Input bytes at the cursor's current logical offset.</param>
    /// <param name="destination">Equally sized output that may alias the input span.</param>
    internal void Transform(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (destination.Length < source.Length)
        {
            throw new ArgumentException("The modcrypt destination is shorter than the source.", nameof(destination));
        }

        for (int index = 0; index < source.Length; index++)
        {
            if (_keyStreamIndex == NdsModcrypt.BlockSize)
            {
                GenerateKeyStream();
                _keyStreamIndex = 0;
            }

            destination[index] = (byte)(source[index] ^ _keyStream[_keyStreamIndex++]);
        }
    }

    /// <summary>Releases AES provider state and erases the library-owned counter and keystream copies.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _encryptor.Dispose();
        _aes.Dispose();
        CryptographicOperations.ZeroMemory(_aesKey);
        CryptographicOperations.ZeroMemory(_counter);
        CryptographicOperations.ZeroMemory(_aesCounter);
        CryptographicOperations.ZeroMemory(_keyStream);
        _disposed = true;
    }

    /// <summary>Encrypts the current counter and advances it as an unsigned 128-bit little-endian integer.</summary>
    private void GenerateKeyStream()
    {
        for (int index = 0; index < _counter.Length; index++)
        {
            _aesCounter[index] = _counter[^(index + 1)];
        }

        _ = _encryptor.TransformBlock(_aesCounter, 0, _aesCounter.Length, _keyStream, 0);
        Array.Reverse(_keyStream);
        Increment(_counter);
    }

    /// <summary>Adds a 64-bit block displacement into a 128-bit little-endian counter with checked overflow.</summary>
    /// <param name="counter">Mutable sixteen-byte counter.</param>
    /// <param name="blocks">Whole AES blocks skipped before transformation begins.</param>
    private static void AddBlocks(Span<byte> counter, ulong blocks)
    {
        ulong carry = blocks;
        for (int index = 0; index < counter.Length && carry != 0; index++)
        {
            ulong sum = counter[index] + (carry & 0xFF);
            counter[index] = (byte)sum;
            carry = (carry >> 8) + (sum >> 8);
        }

        if (carry != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blocks), "The byte offset overflows the 128-bit modcrypt counter.");
        }
    }

    /// <summary>Advances the DSi counter by one block with carry beginning at byte zero.</summary>
    /// <param name="counter">Mutable sixteen-byte counter in platform byte order.</param>
    private static void Increment(Span<byte> counter)
    {
        for (int index = 0; index < counter.Length; index++)
        {
            counter[index]++;
            if (counter[index] != 0)
            {
                return;
            }
        }
    }
}

#pragma warning restore CA5358
