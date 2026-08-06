using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsLegacyArm7HookTests
{
    [Fact]
    public void CorrectionWordPreservesCrcAcrossAnInPlacePatch()
    {
        byte[] data = Enumerable.Range(0, 100).Select(static value => (byte)value).ToArray();
        uint expected = CalculateCrc32(data);
        int logicalLength = data.Length;

        NdsLegacyCrc32.ReplacePreservingCrc(data, ref logicalLength, 20, [9, 8, 7, 6]);

        Assert.Equal(expected, CalculateCrc32(data));
        Assert.Equal(data.Length, logicalLength);
    }

    [Fact]
    public async Task RelocatesArm7AndReportsWholeImageCrc32()
    {
        byte[] source = await BuildImageAsync().ConfigureAwait(true);
        byte[] unchanged = source.ToArray();
        byte[] hook = [0x11, 0x22, 0x33, 0x44, 0x55];

        NdsLegacyArm7HookResult result = NdsLegacyArm7Hook.Apply(source, hook);
        ReadOnlySpan<byte> output = result.Image.Span;

        Assert.Equal(unchanged, source);
        Assert.Equal(result.Crc32, CalculateCrc32(output));
        Assert.Equal(8, result.Hook.Length);
        Assert.Equal(hook, output.Slice(checked((int)result.Hook.Offset), hook.Length).ToArray());
        Assert.Equal([0, 0, 0], output.Slice(checked((int)result.Hook.Offset + hook.Length), 3).ToArray());
        Assert.Equal(
            source.AsSpan(0, 0x200).ToArray(),
            output.Slice(checked((int)result.HeaderBackup.Offset), 0x200).ToArray());
        Assert.Equal(result.RelocatedArm7.Offset, BinaryPrimitives.ReadUInt32LittleEndian(output[0x30..]));
        Assert.Equal(result.RelocatedArm7.Length, BinaryPrimitives.ReadUInt32LittleEndian(output[0x3C..]));
        Assert.Equal(result.HeaderBackup.Offset + 0x200, BinaryPrimitives.ReadUInt32LittleEndian(output[0x80..]));
        Assert.Equal(0x027F_FE18u, BinaryPrimitives.ReadUInt32LittleEndian(output[0x24..]));
        Assert.Equal(0xE59F_F004u, BinaryPrimitives.ReadUInt32LittleEndian(output[0x18..]));

        using NdsImage image = NdsImage.Load(result.Image);
        Assert.True(image.Validate().IsValid);
    }

    [Fact]
    public async Task RejectsEmptyDsiAndOutOfBoundsInputs()
    {
        byte[] source = await BuildImageAsync().ConfigureAwait(true);
        Assert.Throws<ArgumentException>(() => NdsLegacyArm7Hook.Apply(source, []));
        Assert.Throws<InvalidDataException>(() => NdsLegacyArm7Hook.Apply(source.AsSpan(0, 100), [1]));

        byte[] dsi = source.ToArray();
        dsi[0x12] = 2;
        Assert.Throws<InvalidDataException>(() => NdsLegacyArm7Hook.Apply(dsi, [1]));

        byte[] invalid = source.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(0x30), checked((uint)invalid.Length));
        Assert.Throws<InvalidDataException>(() => NdsLegacyArm7Hook.Apply(invalid, [1]));
    }

    private static ValueTask<byte[]> BuildImageAsync()
    {
        var builder = new NdsImageBuilder
        {
            Title = "HOOK TEST",
            GameCode = "HK01",
            MakerCode = "HB",
            Arm9 = new(NdsProcessor.Arm9, [0xA9, 1, 2, 3], 0x0200_0000, 0x0200_0000),
            Arm7 = new(NdsProcessor.Arm7, [0xA7, 4, 5, 6], 0x0238_0000, 0x0238_0000),
        };
        builder.FileSystem.AddFile("/data.bin", [7, 8, 9]);
        return builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    private static uint CalculateCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB8_8320u : 0u);
            }
        }

        return ~crc;
    }
}
