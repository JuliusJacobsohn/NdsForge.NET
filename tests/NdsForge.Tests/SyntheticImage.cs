using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NdsForge.Shared;

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
        WriteUInt32(data, 0x44, 19);
        WriteUInt32(data, 0x48, 0x220);
        WriteUInt32(data, 0x4C, 8);
        WriteUInt32(data, 0x80, 0x22D);
        WriteUInt32(data, 0x84, 0x200);
        WriteUInt32(data, 0x208, 8);
        WriteUInt16(data, 0x20C, 0);
        WriteUInt16(data, 0x20E, 1);
        data[0x210] = 9;
        Encoding.ASCII.GetBytes("hello.bin").CopyTo(data, 0x211);
        data[0x21A] = 0;
        WriteUInt32(data, 0x220, 0x228);
        WriteUInt32(data, 0x224, 0x22D);
        "hello"u8.CopyTo(data.AsSpan(0x228));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x15C), NdsChecksums.ComputeCrc16(data.AsSpan(0xC0, 156)));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x15E), NdsChecksums.ComputeCrc16(data.AsSpan(0, 0x15E)));
        return data;
    }

    public static byte[] CreateWithOverlay()
    {
        byte[] data = CreateHeaderOnly();
        WriteUInt32(data, 0x50, 0x230);
        WriteUInt32(data, 0x54, 32);
        WriteUInt32(data, 0x80, 0x250);
        WriteUInt32(data, 0x230, 7);
        WriteUInt32(data, 0x234, 0x02001000);
        WriteUInt32(data, 0x238, 0x100);
        WriteUInt32(data, 0x23C, 0x20);
        WriteUInt32(data, 0x240, 0x02001080);
        WriteUInt32(data, 0x244, 0x02001088);
        WriteUInt32(data, 0x248, 0);
        WriteUInt32(data, 0x24C, 0x01000005);
        WriteUInt16(data, 0x15E, NdsChecksums.ComputeCrc16(data.AsSpan(0, 0x15E)));
        return data;
    }

    public static byte[] CreateWithOverlayAuthentication(uint tableOffset = 0x100)
    {
        byte[] data = CreateHeaderOnly();
        byte[] fnt = data.AsSpan(0x208, 19).ToArray();
        byte[] overlayPayload = "auth!"u8.ToArray();
        byte[] key = CreateOverlayAuthenticationKey();

        WriteUInt32(data, 0x20, 0x1000);
        WriteUInt32(data, 0x24, 0x02000000);
        WriteUInt32(data, 0x28, 0x02000000);
        WriteUInt32(data, 0x2C, 0x400);
        WriteUInt32(data, 0x30, 0x1500);
        WriteUInt32(data, 0x3C, 4);
        WriteUInt32(data, 0x40, 0x1640);
        WriteUInt32(data, 0x44, 19);
        WriteUInt32(data, 0x48, 0x1660);
        WriteUInt32(data, 0x4C, 8);
        WriteUInt32(data, 0x50, 0x1600);
        WriteUInt32(data, 0x54, 32);
        WriteUInt32(data, 0x80, 0x1685);
        data[0x1BF] = 0x40;

        if (tableOffset <= 0x400 - 20)
        {
#pragma warning disable CA5350 // The synthetic fixture models format-mandated HMAC-SHA1 bytes.
            HMACSHA1.HashData(key, overlayPayload, data.AsSpan(0x1000 + checked((int)tableOffset), 20));
#pragma warning restore CA5350
        }

        key.CopyTo(data, 0x1180);
        WriteUInt32(data, 0x1400, 0xDEC00621);
        WriteUInt32(data, 0x1404, 0);
        WriteUInt32(data, 0x1408, tableOffset);

        WriteUInt32(data, 0x1600, 7);
        WriteUInt32(data, 0x1604, 0x02001000);
        WriteUInt32(data, 0x1608, (uint)overlayPayload.Length);
        WriteUInt32(data, 0x1610, 0x02001000);
        WriteUInt32(data, 0x1614, 0x02001004);
        WriteUInt32(data, 0x1618, 0);
        WriteUInt32(data, 0x161C, 0x02000000);
        fnt.CopyTo(data, 0x1640);
        WriteUInt32(data, 0x1660, 0x1680);
        WriteUInt32(data, 0x1664, 0x1685);
        overlayPayload.CopyTo(data, 0x1680);
        WriteUInt16(data, 0x15E, NdsChecksums.ComputeCrc16(data.AsSpan(0, 0x15E)));
        return data;
    }

    public static byte[] CreateOverlayAuthenticationKey()
    {
        byte[] key = Enumerable.Range(0, 64).Select(static index => (byte)(index * 13 + 7)).ToArray();
        key[0] = 0x21;
        key[1] = 0x06;
        key[2] = 0xC0;
        key[3] = 0xDE;
        return key;
    }

    public static byte[] CreateWithCompressedArm9OverlayAuthentication()
    {
        byte[] data = CreateWithOverlayAuthentication();
        byte[] decoded = data.AsSpan(0x1000, 0x400).ToArray();
        WriteUInt32(decoded, 0x34, 0);
        WriteUInt32(decoded, 0x38, 0x05057533);
        WriteUInt32(decoded, 0x3C, 0xDEC00621);
        WriteUInt32(decoded, 0x40, 0x2106C0DE);
        Assert.True(BlzEngine.TryCompress(decoded, out byte[] encoded, uncompressedPrefixLength: 0x44));
        WriteUInt32(encoded, 0x34, checked(0x02000000u + (uint)encoded.Length));
        data.AsSpan(0x1000, 0x500).Clear();
        encoded.CopyTo(data, 0x1000);
        int footer = 0x1000 + encoded.Length;
        WriteUInt32(data, footer, 0xDEC00621);
        WriteUInt32(data, footer + 4, 0x20);
        WriteUInt32(data, footer + 8, 0x100);
        WriteUInt32(data, 0x2C, checked((uint)encoded.Length));
        WriteUInt16(data, 0x15E, NdsChecksums.ComputeCrc16(data.AsSpan(0, 0x15E)));
        return data;
    }

    public static byte[] CreateWithBanner()
    {
        byte[] data = CreateHeaderOnly();
        WriteUInt32(data, 0x68, 0x300);
        WriteUInt16(data, 0x300, 1);
        data[0x320] = 1;
        WriteUInt16(data, 0x520 + 2, 0x001F);
        Encoding.Unicode.GetBytes("English Title").CopyTo(data, 0x640);
        (int offset, int length) = NdsBanner.GetCrcRegion(0);
        WriteUInt16(data, 0x302, NdsChecksums.ComputeCrc16(data.AsSpan(0x300 + offset, length)));
        WriteUInt16(data, 0x15E, NdsChecksums.ComputeCrc16(data.AsSpan(0, 0x15E)));
        return data;
    }

    public static byte[] CreateWithArm9Footer()
    {
        byte[] data = CreateHeaderOnly();
        byte[] fnt = data.AsSpan(0x208, 19).ToArray();
        WriteUInt32(data, 0x204, 0xDEC00621);
        WriteUInt32(data, 0x208, 0x01020304);
        WriteUInt32(data, 0x20C, 0x05060708);
        WriteUInt32(data, 0x30, 0x218);
        WriteUInt32(data, 0x40, 0x220);
        WriteUInt32(data, 0x48, 0x238);
        WriteUInt32(data, 0x238, 0x240);
        WriteUInt32(data, 0x23C, 0x245);
        fnt.CopyTo(data, 0x220);
        "hello"u8.CopyTo(data.AsSpan(0x240));
        WriteUInt32(data, 0x44, 19);
        WriteUInt32(data, 0x4C, 8);
        WriteUInt32(data, 0x80, 0x245);
        WriteUInt16(data, 0x15E, NdsChecksums.ComputeCrc16(data.AsSpan(0, 0x15E)));
        return data;
    }

    public static byte[] CreateDsiEnhanced()
    {
        byte[] data = CreateHeaderOnly();
        byte[] fnt = data.AsSpan(0x208, 19).ToArray();
        data[0x12] = 2;
        data[0x1C] = 0xA3;
        data[0x1D] = 0xF3;
        WriteUInt32(data, 0x20, 0x1000);
        WriteUInt32(data, 0x30, 0x1004);
        WriteUInt32(data, 0x40, 0x1008);
        WriteUInt32(data, 0x48, 0x1020);
        WriteUInt32(data, 0x80, 0x102D);
        for (int index = 0; index < 0x30; index++)
        {
            data[0x180 + index] = (byte)(index * 3 + 1);
        }

        fnt.CopyTo(data, 0x1008);
        WriteUInt32(data, 0x1020, 0x1028);
        WriteUInt32(data, 0x1024, 0x102D);
        "hello"u8.CopyTo(data.AsSpan(0x1028));
        WriteUInt32(data, 0x1B0, 0x11223344);
        WriteUInt32(data, 0x1B4, 0x55667788);
        WriteUInt32(data, 0x1B8, 0x99AABBCC);
        data[0x1BF] = 0x5A;
        WriteUInt32(data, 0x1C0, 0x1100);
        WriteUInt32(data, 0x1C8, 0x02E00000);
        WriteUInt32(data, 0x1CC, 0x80);
        WriteUInt32(data, 0x208, 0x23C0);
        data[0x20C] = 1;
        data[0x20D] = 2;
        data[0x20E] = 7;
        data[0x20F] = 0x81;
        WriteUInt32(data, 0x210, 0x4000);
        data[0x214] = 3;
        data[0x215] = 4;
        data[0x216] = 5;
        data[0x217] = 6;
        WriteUInt32(data, 0x230, 0x01234567);
        WriteUInt32(data, 0x234, 0x89ABCDEF);
        WriteUInt32(data, 0x238, 0x10000);
        WriteUInt32(data, 0x23C, 0x20000);
        for (int index = 0; index < 0x10; index++)
        {
            data[0x2F0 + index] = (byte)(0x80 | index);
        }

        data[0x2F1] = 0xEA;
        data[0xF80] = 0xA5;
        WriteUInt16(data, 0x15E, NdsChecksums.ComputeCrc16(data.AsSpan(0, 0x15E)));
        return data;
    }

    public static byte[] CreateLateDsAuthenticated()
    {
        byte[] data = CreateHeaderOnly();
        byte[] fnt = data.AsSpan(0x208, 19).ToArray();
        WriteUInt32(data, 0x20, 0x1000);
        WriteUInt32(data, 0x24, 0x02000000);
        WriteUInt32(data, 0x28, 0x02000000);
        WriteUInt32(data, 0x2C, 0x100);
        WriteUInt32(data, 0x30, 0x1120);
        WriteUInt32(data, 0x40, 0x1200);
        WriteUInt32(data, 0x48, 0x1220);
        WriteUInt32(data, 0x80, 0x122D);
        WriteUInt32(data, 0x88, 0x1040);
        WriteUInt32(data, 0x8C, 0);
        data[0x1BF] = 0x60;
        data[0x33C] = 0x31;
        data[0x378] = 0x32;
        data[0x38C] = 0x33;
        data[0xF80] = 0x34;
        WriteUInt32(data, 0x1040, 0x02001000);
        WriteUInt32(data, 0x1044, 0x02001018);
        WriteUInt32(data, 0x1048, 0x02002000);
        WriteUInt32(data, 0x104C, 0x02003000);
        WriteUInt32(data, 0x1050, 0x02003100);
        WriteUInt32(data, 0x1054, 0x02000080);
        WriteUInt32(data, 0x1058, 0x05057533);
        WriteUInt32(data, 0x105C, 0xDEC00621);
        WriteUInt32(data, 0x1060, 0x2106C0DE);
        WriteUInt32(data, 0x1100, 0xDEC00621);
        WriteUInt32(data, 0x1104, 0x40);
        WriteUInt32(data, 0x1108, 0x80);
        fnt.CopyTo(data, 0x1200);
        WriteUInt32(data, 0x1220, 0x1228);
        WriteUInt32(data, 0x1224, 0x122D);
        "hello"u8.CopyTo(data.AsSpan(0x1228));
        WriteUInt16(data, 0x15E, NdsChecksums.ComputeCrc16(data.AsSpan(0, 0x15E)));
        return data;
    }

    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);

    private static void WriteUInt16(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
}
