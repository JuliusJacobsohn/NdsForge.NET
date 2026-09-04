using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsEditRegionProtectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GrowingFileCannotOverwriteDsiProgramsEvenWithVerificationDisabled(bool trailer)
    {
        using NdsImage image = NdsImage.Load(await CreateDsiAsync(trailer));
        using var destination = new MemoryStream();
        destination.Write([9, 8, 7]);
        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await image.Edit().ReplaceFile("hello.bin", new byte[0x5000]).SaveAsync(destination,
                new() { VerifyOutput = false }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Contains("overlaps", exception.Message, StringComparison.Ordinal);
        Assert.Equal([9, 8, 7], destination.ToArray());
        Assert.Equal(3, destination.Position);
    }

    [Fact]
    public async Task BannerAppendCannotOverwriteDsiPrograms()
    {
        using NdsImage image = NdsImage.Load(await CreateDsiAsync(false));
        using var destination = new MemoryStream();
        destination.Write([1, 2]);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await image.Edit()
            .ReplaceBanner(new NdsBannerBuilder().Build()).SaveAsync(destination,
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([1, 2], destination.ToArray());
    }

    [Fact]
    public async Task HeaderAndInPlaceDsiEditsRetainEveryExtraProgramByte()
    {
        byte[] source = await CreateDsiAsync(true);
        using NdsImage image = NdsImage.Load(source);
        NdsImageEditor editor = image.Edit().ReplaceFile("hello.bin", "world"u8);
        editor.Header.Title = "SAFE EDIT";
        using var destination = new MemoryStream();
        await editor.SaveAsync(destination, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = destination.ToArray();
        foreach (NdsProgram program in new[] { image.Header.Arm9i!, image.Header.Arm7i! })
        {
            int offset = checked((int)program.Data.Offset);
            int length = checked((int)program.Data.Length);
            Assert.Equal(source.AsSpan(offset, length).ToArray(), output.AsSpan(offset, length).ToArray());
        }
        using NdsImage rebuilt = NdsImage.Load(output);
        Assert.Equal("SAFE EDIT", rebuilt.Header.Title);
        Assert.Equal("world"u8.ToArray(), await rebuilt.FileSystem.GetFile(0).ReadAllBytesAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0x20)]
    [InlineData(0x30)]
    [InlineData(0x160)]
    public async Task AppendCannotOverwriteCommonProgramOrDebugTail(int field)
    {
        byte[] source = SyntheticImage.CreateHeaderOnly();
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(field), 0x400);
        if (field == 0x160) { BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0x164), 4); }
        using NdsImage image = NdsImage.Load(source);
        using var destination = new MemoryStream();
        destination.Write([5]);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await image.Edit().ReplaceFile("hello.bin", new byte[64])
            .SaveAsync(destination, new() { VerifyOutput = false }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([5], destination.ToArray());
    }

    [Fact]
    public async Task InPlaceAllocationAliasingAProgramIsRejected()
    {
        byte[] bytes = SyntheticImage.CreateHeaderOnly();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x220), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x224), 0x204);
        using NdsImage image = NdsImage.Load(bytes);
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await image.Edit().ReplaceFile("hello.bin", [1])
            .SaveAsync(destination, new() { VerifyOutput = false }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal(0, destination.Length);
        await image.Edit().SaveAsync(destination, new() { VerifyOutput = false }, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, destination.ToArray());
    }

    [Fact]
    public async Task AppendedTrailerCannotOverlapAProgramEvenWhenReplacementFitsTheGap()
    {
        byte[] source = await CreateDsiAsync(true);
        using (NdsImage original = NdsImage.Load(source))
        {
            uint arm9i = checked((uint)original.Header.Arm9i!.Data.Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0x80), arm9i - 136);
            source.AsSpan(checked((int)arm9i) - 136, 136).Clear();
            new byte[] { 0x61, 0x63, 1, 0 }.CopyTo(source, checked((int)arm9i) - 136);
        }
        using NdsImage image = NdsImage.Load(source);
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await image.Edit().ReplaceFile("hello.bin", new byte[16])
            .SaveAsync(destination, new() { RelocatedFileAlignment = 4, VerifyOutput = false },
                TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal(0, destination.Length);
    }

    private static async Task<byte[]> CreateDsiAsync(bool trailer)
    {
        using NdsImage source = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true);
        builder.Kind = NdsImageKind.NintendoDsiEnhanced;
        builder.Carrier = NdsImageCarrier.DigitalSrl;
        builder.DsiMetadata = new() { TitleId = 0x0003000454455354 };
        builder.Arm9i = new(NdsProcessor.Arm9i, [9, 8, 7], 0x02400000, 0x02400000);
        builder.Arm7i = new(NdsProcessor.Arm7i, [6, 5, 4], 0x02E80000, 0x02E80000);
        if (trailer)
        {
            byte[] bytes = new byte[136];
            new byte[] { 0x61, 0x63, 1, 0 }.CopyTo(bytes, 0);
            builder.DownloadPlaySignature = NdsDownloadPlaySignature.Parse(bytes);
        }
        return await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
    }
}
