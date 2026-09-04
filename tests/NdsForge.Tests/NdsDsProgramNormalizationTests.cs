using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NdsForge.Tests;

public sealed class NdsDsProgramNormalizationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothStorageFormsMatchOneFixedEncryptedRepresentation(bool encryptedStorage)
    {
        byte[] imageBytes = CreateFixture(encryptedStorage, out NdsKey1KeyTable secureKey);
        using NdsImage image = NdsImage.Load(imageBytes);
        NdsValidationResult validation = image.Validate(new NdsValidationOptions()
            .SetDsProgramHmacKey(ProgramKey()).SetSecureAreaKeyTable(secureKey));
        Assert.DoesNotContain(validation.Diagnostics, static item => item.Code is "NDS1510" or "NDS1512" or "NDS1514");
        Assert.Equal("7B76A78347E8C7F9BD28E16826625AD39489C5AC", Convert.ToHexString(image.Header.DsExtended!.ProgramsHmac.Span));
        byte[] raw = NdsDsAuthentication.ComputeProgramsHmac(
            imageBytes.AsSpan(0, 0x160), imageBytes.AsSpan(0x4000, 0x4800), imageBytes.AsSpan(0xA000, 0x300), ProgramKey());
        Assert.Equal(encryptedStorage
            ? "7B76A78347E8C7F9BD28E16826625AD39489C5AC"
            : "ED63DD4D15228338F4BD10059A9E01346125535A", Convert.ToHexString(raw));
    }

    [Fact]
    public void AuthenticationNormalizationDoesNotSilentlyRepairEmbeddedProgramBytes()
    {
        byte[] data = CreateFixture(encryptedStorage: false, out NdsKey1KeyTable key);
        data[0x400E] = 0x97;
        data[0x400F] = 0xB2;
        Convert.FromHexString("FA43A54D2BA8DA00C36FC696B8BBA6373459617C").CopyTo(data, 0x378);
        using NdsImage image = NdsImage.Load(data);
        NdsValidationResult validation = image.Validate(new NdsValidationOptions()
            .SetDsProgramHmacKey(ProgramKey()).SetSecureAreaKeyTable(key));
        Assert.DoesNotContain(validation.Diagnostics, static item => item.Code is "NDS1510" or "NDS1512");
        Assert.Contains(validation.Diagnostics, static item => item.Code == "NDS1404");
        Assert.Equal(0x97, data[0x400E]);
        Assert.Equal(0xB2, data[0x400F]);
    }

    private static byte[] CreateFixture(bool encryptedStorage, out NdsKey1KeyTable key)
    {
        byte[] plain = Enumerable.Range(0, 0x4800).Select(static index => (byte)((index * 29) ^ (index >> 3))).ToArray();
        Write(plain, 0, 0xE7FFDEFF);
        Write(plain, 4, 0xE7FFDEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(plain.AsSpan(0xE), 0x4F08);
        key = new(Enumerable.Range(0, NdsKey1KeyTable.ByteLength).Select(static index => (byte)(index * 37 + 11)).ToArray());
        byte[] encrypted = (byte[])plain.Clone();
        NdsSecureArea.Encrypt(plain.AsSpan(0, 0x4000), "TEST", key).CopyTo(encrypted, 0);
        Assert.Equal("0790FE7213248BF6A8D49FF6DCFF03A7A2382263C1CD92D2FAB42B65CE2C46E6", Convert.ToHexString(SHA256.HashData(encrypted)));
        byte[] data = new byte[0xA300];
        Encoding.ASCII.GetBytes("TEST").CopyTo(data, 0);
        Encoding.ASCII.GetBytes("TEST").CopyTo(data, 12);
        Encoding.ASCII.GetBytes("00").CopyTo(data, 16);
        data[0x1BF] = 0x40;
        Write(data, 0x20, 0x4000);
        Write(data, 0x24, 0x02000000);
        Write(data, 0x28, 0x02000000);
        Write(data, 0x2C, 0x4800);
        Write(data, 0x30, 0xA000);
        Write(data, 0x34, 0x02380000);
        Write(data, 0x38, 0x02380000);
        Write(data, 0x3C, 0x300);
        Write(data, 0x80, 0xA300);
        Write(data, 0x84, 0x4000);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x6C), 51487);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x15E), NdsChecksums.ComputeCrc16(data.AsSpan(0, 0x15E)));
        Assert.Equal(37959, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x15E)));
        Assert.Equal("1DA1B96D25417E846F99E1401C9B5FDF9B50BD1A4919333D36A73787948D3F3C",
            Convert.ToHexString(SHA256.HashData(data.AsSpan(0, 0x160))));
        (encryptedStorage ? encrypted : plain).CopyTo(data, 0x4000);
        for (int index = 0; index < 0x300; index++)
        {
            data[0xA000 + index] = (byte)((index * 19) ^ (index >> 4) ^ 0xA5);
        }

        Convert.FromHexString("7B76A78347E8C7F9BD28E16826625AD39489C5AC").CopyTo(data, 0x378);
        return data;
    }

    private static byte[] ProgramKey() => Enumerable.Range(0, 64).Select(static index => (byte)index).ToArray();

    private static void Write(Span<byte> data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);
}
