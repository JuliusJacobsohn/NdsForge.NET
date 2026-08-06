using System.Buffers.Binary;

namespace NdsForge;

/// <summary>
/// Holds the 18 round words and four 256-word substitution boxes consumed by the DS KEY1 algorithm. The library
/// deliberately has no built-in retail table: callers obtain the 0x1048 bytes from a source they are authorized to
/// use and can keep that provenance outside build recipes and logs.
/// </summary>
public sealed class NdsKey1KeyTable
{
    /// <summary>Number of little-endian bytes in the complete KEY1 seed schedule.</summary>
    public const int ByteLength = 0x1048;

    /// <summary>Retains an independent word copy so later caller-buffer changes cannot affect transformations.</summary>
    private readonly uint[] _words;

    /// <summary>Loads the complete native little-endian key schedule without assuming where it originated.</summary>
    /// <param name="data">Exactly 0x1048 bytes: 18 round words followed by 1024 substitution-box words.</param>
    public NdsKey1KeyTable(ReadOnlySpan<byte> data)
    {
        if (data.Length != ByteLength)
        {
            throw new ArgumentException($"A DS KEY1 table must contain exactly 0x{ByteLength:X} bytes.", nameof(data));
        }

        _words = new uint[ByteLength / sizeof(uint)];
        for (int index = 0; index < _words.Length; index++)
        {
            _words[index] = BinaryPrimitives.ReadUInt32LittleEndian(data[(index * sizeof(uint))..]);
        }
    }

    /// <summary>Exports the seed schedule exactly as little-endian words for controlled persistence or interop.</summary>
    /// <returns>A new 0x1048-byte array that cannot mutate this instance.</returns>
    public byte[] Export()
    {
        var data = new byte[ByteLength];
        for (int index = 0; index < _words.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(index * sizeof(uint)), _words[index]);
        }

        return data;
    }

    /// <summary>Protects the reusable seed schedule from the algorithm's destructive per-title expansion.</summary>
    /// <returns>A private word array because initialization mutates every round and substitution word.</returns>
    internal uint[] CreateWorkingWords() => (uint[])_words.Clone();
}
