using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsImageResizeSafetyTests
{
    [Fact]
    public async Task LateDsCoverageAndEveryAuthenticationLayerSurviveResize()
    {
        using var fixture = new LateDsBuildFixture();
        byte[] full = await fixture.Builder.BuildAsync(new() { PadToDeviceCapacity = true }, TestContext.Current.CancellationToken);
        using NdsImage source = NdsImage.Load(full);
        using var output = new MemoryStream();
        await NdsImageResizer.WriteAsync(source, output, new() { Mode = NdsImageResizeMode.Trim }, TestContext.Current.CancellationToken);
        Assert.Equal(source.SizeInfo.DeclaredContentEnd, output.Length);
        using NdsImage trimmed = NdsImage.Load(output.ToArray());
        Assert.Equal(source.Header.RawData.ToArray(), trimmed.Header.RawData.ToArray());
        Assert.True(trimmed.Validate(fixture.Validation()).IsValid);
        Assert.All(NdsDsAuthentication.GetOverlayHashRegions(trimmed), region => Assert.True(region.End <= trimmed.Length));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DsiProgramsReservationsAndDigestsSurviveTrimming(bool digital)
    {
        var builder = new NdsImageBuilder
        {
            Kind = NdsImageKind.NintendoDsiEnhanced,
            Carrier = digital ? NdsImageCarrier.DigitalSrl : NdsImageCarrier.Cartridge,
            Arm9 = new(NdsProcessor.Arm9, [1], 0x02000000, 0x02000000),
            Arm7 = new(NdsProcessor.Arm7, [2], 0x02380000, 0x02380000),
            Arm9i = new(NdsProcessor.Arm9i, [3], 0x02400000, 0x02400000),
            Arm7i = new(NdsProcessor.Arm7i, [4], 0x02E80000, 0x02E80000),
            DsiMetadata = new()
            {
                TitleId = digital ? 0x0003000441424344ul : 0,
                Digests = new() { SectorSize = 0x400, BlockSectorCount = 2 },
                Integrity = NdsDsiIntegrityOptions.CreateHmacSha1([2, 4, 6, 8]),
            },
        };
        byte[] compact = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        byte[] bytes = new byte[compact.Length + 8192];
        compact.CopyTo(bytes, 0);
        bytes.AsSpan(compact.Length).Fill(255);
        using NdsImage image = NdsImage.Load(bytes);
        using var output = new MemoryStream();
        await NdsImageResizer.WriteAsync(image, output, new() { Mode = NdsImageResizeMode.Trim }, TestContext.Current.CancellationToken);
        Assert.Equal(compact, output.ToArray());
        using NdsImage trimmed = NdsImage.Load(output.ToArray());
        Assert.True(trimmed.Validate(new NdsValidationOptions().SetDsiHmacKey([2, 4, 6, 8])).IsValid);
        if (digital)
        {
            await Assert.ThrowsAsync<ArgumentException>(async () => await NdsImageResizer.WriteAsync(image, output,
                new() { Mode = NdsImageResizeMode.PadToDeviceCapacity }, TestContext.Current.CancellationToken).ConfigureAwait(true));
            await NdsImageResizer.WriteAsync(image, output, new() { Mode = NdsImageResizeMode.ExactLength, OutputLengthBytes = bytes.Length + 100 },
                TestContext.Current.CancellationToken);
            Assert.Equal(bytes.Length + 100, output.Length);
        }
    }

    [Fact]
    public async Task UnknownCarrierAndUnrepresentableCapacityAreNeverGuessed()
    {
        foreach (bool unknownCarrier in new[] { false, true })
        {
            byte[] bytes = SyntheticImage.CreateHeaderOnly();
            if (unknownCarrier)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x234), 0x12345678);
                bytes[0x12] = 3;
            }
            else { bytes[0x14] = 255; }
            using NdsImage image = NdsImage.Load(bytes);
            using var output = new MemoryStream();
            await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageResizer.WriteAsync(image, output,
                new() { Mode = NdsImageResizeMode.Trim, VerifyOutput = false }, TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Empty(output.ToArray());
        }
    }

    [Fact]
    public async Task BadChecksumsCanOnlyBeCopiedWhenVerificationIsExplicitlyDisabled()
    {
        byte[] bytes = SyntheticImage.CreateHeaderOnly();
        bytes[0x15E] ^= 1;
        using NdsImage image = NdsImage.Load(bytes);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageResizer.WriteAsync(image, output,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        await NdsImageResizer.WriteAsync(image, output, new() { VerifyOutput = false }, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, output.ToArray());
    }

    [Fact]
    public async Task InvalidTrailingPolicyAndOversizedMemoryOutputFailBeforeMutation()
    {
        byte[] bytes = SyntheticImage.CreateHeaderOnly();
        bytes[0x14] = 15;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x15E), NdsChecksums.ComputeCrc16(bytes.AsSpan(0, 0x15E)));
        using NdsImage image = NdsImage.Load(bytes);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(async () => await NdsImageResizer.WriteAsync(image, output,
            new() { TrailingDataPolicy = (NdsTrailingDataPolicy)99 }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        await Assert.ThrowsAsync<ArgumentException>(async () => await NdsImageResizer.WriteAsync(image, output,
            new() { Mode = NdsImageResizeMode.PadToDeviceCapacity }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Empty(output.ToArray());
        using var readOnly = new MemoryStream(bytes, writable: false);
        await Assert.ThrowsAsync<ArgumentException>(async () => await NdsImageResizer.WriteAsync(image, readOnly,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerificationDetectsCorruptionInPreservedAndAddedBytes(bool padding)
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        using var output = new CorruptingStream(padding ? image.Length : 0x228);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageResizer.WriteAsync(image, output,
            new() { Mode = NdsImageResizeMode.PadToDeviceCapacity }, TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    private sealed class CorruptingStream(long offset) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position <= offset && Position + buffer.Length > offset)
            {
                byte[] changed = buffer.ToArray();
                changed[checked((int)(offset - Position))] ^= 1;
                return base.WriteAsync(changed, cancellationToken);
            }
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}
