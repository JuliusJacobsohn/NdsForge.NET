using System.Buffers.Binary;
using System.Text;

namespace NdsForge;

/// <summary>Centralizes the little-endian and fixed-field string conventions shared by DS-family structures.</summary>
internal static class NdsBinary
{
    /// <summary>Decodes an unsigned 16-bit field at a structure-relative byte offset.</summary>
    /// <param name="data">Complete containing structure or table entry.</param>
    /// <param name="offset">Byte position whose next two bytes must be available.</param>
    /// <returns>The little-endian field value without sign extension.</returns>
    public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    /// <summary>Decodes an unsigned 32-bit field at a structure-relative byte offset.</summary>
    /// <param name="data">Complete containing structure or table entry.</param>
    /// <param name="offset">Byte position whose next four bytes must be available.</param>
    /// <returns>The little-endian field value without sign extension.</returns>
    public static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    /// <summary>Encodes a 16-bit value in the byte order required by DS headers and table records.</summary>
    /// <param name="data">Mutable containing structure with at least two bytes remaining.</param>
    /// <param name="offset">Structure-relative destination offset.</param>
    /// <param name="value">Unsigned value written exactly, including zero.</param>
    public static void WriteUInt16(Span<byte> data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data[offset..], value);

    /// <summary>Encodes a 32-bit value in the byte order required by DS headers and table records.</summary>
    /// <param name="data">Mutable containing structure with at least four bytes remaining.</param>
    /// <param name="offset">Structure-relative destination offset.</param>
    /// <param name="value">Unsigned value written exactly, including high-bit values.</param>
    public static void WriteUInt32(Span<byte> data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);

    /// <summary>Decodes a fixed-width ASCII field and removes only format padding, never interior spaces.</summary>
    /// <param name="data">Structure containing the fixed-width text bytes.</param>
    /// <param name="offset">First byte of the field.</param>
    /// <param name="length">Encoded field width, not the expected visible character count.</param>
    /// <returns>Text with trailing NUL and space padding removed.</returns>
    public static string ReadAscii(ReadOnlySpan<byte> data, int offset, int length) =>
        Encoding.ASCII.GetString(data.Slice(offset, length)).TrimEnd('\0', ' ');
}
