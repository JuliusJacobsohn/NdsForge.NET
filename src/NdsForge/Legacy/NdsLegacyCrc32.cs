using System.Buffers.Binary;

namespace NdsForge;

/// <summary>Implements the reversible CRC32 correction word used only by the historical ARM7 hook layout.</summary>
internal static class NdsLegacyCrc32
{
    /// <summary>Contains the reflected IEEE polynomial table used by ndstool's legacy CRC routines.</summary>
    private static readonly uint[] Table = CreateTable();

    /// <summary>Calculates standard finalized CRC32 for an invariant check or user-facing report.</summary>
    /// <param name="data">Complete bytes included in the checksum.</param>
    /// <returns>IEEE CRC32 with initial and final inversion.</returns>
    public static uint Calculate(ReadOnlySpan<byte> data) => ~Update(data, uint.MaxValue);

    /// <summary>
    /// Replaces bytes and writes a four-byte correction word so the selected prefix retains its previous CRC state.
    /// The operation mirrors the old tool's staged patch primitive rather than serving as a general checksum API.
    /// </summary>
    /// <param name="data">Mutable complete image with room for both patch and correction word.</param>
    /// <param name="patchOffset">First byte replaced.</param>
    /// <param name="patch">Replacement bytes.</param>
    /// <param name="logicalLength">Current file length; seeks beyond it read as legacy EOF bytes until a write extends the file.</param>
    /// <param name="fixOffset">Correction-word position; by default it immediately follows the patch.</param>
    public static void ReplacePreservingCrc(
        Span<byte> data,
        ref int logicalLength,
        int patchOffset,
        ReadOnlySpan<byte> patch,
        int? fixOffset = null)
    {
        int correctionOffset = fixOffset ?? checked(patchOffset + patch.Length);
        if (patchOffset < 0 || correctionOffset < patchOffset + patch.Length ||
            patch.Length > data.Length - patchOffset || 4 > data.Length - correctionOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(patchOffset), "The legacy CRC patch or correction word is outside the image buffer.");
        }

        Span<byte> state = stackalloc byte[8];
        uint before = UpdateVirtual(data, patchOffset, correctionOffset - patchOffset, logicalLength, uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(state, before);
        patch.CopyTo(data[patchOffset..]);
        logicalLength = Math.Max(logicalLength, checked(patchOffset + patch.Length));
        uint after = UpdateVirtual(data, patchOffset, correctionOffset - patchOffset + 4, logicalLength, uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(state[4..], after);
        for (int index = 4; index >= 1; index--)
        {
            byte tableIndex = FindHighByte(state[index + 3], out uint tableValue);
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(state[index..]);
            BinaryPrimitives.WriteUInt32LittleEndian(state[index..], word ^ tableValue);
            state[index - 1] ^= tableIndex;
        }

        state[..4].CopyTo(data[correctionOffset..]);
        logicalLength = Math.Max(logicalLength, checked(correctionOffset + 4));
    }

    /// <summary>Advances an unfinalized reflected CRC state through one byte span.</summary>
    /// <param name="data">Consecutive checksum input.</param>
    /// <param name="state">Initial unfinalized CRC state.</param>
    /// <returns>Unfinalized state after the last byte.</returns>
    private static uint Update(ReadOnlySpan<byte> data, uint state)
    {
        foreach (byte value in data)
        {
            state = (state >> 8) ^ Table[(state ^ value) & 0xFF];
        }

        return state;
    }

    /// <summary>Reproduces C stdio EOF reads as <c>0xFF</c> while retaining zero-filled host filesystem gaps.</summary>
    /// <param name="data">Allocated output capacity.</param>
    /// <param name="offset">First virtual file position.</param>
    /// <param name="length">Number of bytes consumed by the legacy CRC loop.</param>
    /// <param name="logicalLength">Current written file length.</param>
    /// <param name="state">Initial unfinalized CRC state.</param>
    /// <returns>Unfinalized state after actual bytes and synthetic EOF bytes.</returns>
    private static uint UpdateVirtual(ReadOnlySpan<byte> data, int offset, int length, int logicalLength, uint state)
    {
        for (int index = 0; index < length; index++)
        {
            int position = offset + index;
            byte value = position < logicalLength ? data[position] : byte.MaxValue;
            state = (state >> 8) ^ Table[(state ^ value) & 0xFF];
        }

        return state;
    }

    /// <summary>Reverses one table step using the unique high byte emitted by the reflected CRC32 table.</summary>
    /// <param name="highByte">Most significant byte of a table value.</param>
    /// <param name="value">Receives the complete matching table word.</param>
    /// <returns>The table index whose low input byte produced the value.</returns>
    private static byte FindHighByte(byte highByte, out uint value)
    {
        for (int index = 0; index < Table.Length; index++)
        {
            if ((byte)(Table[index] >> 24) == highByte)
            {
                value = Table[index];
                return (byte)index;
            }
        }

        throw new InvalidOperationException("The CRC32 reverse table is incomplete.");
    }

    /// <summary>Generates the standard reflected IEEE table from its polynomial instead of embedding copied constants.</summary>
    /// <returns>All 256 byte-transition words.</returns>
    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value >> 1) ^ ((value & 1) != 0 ? 0xEDB8_8320u : 0u);
            }

            table[index] = value;
        }

        return table;
    }
}
