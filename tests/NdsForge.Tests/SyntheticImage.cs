using System.Buffers.Binary;
using System.Text;

namespace NdsForge.Tests;

internal static class SyntheticImage
{
    public static byte[] CreateHeaderOnly()
    {
        byte[] data = new byte[0x4000];
        Encoding.ASCII.GetBytes("TEST IMAGE").CopyTo(data, 0x00);
        Encoding.ASCII.GetBytes("TEST").CopyTo(data, 0x0C);
        Encoding.ASCII.GetBytes("01").CopyTo(data, 0x10);
        data[0x14] = 0;
        WriteUInt32(data, 0x20, 0x200);
        WriteUInt32(data, 0x24, 0x02000000);
        WriteUInt32(data, 0x28, 0x02000000);
        WriteUInt32(data, 0x2C, 4);
        WriteUInt32(data, 0x30, 0x204);
        WriteUInt32(data, 0x34, 0x02380000);
        WriteUInt32(data, 0x38, 0x02380000);
        WriteUInt32(data, 0x3C, 4);
        WriteUInt32(data, 0x40, 0x208);
        WriteUInt32(data, 0x44, 8);
        WriteUInt32(data, 0x48, 0x210);
        WriteUInt32(data, 0x4C, 8);
        WriteUInt32(data, 0x80, 0x218);
        WriteUInt32(data, 0x84, 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x15C), NdsChecksums.ComputeCrc16(data.AsSpan(0xC0, 156)));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x15E), NdsChecksums.ComputeCrc16(data.AsSpan(0, 0x15E)));
        return data;
    }

    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
}

