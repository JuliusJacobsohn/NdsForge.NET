using System.Security.Cryptography;

namespace NdsForge.Tests;

public sealed class NdsImageCapacityTests
{
    [Theory]
    [InlineData(0x20000, 90877, 0, "3910D5A540ED26B1FED942E3BE61208B678198E3CA60E049FB4E14EED9D9A60B")]
    [InlineData(0x100000, 715776, 255, "3F26F89123D20662F7D63CE3862F207C0629960CE5ED4639BEAD662DCD0E9DC1")]
    [InlineData(0x20000, 1, 0, "6E340B9CFFB37A989CA544E6BB780A2C78901D3FB33738768511A30617AFA01D")]
    public async Task PaddingIntervalsMatchKnownLengthAndByteDigests(int capacity, int paddingLength, byte fill, string digest)
    {
        NdsImageBuilder builder = CreateBuilder();
        int contentLength = capacity - paddingLength;
        builder.FileSystem.AddFile("/payload.bin", new byte[contentLength - 0x4608]);
        byte[] bytes = await builder.BuildAsync(new()
        {
            FileAlignment = 1,
            RequestedDeviceCapacityBytes = capacity,
            PadToDeviceCapacity = true,
            PaddingByte = fill,
        }, TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.Equal((long)contentLength, image.Header.UsedImageSize);
        Assert.Equal(capacity, bytes.Length);
        Assert.Equal(digest, Convert.ToHexString(SHA256.HashData(bytes.AsSpan(contentLength))));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 255)]
    [InlineData(true, 0)]
    [InlineData(true, 255)]
    public async Task CapacityPaddingPreservesEveryContentByteAndUsedField(bool dsi, byte padding)
    {
        NdsImageBuilder builder = CreateBuilder(dsi);
        builder.FileSystem.AddFile("/all-ff.bin", Enumerable.Repeat((byte)255, 259).ToArray());
        builder.DownloadPlaySignature = CreateTrailer();
        if (dsi)
        {
            builder.DsiMetadata!.Digests = new() { SectorSize = 0x400, BlockSectorCount = 2 };
            builder.DsiMetadata.Integrity = NdsDsiIntegrityOptions.CreateHmacSha1([2, 4, 6, 8]);
        }
        long capacity = dsi ? 0x200000 : 0x40000;
        var compactOptions = new NdsImageBuildOptions { RequestedDeviceCapacityBytes = capacity, PaddingByte = padding };
        byte[] compact = await builder.BuildAsync(compactOptions, TestContext.Current.CancellationToken);
        byte[] padded = await builder.BuildAsync(compactOptions with { PadToDeviceCapacity = true }, TestContext.Current.CancellationToken);
        Assert.Equal(capacity, padded.Length);
        Assert.Equal(compact, padded[..compact.Length]);
        Assert.All(padded[compact.Length..], value => Assert.Equal(padding, value));
        using NdsImage compactImage = NdsImage.Load(compact);
        using NdsImage image = NdsImage.Load(padded);
        Assert.Equal(capacity, image.SizeInfo.DeviceCapacityBytes);
        Assert.Equal(compactImage.SizeInfo.DeclaredContentEnd, image.SizeInfo.DeclaredContentEnd);
        Assert.Equal(new NdsRegion(image.SizeInfo.DeclaredContentEnd, capacity - image.SizeInfo.DeclaredContentEnd), image.SizeInfo.TrailingData);
        Assert.Equal(builder.DownloadPlaySignature.RawData.ToArray(), image.DownloadPlaySignature!.RawData.ToArray());
        using Stream file = image.FileSystem.GetFile("/all-ff.bin").OpenRead();
        Assert.Equal("0B54B4EF2F019C1A1661461F37AAFA996BD3D4F4DD717C598CC15CB17DCE967D",
            Convert.ToHexString(await SHA256.HashDataAsync(file, TestContext.Current.CancellationToken)));
        if (dsi)
        {
            Assert.Equal((long)compact.Length, image.Header.Dsi!.TotalImageSize);
            Assert.True(image.Validate(new NdsValidationOptions().SetDsiHmacKey([2, 4, 6, 8])).IsValid);
        }
        else { Assert.True(image.Validate().IsValid); }
    }

