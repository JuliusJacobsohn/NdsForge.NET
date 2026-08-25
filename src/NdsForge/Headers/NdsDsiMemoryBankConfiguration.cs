using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace NdsForge;

/// <summary>Projects the complete 48-byte DSi MBK and WRAM configuration without discarding register bits.</summary>
public sealed class NdsDsiMemoryBankConfiguration
{
    /// <summary>Decodes register groups from a retained native 48-byte configuration.</summary>
    /// <param name="data">Exact MBK and WRAM bytes from the extended header.</param>
    internal NdsDsiMemoryBankConfiguration(ReadOnlyMemory<byte> data)
    {
        RawData = data;
        ReadOnlySpan<byte> span = data.Span;
        GlobalBanks = ReadWords(span[..0x14], 5);
        Arm9LocalBanks = ReadWords(span.Slice(0x14, 0x0C), 3);
        Arm7LocalBanks = ReadWords(span.Slice(0x20, 0x0C), 3);
        Bank9WriteProtection = (uint)(span[0x2C] | (span[0x2D] << 8) | (span[0x2E] << 16));
        WramControl = span[0x2F];
    }

    /// <summary>Gets all 48 native bytes, including every reserved register bit.</summary>
    public ReadOnlyMemory<byte> RawData { get; }

    /// <summary>Groups the five little-endian MBK registers shared between processor modes.</summary>
    public IReadOnlyList<uint> GlobalBanks { get; }

    /// <summary>Groups the three little-endian MBK registers applied to the ARM9 processor.</summary>
    public IReadOnlyList<uint> Arm9LocalBanks { get; }

    /// <summary>Groups the three little-endian MBK registers applied to the ARM7 processor.</summary>
    public IReadOnlyList<uint> Arm7LocalBanks { get; }

    /// <summary>Combines the three native MBK9 bytes into a low-24-bit write-protection value.</summary>
    public uint Bank9WriteProtection { get; }

    /// <summary>Preserves the complete WRAM control byte stored immediately after MBK9.</summary>
    public byte WramControl { get; }

    /// <summary>Decodes a fixed register group without exposing mutable array storage.</summary>
    /// <param name="data">Little-endian bytes containing at least <paramref name="count"/> words.</param>
    /// <param name="count">Number of consecutive 32-bit registers to decode.</param>
    /// <returns>A read-only collection retaining native register order.</returns>
    private static ReadOnlyCollection<uint> ReadWords(ReadOnlySpan<byte> data, int count)
    {
        var result = new uint[count];
        for (int index = 0; index < count; index++)
        {
            result[index] = BinaryPrimitives.ReadUInt32LittleEndian(data[(index * 4)..]);
        }

        return Array.AsReadOnly(result);
    }
}
