using System.Buffers.Binary;
using System.Text;

namespace NdsForge;

internal static class NdsBinary
{
    public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    public static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    public static void WriteUInt16(Span<byte> data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data[offset..], value);

    public static void WriteUInt32(Span<byte> data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);

    public static string ReadAscii(ReadOnlySpan<byte> data, int offset, int length) =>
        Encoding.ASCII.GetString(data.Slice(offset, length)).TrimEnd('\0', ' ');
}
