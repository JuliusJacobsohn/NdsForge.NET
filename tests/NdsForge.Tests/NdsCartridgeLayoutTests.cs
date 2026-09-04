using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsCartridgeLayoutTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(0x4000, false)]
    [InlineData(0x4200, true)]
    [InlineData(1, true)]
    public async Task CartridgeTailRespectsAccessWindowsAndDigestPlacement(int arm9iLength, bool digests)
    {
        NdsImageBuilder builder = CreateBuilder(arm9iLength);
        if (digests)
        {
            builder.DsiMetadata!.Digests = new() { SectorSize = 0x400, BlockSectorCount = 2 };
            builder.DsiMetadata.Integrity = NdsDsiIntegrityOptions.CreateHmacSha1([2, 4, 6, 8]);
        }
        byte[] bytes = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(bytes);
        using var stream = new MemoryStream(bytes);
        using NdsImage asynchronous = await NdsImage.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        NdsCartridgeLayout layout = Assert.IsType<NdsCartridgeLayout>(image.CarrierLayout);
        Assert.Empty(layout.Diagnostics);
        Assert.Equal(0x80000, layout.NtrRegionEnd);
        Assert.Equal(layout.NtrRegionEnd, layout.TwlRegionStart);
        Assert.Equal(new NdsRegion(0x80000, 0x3000), layout.TwlReservedRegion);
        Assert.Equal(0x83000, image.Header.Arm9i!.Data.Offset);
        Assert.Equal(Math.Max(0x87000, 0x83000 + arm9iLength), image.Header.Arm7i!.Data.Offset);
        Assert.Equal(checked((uint)bytes.Length), image.Header.Dsi!.TotalImageSize);
        for (int part = 0; part < 3; part++)
        {
            Assert.Equal(bytes.AsSpan(0x8000, 0x1000).ToArray(), layout.TwlReservedData.Slice(part * 0x1000, 0x1000).ToArray());
        }
        Assert.Equal(layout.TwlReservedData.ToArray(), Assert.IsType<NdsCartridgeLayout>(asynchronous.CarrierLayout).TwlReservedData.ToArray());
        if (digests)
        {
            Assert.Equal(image.Header.Dsi.NtrDigest.End, image.Header.Dsi.SectorHashTable.Offset);
            Assert.True(image.Header.Dsi.BlockHashTable.End <= image.Header.UsedImageSize);
            Assert.True(image.Header.UsedImageSize <= layout.NtrRegionEnd);
            Assert.True(image.Validate(new NdsValidationOptions().SetDsiHmacKey([2, 4, 6, 8])).IsValid);
        }
        Assert.Equal(bytes, await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpaqueReservationSurvivesNoOpAndRelocationUnlessExplicitlyRegenerated()
    {
        NdsImageBuilder builder = CreateBuilder();
        byte[] opaque = Enumerable.Range(0, 0x3000).Select(static value => (byte)(value * 19 + 7)).ToArray();
        builder.SetTwlReservedData(opaque);
        byte[] bytes = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        opaque[0] ^= 255;
        using NdsImage original = NdsImage.Load(bytes);
        using var copy = new MemoryStream();
        await original.Edit().SaveAsync(copy, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(bytes, copy.ToArray());
        NdsImageBuilder imported = await NdsImageBuilder.FromImageAsync(original, TestContext.Current.CancellationToken);
        Assert.NotEqual(opaque[0], imported.TwlReservedData.Span[0]);
        imported.FileSystem.AddFile("/growth.bin", new byte[0x80000]);
        using NdsImage rebuilt = NdsImage.Load(await imported.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        NdsCartridgeLayout layout = Assert.IsType<NdsCartridgeLayout>(rebuilt.CarrierLayout);
        Assert.Equal(0x100000, layout.TwlRegionStart);
        Assert.Equal(builder.TwlReservedData.ToArray(), layout.TwlReservedData.ToArray());
        imported.SetTwlReservedData([]);
        byte[] regenerated = await imported.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(regenerated.AsSpan(0x8000, 0x1000).ToArray(), regenerated.AsSpan(0x100000, 0x1000).ToArray());
    }

    [Theory]
    [InlineData(0x90, 0, "NDS1564")]
    [InlineData(0x92, 0, "NDS1564")]
    [InlineData(0x80, 0x80001, "NDS1564")]
    [InlineData(0x1C0, 0x80000, "NDS1565")]
    [InlineData(0x1D0, 0x84000, "NDS1566")]
    public async Task InvalidCartridgeDeclarationsRejectSemanticWritesAtomically(int field, uint value, string diagnostic)
    {
        byte[] bytes = await CreateBuilder().BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        if (field is 0x90 or 0x92) { BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(field), checked((ushort)value)); }
        else { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(field), value); }
        RepairCrc(bytes);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.Contains(image.Validate().Diagnostics, item => item.Code == diagnostic);
        using var destination = new MemoryStream();
        destination.Write([9, 8]);
        NdsImageEditor editor = image.Edit();
        editor.Header.Title = "REJECT";
        await Assert.ThrowsAsync<InvalidDataException>(async () => await editor.SaveAsync(destination,
            new() { VerifyOutput = false }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8], destination.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageBuilder.FromImageAsync(image,
            TestContext.Current.CancellationToken).ConfigureAwait(true));
        await image.Edit().SaveAsync(destination, new() { VerifyOutput = false }, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, destination.ToArray());
    }

    [Fact]
    public async Task TruncatedAndUnspecifiedReservationsAreExplicitlyDiagnosed()
    {
        byte[] bytes = await CreateBuilder().BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        bytes.AsSpan(0x1C0, 0x20).Clear();
        using NdsImage truncated = NdsImage.Load(bytes.AsMemory(0, 0x81000));
        Assert.Equal(0x1000, Assert.IsType<NdsCartridgeLayout>(truncated.CarrierLayout).TwlReservedData.Length);
        Assert.Contains(truncated.Validate().Diagnostics, static item => item.Code == "NDS1565");
        bytes.AsSpan(0x90, 4).Clear();
        RepairCrc(bytes);
        using NdsImage unspecified = NdsImage.Load(bytes);
        Assert.Contains(unspecified.CarrierLayout.Diagnostics, static item => item.Code == "NDS1563");
        Assert.Null(Assert.IsType<NdsCartridgeLayout>(unspecified.CarrierLayout).TwlReservedRegion);
    }

    [Fact]
    public async Task RelocatedFileCannotOverwriteTwlReservation()
    {
        using NdsImage image = NdsImage.Load(await CreateBuilder().BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        NdsCartridgeLayout layout = Assert.IsType<NdsCartridgeLayout>(image.CarrierLayout);
        int size = checked((int)(layout.TwlRegionStart - image.Header.UsedImageSize + 512));
        using var destination = new MemoryStream();
        destination.Write([7]);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await image.Edit().ReplaceFile("hello.bin", new byte[size])
            .SaveAsync(destination, new() { VerifyOutput = false }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([7], destination.ToArray());
    }

    [Theory]
    [InlineData(0x80000, "NDS1565")]
    [InlineData(0x2000, "NDS1561")]
    public async Task RecognizedTrailerCannotAliasAnOpaqueReservation(int trailerOffset, string diagnostic)
    {
        NdsImageBuilder builder = CreateBuilder();
        if (trailerOffset == 0x2000)
        {
            builder.Carrier = NdsImageCarrier.DigitalSrl;
            builder.DsiMetadata!.TitleId = 0x0003000454455354;
        }
        byte[] bytes = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80), checked((uint)trailerOffset));
        new byte[] { 0x61, 0x63, 1, 0 }.CopyTo(bytes, trailerOffset);
        RepairCrc(bytes);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.NotNull(image.DownloadPlaySignature);
        Assert.Contains(image.CarrierLayout.Diagnostics, item => item.Code == diagnostic);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageBuilder.FromImageAsync(image,
            TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task ReservationRecipeCannotSilentlyDiscardBytesForOtherCarriers()
    {
        NdsImageBuilder builder = CreateBuilder();
        Assert.Throws<ArgumentException>(() => builder.SetTwlReservedData([1]));
        builder.SetTwlReservedData(new byte[0x3000]);
        using var destination = new MemoryStream();
        destination.Write([1]);
        builder.Carrier = NdsImageCarrier.DigitalSrl;
        builder.DsiMetadata!.TitleId = 0x0003000454455354;
        await Assert.ThrowsAsync<InvalidDataException>(async () => await builder.WriteAsync(destination,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([1], destination.ToArray());
        builder.Carrier = NdsImageCarrier.Cartridge;
        builder.Kind = NdsImageKind.NintendoDs;
        await Assert.ThrowsAsync<InvalidDataException>(async () => await builder.WriteAsync(destination,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    private static NdsImageBuilder CreateBuilder(int arm9iLength = 1)
    {
        var builder = new NdsImageBuilder
        {
            Kind = NdsImageKind.NintendoDsiEnhanced,
            Arm9 = new(NdsProcessor.Arm9, Enumerable.Range(0, 0x5000).Select(static value => (byte)(value * 17 + 3)).ToArray(), 0x02000000, 0x02000000),
            Arm7 = new(NdsProcessor.Arm7, [1, 2, 3], 0x02380000, 0x02380000),
            Arm9i = new(NdsProcessor.Arm9i, new byte[arm9iLength], 0x02400000, 0x02400000),
            Arm7i = new(NdsProcessor.Arm7i, [6, 7], 0x02E80000, 0x02E80000),
            DsiMetadata = new() { TitleId = 0x0003000054455354 },
        };
        builder.FileSystem.AddFile("/hello.bin", "hello"u8);
        return builder;
    }

    private static void RepairCrc(byte[] bytes) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x15E),
        NdsChecksums.ComputeCrc16(bytes.AsSpan(0, 0x15E)));
}