    [Theory]
    [InlineData(0x20000L, 0)]
    [InlineData(0x40000L, 1)]
    [InlineData(0x100000000L, 15)]
    public async Task RequestedCapacityChangesOnlyTheCapacityByteAndHeaderChecksum(long capacity, byte exponent)
    {
        NdsImageBuilder builder = CreateBuilder();
        byte[] original = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        byte[] bytes = await builder.BuildAsync(new() { RequestedDeviceCapacityBytes = capacity }, TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.Equal(original.Length, bytes.Length);
        Assert.Equal(exponent, image.Header.DeviceCapacityExponent);
        Assert.Equal(capacity, image.Header.DeviceCapacityBytes);
        Assert.True(image.Validate().IsValid);
        bytes[0x14] = original[0x14];
        original.AsSpan(0x15E, 2).CopyTo(bytes.AsSpan(0x15E));
        Assert.Equal(original, bytes);
    }

    [Theory]
    [InlineData(false, 0x20000)]
    [InlineData(true, 0x100000)]
    public async Task AutomaticCapacityPadsToTheSmallestContainingDevice(bool dsi, long capacity)
    {
        byte[] bytes = await CreateBuilder(dsi).BuildAsync(new() { PadToDeviceCapacity = true }, TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.Equal(capacity, bytes.Length);
        Assert.Equal(capacity, image.Header.DeviceCapacityBytes);
        Assert.True(image.SizeInfo.DeclaredContentEnd < bytes.Length);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(65536L)]
    [InlineData(131073L)]
    [InlineData(0x200000000L)]
    [InlineData(long.MaxValue)]
    public async Task InvalidRepresentationsFailBeforeChangingDestination(long capacity)
    {
        using var stream = new MemoryStream();
        stream.Write([9, 8, 7]);
        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateBuilder().WriteAsync(stream,
            new() { RequestedDeviceCapacityBytes = capacity }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], stream.ToArray());
        Assert.Equal(3, stream.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndersizedCapacityRejectsDsiReservationsEvenWithoutPadding(bool pad)
    {
        using var stream = new MemoryStream();
        stream.Write([9, 8, 7]);
        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateBuilder(true).WriteAsync(stream,
            new() { RequestedDeviceCapacityBytes = 0x80000, PadToDeviceCapacity = pad }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], stream.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DigitalCarrierRejectsExplicitCartridgePolicies(bool pad)
    {
        NdsImageBuilder builder = CreateBuilder(true);
        builder.Carrier = NdsImageCarrier.DigitalSrl;
        builder.DsiMetadata!.TitleId = 0x0003000441424344;
        using var stream = new MemoryStream();
        stream.Write([9, 8, 7]);
        await Assert.ThrowsAsync<ArgumentException>(async () => await builder.WriteAsync(stream,
            new() { RequestedDeviceCapacityBytes = pad ? null : 0x20000, PadToDeviceCapacity = pad },
            TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], stream.ToArray());
    }

    [Fact]
    public async Task OversizedContiguousOutputIsRejectedBeforeAllocationOrStreamMutation()
    {
        using var stream = new MemoryStream();
        stream.Write([9, 8, 7]);
        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateBuilder().WriteAsync(stream,
            new() { RequestedDeviceCapacityBytes = 0x100000000, PadToDeviceCapacity = true },
            TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], stream.ToArray());
    }

    [Fact]
    public async Task FourGiBCapacityStreamsWithBoundedBuffersAndUnchangedUsedSize()
    {
        using var stream = new CountingOutputStream();
        NdsImageBuildResult result = await CreateBuilder().WriteAsync(stream, new()
        {
            RequestedDeviceCapacityBytes = 0x100000000,
            PadToDeviceCapacity = true,
            VerifyOutput = false,
        }, TestContext.Current.CancellationToken);
        Assert.Equal(0x100000000, result.PhysicalSize);
        Assert.Equal(result.PhysicalSize, stream.Length);
        Assert.Equal(result.PhysicalSize, stream.Position);
        Assert.True(result.UsedSize < 0x20000);
        Assert.True(stream.MaximumWrite <= 64 * 1024);
        Assert.Equal(15, stream.HeaderCapacity);
    }

    [Fact]
    public async Task CapacityChangesAreIncludedInRegeneratedAuthentication()
    {
        using var fixture = new LateDsBuildFixture();
        byte[] before = await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        byte[] after = await fixture.Builder.BuildAsync(new() { RequestedDeviceCapacityBytes = 0x40000, PadToDeviceCapacity = true },
            TestContext.Current.CancellationToken);
        using NdsImage source = NdsImage.Load(before);
        using NdsImage output = NdsImage.Load(after);
        Assert.NotEqual(source.Header.DsExtended!.ProgramsHmac.ToArray(), output.Header.DsExtended!.ProgramsHmac.ToArray());
        Assert.True(output.Header.DsExtended.VerifyRsaSignature(fixture.PublicKey));
        Assert.True(output.Validate(fixture.Validation()).IsValid);
    }

    [Fact]
    public async Task ExplicitTailPaddingDoesNotInheritProfileSpecificInteriorFiller()
    {
        NdsImageBuilder builder = CreateBuilder();
        var options = new NdsImageBuildOptions { Profile = NdsImageBuildProfile.Ndstool1503, PaddingByte = 0xA5 };
        byte[] compact = await builder.BuildAsync(options, TestContext.Current.CancellationToken);
        byte[] padded = await builder.BuildAsync(options with { PadToDeviceCapacity = true }, TestContext.Current.CancellationToken);
        Assert.Equal(compact, padded[..compact.Length]);
        Assert.All(padded[compact.Length..], static value => Assert.Equal(0xA5, value));
    }

    [Fact]
    public async Task InvalidCapacityDoesNotReplaceAnExistingPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ndsforge-capacity-{Guid.NewGuid():N}.nds");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 9, 8, 7 }, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<ArgumentException>(async () => await CreateBuilder(true).WriteAsync(path,
                new() { RequestedDeviceCapacityBytes = 0x20000, OverwriteDestination = true },
                TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".ndsforge-*"));
        }
        finally { File.Delete(path); }
    }

    private static NdsDownloadPlaySignature CreateTrailer()
    {
        byte[] bytes = new byte[136];
        new byte[] { 0x61, 0x63, 1, 0 }.CopyTo(bytes, 0);
        for (int index = 4; index < bytes.Length; index++) { bytes[index] = (byte)(index * 7); }
        return NdsDownloadPlaySignature.Parse(bytes);
    }

    private static NdsImageBuilder CreateBuilder(bool dsi = false) => new()
    {
        Kind = dsi ? NdsImageKind.NintendoDsiEnhanced : NdsImageKind.NintendoDs,
        Arm9 = new(NdsProcessor.Arm9, [1], 0x02000000, 0x02000000),
        Arm7 = new(NdsProcessor.Arm7, [2], 0x02380000, 0x02380000),
        Arm9i = dsi ? new(NdsProcessor.Arm9i, [3], 0x02400000, 0x02400000) : null,
        Arm7i = dsi ? new(NdsProcessor.Arm7i, [4], 0x02E80000, 0x02E80000) : null,
        DsiMetadata = dsi ? new() : null,
    };

    private sealed class CountingOutputStream : Stream
    {
        private long _length;
        public int MaximumWrite { get; private set; }
        public byte HeaderCapacity { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get; set; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => _length = value;
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            MaximumWrite = Math.Max(MaximumWrite, buffer.Length);
            if (Position == 0 && buffer.Length > 0x14) { HeaderCapacity = buffer[0x14]; }
            Position += buffer.Length;
            _length = Math.Max(_length, Position);
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
    }
}
