using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsDownloadPlaySignatureTests
{
    [Fact]
    public async Task EveryTailLengthIsBoundedAndOnlyACompleteTrailerIsExposed()
    {
        byte[] source = CreateImage();
        for (int length = 0; length <= NdsDownloadPlaySignature.ByteLength; length++)
        {
            byte[] bytes = source[..(0x22D + length)];
            using NdsImage image = NdsImage.Load(bytes);
            using var stream = new MemoryStream(bytes, writable: false);
            using NdsImage asynchronous = await NdsImage.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
            bool complete = length == NdsDownloadPlaySignature.ByteLength;
            Assert.Equal(complete, image.DownloadPlaySignature is not null);
            Assert.Equal(complete, asynchronous.DownloadPlaySignature is not null);
            Assert.Equal(length >= 4 && !complete, image.Validate().Diagnostics.Any(static item => item.Code == "NDS1551"));
            if (complete)
            {
                Assert.Equal(new NdsRegion(0x22D, 136), image.DownloadPlaySignatureRegion);
                Assert.Equal(CreateTrailer(), image.DownloadPlaySignature!.RawData.ToArray());
                Assert.Equal(CreateTrailer()[4..132], image.DownloadPlaySignature.Signature.ToArray());
                Assert.Equal(0x78563412u, image.DownloadPlaySignature.Seed);
            }
        }
    }

    [Fact]
    public void DetectionDoesNotScanOrTrustImpossibleUsedOffsets()
    {
        for (int index = 0; index < 4; index++)
        {
            byte[] bytes = CreateImage();
            bytes[0x22D + index] ^= 0x80;
            using NdsImage image = NdsImage.Load(bytes);
            Assert.Null(image.DownloadPlaySignature);
        }

        byte[] shifted = SyntheticImage.CreateHeaderOnly();
        CreateTrailer().CopyTo(shifted, 0x22E);
        using NdsImage displaced = NdsImage.Load(shifted);
        Assert.Null(displaced.DownloadPlaySignature);
        foreach (uint offset in new[] { 0u, 0x100u, uint.MaxValue })
        {
            byte[] bytes = CreateImage();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80), offset);
            using NdsImage image = NdsImage.Load(bytes);
            Assert.Null(image.DownloadPlaySignature);
        }
    }

    [Fact]
    public void ParsedStoredBytesAreCopiedAndMalformedInputsRejected()
    {
        byte[] bytes = CreateTrailer();
        NdsDownloadPlaySignature signature = NdsDownloadPlaySignature.Parse(bytes);
        bytes[4] ^= 0xFF;
        Assert.Equal(CreateTrailer(), signature.RawData.ToArray());
        Assert.Throws<InvalidDataException>(() => NdsDownloadPlaySignature.Parse(bytes.AsSpan(1)));
        bytes[0] = 0;
        Assert.Throws<InvalidDataException>(() => NdsDownloadPlaySignature.Parse(bytes));
    }

    [Fact]
    public async Task NoOpPreservesAllBytesAndSemanticEditsRelocateTheTrailerWithAWarning()
    {
        byte[] bytes = CreateImage();
        using NdsImage image = NdsImage.Load(bytes);
        using var destination = new MemoryStream();
        NdsSaveResult noOp = await image.Edit().SaveAsync(destination, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(bytes, destination.ToArray());
        Assert.Empty(noOp.Diagnostics);
        NdsSaveResult result = await image.Edit().ReplaceFile("/hello.bin", new byte[0x5000]).SaveAsync(destination,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, static item => item.Code == "NDS1550");
        using NdsImage output = NdsImage.Load(destination.ToArray());
        Assert.NotEqual(image.DownloadPlaySignatureRegion, output.DownloadPlaySignatureRegion);
        Assert.Equal(CreateTrailer(), output.DownloadPlaySignature!.RawData.ToArray());
        Assert.Equal(result.UsedImageSize + NdsDownloadPlaySignature.ByteLength, result.PhysicalImageSize);
    }

    [Theory]
    [InlineData(NdsImageBuildProfile.Deterministic)]
    [InlineData(NdsImageBuildProfile.Ndstool1503)]
    public async Task StructuralBuildPreservesTrailerAndExplicitOmissionIsPossible(NdsImageBuildProfile profile)
    {
        using NdsImage image = NdsImage.Load(CreateImage());
        NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(image, TestContext.Current.CancellationToken);
        using var stream = new MemoryStream();
        NdsImageBuildResult result = await builder.WriteAsync(stream, new() { Profile = profile }, TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, static item => item.Code == "NDS1550");
        using NdsImage output = NdsImage.Load(stream.ToArray());
        Assert.Equal(CreateTrailer(), output.DownloadPlaySignature!.RawData.ToArray());
        Assert.Equal((result.UsedSize + NdsDownloadPlaySignature.ByteLength + 3) & ~3L, result.PhysicalSize);
        builder.DownloadPlaySignature = null;
        using NdsImage omitted = NdsImage.Load(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(omitted.DownloadPlaySignature);
    }

    [Fact]
    public async Task TruncatedTrailerAllowsOnlyExplicitUnverifiedNoOpCopy()
    {
        byte[] bytes = CreateImage()[..0x240];
        using NdsImage image = NdsImage.Load(bytes);
        using var destination = new MemoryStream();
        await image.Edit().SaveAsync(destination, new() { VerifyOutput = false }, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, destination.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(async () => await image.Edit().ReplaceFile("/hello.bin", [1])
            .SaveAsync(destination, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal(bytes, destination.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageBuilder.FromImageAsync(image,
            TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task DsiBuildPlacesAdditionalProgramsAfterTheTrailer()
    {
        using NdsImage source = NdsImage.Load(CreateImage());
        NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken);
        builder.Kind = NdsImageKind.NintendoDsiEnhanced;
        builder.Arm9i = new(NdsProcessor.Arm9i, [9, 8, 7], 0x02400000, 0x02400000);
        builder.Arm7i = new(NdsProcessor.Arm7i, [6, 5, 4], 0x02E80000, 0x02E80000);
        builder.DsiMetadata = new();
        using NdsImage output = NdsImage.Load(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(CreateTrailer(), output.DownloadPlaySignature!.RawData.ToArray());
        Assert.True(output.Header.Arm9i!.Data.Offset >= output.DownloadPlaySignatureRegion!.Value.End);
        using Stream arm9i = output.OpenRead(output.Header.Arm9i.Data);
        byte[] bytes = new byte[3];
        await arm9i.ReadExactlyAsync(bytes, TestContext.Current.CancellationToken);
        Assert.Equal([9, 8, 7], bytes);
    }

    private static byte[] CreateImage()
    {
        byte[] bytes = SyntheticImage.CreateHeaderOnly();
        CreateTrailer().CopyTo(bytes, 0x22D);
        return bytes;
    }

    private static byte[] CreateTrailer()
    {
        byte[] bytes = Enumerable.Range(0, 136).Select(static index => (byte)(index * 29 + 7)).ToArray();
        new byte[] { 0x61, 0x63, 1, 0 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(132), 0x78563412);
        return bytes;
    }
}
