using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsSecureAreaTests
{
    [Fact]
    public void ExplicitKeyTableEncryptsAndDecryptsWithoutTouchingTail()
    {
        byte[] area = CreateDecryptedArea();
        NdsKey1KeyTable key = CreateTestKey();

        byte[] encrypted = NdsSecureArea.Encrypt(area, "SA01", key);
        byte[] decrypted = NdsSecureArea.Decrypt(encrypted, "SA01", key);

        Assert.NotEqual(area[..0x800], encrypted[..0x800]);
        Assert.Equal(area[0x800..], encrypted[0x800..]);
        Assert.Equal(area, decrypted);
        Assert.Equal(key.Export(), CreateTestKey().Export());
    }

    [Fact]
    public void InspectionSeparatesStateFromNullableCrcEvidence()
    {
        byte[] plain = CreateDecryptedArea();
        NdsKey1KeyTable key = CreateTestKey();
        byte[] encrypted = NdsSecureArea.Encrypt(plain, "SA01", key);
        ushort crc = NdsChecksums.ComputeCrc16(encrypted);

        NdsSecureAreaInspection unknownCrc = NdsSecureArea.Inspect(plain, "SA01", crc);
        NdsSecureAreaInspection plainInspection = NdsSecureArea.Inspect(plain, "SA01", crc, key);
        NdsSecureAreaInspection encryptedInspection = NdsSecureArea.Inspect(encrypted, "SA01", crc, key);

        Assert.Equal(NdsSecureAreaState.Decrypted, unknownCrc.State);
        Assert.Null(unknownCrc.IsCrcValid);
        Assert.True(plainInspection.IsCrcValid);
        Assert.Equal(NdsSecureAreaState.Encrypted, encryptedInspection.State);
        Assert.True(encryptedInspection.IsCrcValid);
        Assert.True(encryptedInspection.IsTransformable);
    }

    [Fact]
    public void WrongIdentityCannotProducePlausibleDecryption()
    {
        byte[] encrypted = NdsSecureArea.Encrypt(CreateDecryptedArea(), "SA01", CreateTestKey());

        Assert.Throws<InvalidDataException>(() =>
            NdsSecureArea.Decrypt(encrypted, "NOPE", CreateTestKey()));
        Assert.Equal(
            NdsSecureAreaState.Unrecognized,
            NdsSecureArea.Inspect(encrypted, "NOPE", 0, CreateTestKey()).State);
    }

    [Fact]
    public void InspectionReportsAbsentAndTruncatedImageStates()
    {
        using NdsImage absent = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        byte[] truncatedBytes = SyntheticImage.CreateHeaderOnly();
        BinaryPrimitives.WriteUInt32LittleEndian(truncatedBytes.AsSpan(0x20), 0x4000);
        using NdsImage truncated = NdsImage.Load(truncatedBytes);

        Assert.Equal(NdsSecureAreaState.Absent, NdsSecureArea.Inspect(absent).State);
        Assert.Equal(NdsSecureAreaState.Malformed, NdsSecureArea.Inspect(truncated).State);
    }

    [Fact]
    public void InputsMustHonorFixedFormatBoundaries()
    {
        Assert.Throws<ArgumentException>(() => new NdsKey1KeyTable(new byte[8]));
        Assert.Throws<ArgumentException>(() => NdsSecureArea.Encrypt(new byte[8], "SA01", CreateTestKey()));
        Assert.Throws<ArgumentException>(() => NdsSecureArea.Encrypt(CreateDecryptedArea(), "BAD", CreateTestKey()));
        Assert.Throws<InvalidDataException>(() =>
            NdsSecureArea.Encrypt(new byte[NdsSecureArea.ByteLength], "SA01", CreateTestKey()));
    }

    [Fact]
    public void ValidationUsesExplicitKeyAndReportsCrcTampering()
    {
        byte[] bytes = CreateImageWithSecureArea(out NdsKey1KeyTable key);
        using NdsImage image = NdsImage.Load(bytes);

        NdsValidationResult valid = image.Validate(new NdsValidationOptions().SetSecureAreaKeyTable(key));
        bytes[NdsSecureArea.Offset + 0x900] ^= 1;
        using NdsImage tampered = NdsImage.Load(bytes);
        NdsValidationResult invalid = tampered.Validate(new NdsValidationOptions().SetSecureAreaKeyTable(key));

        Assert.DoesNotContain(valid.Diagnostics, static value => value.Code.StartsWith("NDS14", StringComparison.Ordinal));
        Assert.Contains(invalid.Diagnostics, static value => value.Code == "NDS1404");
    }

    [Fact]
    public async Task StreamTransformsDoNotCloseCallerStreamsOrWriteOnInvalidInput()
    {
        byte[] plain = CreateDecryptedArea();
        NdsKey1KeyTable key = CreateTestKey();
        using var source = new MemoryStream(plain);
        using var encrypted = new MemoryStream();

        await NdsSecureArea.EncryptAsync(
            source,
            encrypted,
            "SA01",
            key,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        using var output = new MemoryStream([9, 8, 7], writable: true);
        output.Position = output.Length;
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await NdsSecureArea.DecryptAsync(
                new MemoryStream(encrypted.ToArray()),
                output,
                "NOPE",
                key,
                TestContext.Current.CancellationToken).ConfigureAwait(true));

        Assert.True(source.CanRead);
        Assert.True(encrypted.CanWrite);
        Assert.Equal([9, 8, 7], output.ToArray());
    }

    [Fact]
    public async Task ExplicitEditorRepairUpdatesSecureAndDependentHeaderCrcs()
    {
        byte[] bytes = CreateImageWithSecureArea(out NdsKey1KeyTable key);
        bytes[0x6C] ^= 0x20;
        bytes[0x15E] ^= 0x40;
        using NdsImage image = NdsImage.Load(bytes);
        NdsImageEditor editor = image.Edit().RepairSecureAreaCrc(key);
        using var destination = new MemoryStream();

        await editor.SaveAsync(destination, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using NdsImage output = NdsImage.Load(destination.ToArray());
        NdsValidationResult validation = output.Validate(new NdsValidationOptions().SetSecureAreaKeyTable(key));

        Assert.Equal(NdsRepairKind.SecureAreaCrc | NdsRepairKind.HeaderCrc, editor.Plan.Repairs);
        Assert.True(validation.IsValid);
    }

    private static byte[] CreateDecryptedArea()
    {
        var area = new byte[NdsSecureArea.ByteLength];
        for (int index = 8; index < area.Length; index++)
        {
            area[index] = (byte)((index * 29) ^ (index >> 3));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(area, 0xE7FFDEFF);
        BinaryPrimitives.WriteUInt32LittleEndian(area.AsSpan(4), 0xE7FFDEFF);
        return area;
    }

    private static NdsKey1KeyTable CreateTestKey()
    {
        var data = new byte[NdsKey1KeyTable.ByteLength];
        for (int index = 0; index < data.Length; index++)
        {
            data[index] = (byte)((index * 37) + 11);
        }

        return new(data);
    }

    private static byte[] CreateImageWithSecureArea(out NdsKey1KeyTable key)
    {
        byte[] bytes = SyntheticImage.CreateHeaderOnly();
        Array.Resize(ref bytes, 0x8000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x20), NdsSecureArea.Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x2C), NdsSecureArea.ByteLength);
        key = CreateTestKey();
        byte[] encrypted = NdsSecureArea.Encrypt(CreateDecryptedArea(), "TEST", key);
        encrypted.CopyTo(bytes, NdsSecureArea.Offset);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x6C), NdsChecksums.ComputeCrc16(encrypted));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x15E), NdsChecksums.ComputeCrc16(bytes.AsSpan(0, 0x15E)));
        return bytes;
    }
}
