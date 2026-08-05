using System.Buffers.Binary;
using System.Text;

namespace NdsForge;

internal static class NdsBinary
{
    public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    public static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    public static string ReadAscii(ReadOnlySpan<byte> data, int offset, int length) =>
        Encoding.ASCII.GetString(data.Slice(offset, length)).TrimEnd('\0', ' ');
}

