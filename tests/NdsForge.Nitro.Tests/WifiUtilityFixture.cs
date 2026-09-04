using System.Buffers.Binary;

namespace NdsForge.Nitro.Tests;

/// <summary>Constructs a small caller-owned archive from explicit envelope, directory, and allocation records.</summary>
internal static class WifiUtilityFixture
{
    internal static byte[] Create(bool unnamed = false)
    {
        byte[] data = new byte[unnamed ? 128 : 112];
        Write32(data, 0, 16);
        Write32(data, 4, 46);
        Write32(data, 8, 64);
        Write32(data, 12, unnamed ? 32u : 24u);
        byte[] names =
        [
            16, 0, 0, 0, 0, 0, 2, 0,
            29, 0, 0, 0, 1, 0, 0, 0xF0,
            5, (byte)'a', (byte)'.', (byte)'b', (byte)'i', (byte)'n',
            0x83, (byte)'s', (byte)'u', (byte)'b', 1, 0xF0, 0,
            5, (byte)'b', (byte)'.', (byte)'b', (byte)'i', (byte)'n',
            9, (byte)'e', (byte)'m', (byte)'p', (byte)'t', (byte)'y', (byte)'.', (byte)'b', (byte)'i', (byte)'n', 0,
        ];
        names.CopyTo(data, 16);
        uint payload = unnamed ? 96u : 88u;
        Write32(data, 64, payload);
        Write32(data, 68, payload + 3);
        Write32(data, 72, payload + 4);
        Write32(data, 76, payload + 21);
        Write32(data, 80, payload + 24);
        Write32(data, 84, payload + 24);
        new byte[] { 0x11, 0x22, 0x33 }.CopyTo(data, payload);
        for (int index = 0; index < 17; index++) { data[payload + 4 + index] = (byte)(index * 13 + 7); }
        if (unnamed)
        {
            Write32(data, 88, 120);
            Write32(data, 92, 125);
            new byte[] { 1, 3, 5, 7, 9 }.CopyTo(data, 120);
        }
        return data;
    }

    internal static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
}
