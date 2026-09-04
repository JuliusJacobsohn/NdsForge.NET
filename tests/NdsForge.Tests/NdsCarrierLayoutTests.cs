using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsCarrierLayoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task DigitalStorageIsIndependentOfExecutionMode(int unit)
    {
        byte[] bytes = await CreateDigitalBuilder().BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        foreach (uint category in new uint[] { 0x30004, 0x30005, 0x30015, 0x30017 })
        {
            bytes[0x12] = (byte)unit;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x234), category);
            RepairHeaderCrc(bytes);
            using NdsImage image = NdsImage.Load(bytes);
            using var stream = new MemoryStream(bytes, writable: false);
            using NdsImage asynchronous = await NdsImage.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
            NdsDigitalSrlLayout layout = Assert.IsType<NdsDigitalSrlLayout>(image.CarrierLayout);
            Assert.IsType<NdsDigitalSrlLayout>(asynchronous.CarrierLayout);
            Assert.Equal(((ulong)category << 32) | 0x54455354, layout.TitleId);
            Assert.Equal((NdsImageKind)unit, image.Header.Kind);
            Assert.Equal(NdsSecureAreaState.Absent, NdsSecureArea.Inspect(image).State);
            Assert.Equal(new NdsRegion(0x1000, 0x3000), layout.PostHeaderRegion);
            Assert.Equal(Pattern(), layout.PostHeaderData.ToArray());
            Assert.True(image.Validate().IsValid);
            using var copy = new MemoryStream();
            await image.Edit().SaveAsync(copy, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(bytes, copy.ToArray());
        }
    }

    [Fact]
    public async Task DsiExclusiveCartridgeRemainsACartridgeAndKeepsOpaqueMaterial()
    {
        NdsImageBuilder builder = CreateDigitalBuilder();
        builder.Carrier = NdsImageCarrier.Cartridge;
        builder.DsiMetadata!.TitleId = 0x0003000054455354;
        byte[] bytes = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.IsType<NdsCartridgeLayout>(image.CarrierLayout);
        Assert.NotEqual(NdsSecureAreaState.Absent, NdsSecureArea.Inspect(image).State);
        NdsImageBuilder imported = await NdsImageBuilder.FromImageAsync(image, TestContext.Current.CancellationToken);
        using NdsImage rebuilt = NdsImage.Load(await imported.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(Pattern(), rebuilt.CarrierLayout.PostHeaderData.ToArray());
    }

    [Fact]
    public async Task ReservedDigitalBytesAndInformationalCapacitySurviveSemanticWrites()
    {
        byte[] bytes = await CreateDigitalBuilder().BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        bytes[0x14] = 0;
        RepairHeaderCrc(bytes);
        using NdsImage image = NdsImage.Load(bytes);
        NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(image, TestContext.Current.CancellationToken);
        builder.FileSystem.AddFile("/new.bin", new byte[0x40000]);
        byte[] rebuilt = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage output = NdsImage.Load(rebuilt);
        Assert.Equal(0, output.Header.DeviceCapacityExponent);
        Assert.Equal(output.Length, output.Header.Dsi!.TotalImageSize);
        Assert.True(output.Length > output.Header.DeviceCapacityBytes);
        Assert.Equal(Pattern(), output.CarrierLayout.PostHeaderData.ToArray());
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(rebuilt.AsSpan(0x90)));
        Assert.Equal(NdsChecksums.ComputeCrc16(rebuilt.AsSpan(0x4000, 0x4000)), output.Header.SecureAreaCrc);
        NdsImageEditor editor = output.Edit();
        editor.Header.Title = "DIGITAL EDIT";
        using var edited = new MemoryStream();
        await editor.SaveAsync(edited, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, edited.ToArray()[0x14]);
    }

    [Theory]
    [InlineData(0x90, 1, "NDS1560")]
    [InlineData(0x234, 0x3000F, "NDS1562")]
    [InlineData(0x234, 0x40004, "NDS1562")]
    [InlineData(0x84, 0x2000, "NDS1561")]
    public async Task AmbiguousOrMalformedCarriersRejectSemanticWritesBeforeMutation(int offset, uint value, string diagnostic)
    {
        byte[] bytes = await CreateDigitalBuilder().BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        RepairHeaderCrc(bytes);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.Contains(image.Validate().Diagnostics, item => item.Code == diagnostic);
        using var destination = new MemoryStream();
        destination.Write([8, 9]);
        NdsImageEditor editor = image.Edit();
        editor.Header.Title = "REJECT";
        await Assert.ThrowsAsync<InvalidDataException>(async () => await editor.SaveAsync(destination,
            new() { VerifyOutput = false }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([8, 9], destination.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageBuilder.FromImageAsync(image,
            TestContext.Current.CancellationToken).ConfigureAwait(true));
        await image.Edit().SaveAsync(destination, new() { VerifyOutput = false }, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, destination.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EmptyDigitalProgramTuplesRetainAddressesAndRoundTrip(int emptyMask)
    {
        NdsImageBuilder builder = CreateDigitalBuilder();
        if ((emptyMask & 1) != 0) { builder.Arm9i = new(NdsProcessor.Arm9i, [], 0x02400000, 0x02400000); }
        if ((emptyMask & 2) != 0) { builder.Arm7i = new(NdsProcessor.Arm7i, [], 0x02E80000, 0x02E80000); }
        using NdsImage image = NdsImage.Load(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0x02400000u, image.Header.Arm9i!.LoadAddress);
        Assert.Equal(0x02E80000u, image.Header.Arm7i!.LoadAddress);
        Assert.Equal((emptyMask & 1) != 0, image.Header.Arm9i.Data.IsEmpty);
        Assert.Equal((emptyMask & 2) != 0, image.Header.Arm7i.Data.IsEmpty);
        NdsImageBuilder imported = await NdsImageBuilder.FromImageAsync(image, TestContext.Current.CancellationToken);
        using NdsImage rebuilt = NdsImage.Load(await imported.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(image.Header.Arm9i, rebuilt.Header.Arm9i);
        Assert.Equal(image.Header.Arm7i, rebuilt.Header.Arm7i);
    }

    [Fact]
    public async Task InvalidRecipeCarrierAndEmptyProgramAddressAreRejected()
    {
        NdsImageBuilder builder = CreateDigitalBuilder();
        using var destination = new MemoryStream();
        destination.Write([3]);
        foreach (NdsImageCarrier carrier in new[] { NdsImageCarrier.Unknown, NdsImageCarrier.Cartridge, (NdsImageCarrier)99 })
        {
            builder.Carrier = carrier;
            await Assert.ThrowsAsync<InvalidDataException>(async () => await builder.WriteAsync(destination,
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal([3], destination.ToArray());
        }
        builder.Carrier = NdsImageCarrier.DigitalSrl;
        builder.Arm9i = new(NdsProcessor.Arm9i, [], 0, 0);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await builder.WriteAsync(destination,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Throws<ArgumentException>(() => builder.SetPostHeaderData(new byte[0x3001]));
    }

    [Fact]
    public async Task HeaderReservationDoesNotClaimAliasedProgramBytes()
    {
        byte[] bytes = await CreateDigitalBuilder().BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x30), 0x1000);
        RepairHeaderCrc(bytes);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.Empty(image.CarrierLayout.PostHeaderData.ToArray());
        Assert.Contains(image.Validate().Diagnostics, static item => item.Code == "NDS1561");
    }

    [Fact]
    public async Task TruncatedReservedBytesAreBoundedAndDsModeWritingIsExplicitlyUnsupported()
    {
        byte[] bytes = await CreateDigitalBuilder().BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        bytes[0x12] = 0;
        RepairHeaderCrc(bytes);
        using (NdsImage source = NdsImage.Load(bytes))
        {
            NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await builder.BuildAsync(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        }
        bytes.AsSpan(0x20, 0x40).Clear();
        bytes.AsSpan(0x1C0, 0x20).Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80), 0x2000);
        RepairHeaderCrc(bytes);
        using NdsImage truncated = NdsImage.Load(bytes.AsMemory(0, 0x2000));
        Assert.Equal(0x1000, truncated.CarrierLayout.PostHeaderData.Length);
        Assert.Contains(truncated.Validate().Diagnostics, static item => item.Code == "NDS1561");
    }

    private static NdsImageBuilder CreateDigitalBuilder()
    {
        var builder = new NdsImageBuilder
        {
            Title = "CARRIER TEST",
            GameCode = "TEST",
            MakerCode = "HB",
            Kind = NdsImageKind.NintendoDsiExclusive,
            Carrier = NdsImageCarrier.DigitalSrl,
            Arm9 = new(NdsProcessor.Arm9, Enumerable.Range(0, 0x5000).Select(static value => (byte)(value * 17 + 3)).ToArray(), 0x02000000, 0x02000000),
            Arm7 = new(NdsProcessor.Arm7, [1, 2, 3], 0x02380000, 0x02380000),
            Arm9i = new(NdsProcessor.Arm9i, [4, 5], 0x02400000, 0x02400000),
            Arm7i = new(NdsProcessor.Arm7i, [6, 7], 0x02E80000, 0x02E80000),
            DsiMetadata = new() { TitleId = 0x0003000454455354 },
        };
        byte[] pattern = Pattern();
        builder.SetPostHeaderData(pattern);
        pattern[0] ^= 0xFF;
        builder.FileSystem.AddFile("/hello.bin", "hello"u8);
        return builder;
    }

    private static byte[] Pattern() => Enumerable.Range(0, 0x3000).Select(static value => (byte)((value * 37 + 11) ^ (value >> 5))).ToArray();

    private static void RepairHeaderCrc(byte[] bytes) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x15E),
        NdsChecksums.ComputeCrc16(bytes.AsSpan(0, 0x15E)));
}
