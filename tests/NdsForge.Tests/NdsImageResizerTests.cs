using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsImageResizerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public async Task TrimAndExpandPreserveTheCompleteDeclaredPrefix(byte padding)
    {
        byte[] bytes = CreatePadded(padding);
        using NdsImage source = NdsImage.Load(bytes);
        using var stream = new MemoryStream();
        NdsImageResizeResult trimmed = await NdsImageResizer.WriteAsync(source, stream,
            new() { Mode = NdsImageResizeMode.Trim, PaddingByte = padding }, TestContext.Current.CancellationToken);
        Assert.Equal(bytes[..0x22D], stream.ToArray());
        Assert.Equal(new NdsRegion(0x22D, bytes.Length - 0x22D), trimmed.RemovedData);
        Assert.Null(trimmed.AddedData);
        Assert.Empty(trimmed.Diagnostics);
        using NdsImage compact = NdsImage.Load(stream.ToArray());
        using var expanded = new MemoryStream();
        NdsImageResizeResult result = await NdsImageResizer.WriteAsync(compact, expanded,
            new() { Mode = NdsImageResizeMode.PadToDeviceCapacity, PaddingByte = padding }, TestContext.Current.CancellationToken);
        Assert.Equal(0x20000, result.OutputLength);
        Assert.Equal(new NdsRegion(0x22D, 0x20000 - 0x22D), result.AddedData);
        Assert.Null(result.RemovedData);
        byte[] output = expanded.ToArray();
        Assert.Equal(bytes[..0x22D], output[..0x22D]);
        Assert.True(output.AsSpan(0x22D).IndexOfAnyExcept(padding) < 0);
        using NdsImage full = NdsImage.Load(output);
        Assert.True(full.Validate().IsValid);
        Assert.Equal(0x22Du, full.Header.UsedImageSize);
        Assert.Equal(0, full.Header.DeviceCapacityExponent);
    }

    [Fact]
    public async Task PreservationDoesNotClassifyOrRemoveArbitraryTrailingBytes()
    {
        byte[] bytes = CreatePadded(255);
        bytes[^1] = 37;
        using NdsImage image = NdsImage.Load(bytes);
        using var output = new MemoryStream();
        NdsImageResizeResult result = await NdsImageResizer.WriteAsync(image, output, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(bytes.Length, result.InputLength);
        Assert.Equal(result.InputLength, result.OutputLength);
        Assert.Null(result.AddedData);
        Assert.Null(result.RemovedData);
    }

    [Fact]
    public async Task NonPaddingRejectsBeforeMutationUnlessDiscardIsExplicit()
    {
        byte[] bytes = CreatePadded(255);
        bytes[^1] = 37;
        using NdsImage image = NdsImage.Load(bytes);
        using var output = new MemoryStream();
        output.Write([9, 8, 7]);
        var options = new NdsImageResizeOptions { Mode = NdsImageResizeMode.Trim };
        await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageResizer.WriteAsync(image, output,
            options, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], output.ToArray());
        Assert.Equal(3, output.Position);
        NdsImageResizeResult result = await NdsImageResizer.WriteAsync(image, output,
            options with { TrailingDataPolicy = NdsTrailingDataPolicy.Discard }, TestContext.Current.CancellationToken);
        Assert.Equal(bytes[..0x22D], output.ToArray());
        Assert.Equal("NDS1580", Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData(0x300)]
    [InlineData(0x4000)]
    [InlineData(0x5000)]
    public async Task ExactLengthDoesNotChangeCapacityOrUsedFields(long length)
    {
        byte[] bytes = CreatePadded(255);
        using NdsImage source = NdsImage.Load(bytes);
        using var output = new MemoryStream();
        NdsImageResizeResult result = await NdsImageResizer.WriteAsync(source, output,
            new() { Mode = NdsImageResizeMode.ExactLength, OutputLengthBytes = length }, TestContext.Current.CancellationToken);
        Assert.Equal(length, result.OutputLength);
        byte[] resized = output.ToArray();
        Assert.Equal(bytes[..0x22D], resized[..0x22D]);
        Assert.True(resized.AsSpan(0x22D).IndexOfAnyExcept((byte)255) < 0);
    }

    [Theory]
    [InlineData(NdsImageResizeMode.ExactLength, null)]
    [InlineData(NdsImageResizeMode.Preserve, 1024L)]
    [InlineData(NdsImageResizeMode.Trim, 1024L)]
    [InlineData(NdsImageResizeMode.ExactLength, 0L)]
    [InlineData(NdsImageResizeMode.ExactLength, -1L)]
    [InlineData(NdsImageResizeMode.ExactLength, 0x100000001L)]
    [InlineData(NdsImageResizeMode.ExactLength, 0x22CL)]
    [InlineData(NdsImageResizeMode.ExactLength, 0x20001L)]
    [InlineData((NdsImageResizeMode)99, null)]
    public async Task ContradictoryOrDestructiveLengthsRejectWithoutWriting(NdsImageResizeMode mode, long? length)
    {
        using NdsImage source = NdsImage.Load(CreatePadded(255));
        using var output = new MemoryStream();
        output.Write([9, 8, 7]);
        await Assert.ThrowsAsync<ArgumentException>(async () => await NdsImageResizer.WriteAsync(source, output,
            new() { Mode = mode, OutputLengthBytes = length }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], output.ToArray());
    }

    [Fact]
    public async Task PaddingModeDoesNotSilentlyShrinkAnOversizedInput()
    {
        byte[] bytes = new byte[0x20001];
        CreatePadded(255).CopyTo(bytes, 0);
        using NdsImage source = NdsImage.Load(bytes);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(async () => await NdsImageResizer.WriteAsync(source, output,
            new() { Mode = NdsImageResizeMode.PadToDeviceCapacity }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedExtentCannotBeResizedEvenWithoutOutputVerification(bool verify)
    {
        byte[] bytes = CreatePadded(255);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80), 0x5000);
        using NdsImage source = NdsImage.Load(bytes);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageResizer.WriteAsync(source, output,
            new() { Mode = NdsImageResizeMode.Trim, VerifyOutput = verify }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Empty(output.ToArray());
        await NdsImageResizer.WriteAsync(source, output, new() { VerifyOutput = false }, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, output.ToArray());
    }

    [Fact]
    public async Task RecognizedTrailerIsRetainedAndTruncationIsNotRepairedByPadding()
    {
        byte[] bytes = CreatePadded(255);
        new byte[] { 0x61, 0x63, 1, 0 }.CopyTo(bytes, 0x22D);
        using NdsImage source = NdsImage.Load(bytes);
        using var output = new MemoryStream();
        await NdsImageResizer.WriteAsync(source, output, new() { Mode = NdsImageResizeMode.Trim }, TestContext.Current.CancellationToken);
        Assert.Equal(bytes[..(0x22D + 136)], output.ToArray());
        using NdsImage truncated = NdsImage.Load(bytes.AsMemory(0, 0x231));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageResizer.WriteAsync(truncated, output,
            new() { Mode = NdsImageResizeMode.PadToDeviceCapacity, VerifyOutput = false }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal(bytes[..(0x22D + 136)], output.ToArray());
    }

    [Fact]
    public async Task DeclaredFfPayloadIsNeverTrimmedAsPadding()
    {
        byte[] bytes = CreatePadded(255);
        bytes.AsSpan(0x228, 5).Fill(255);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80), 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x15E), NdsChecksums.ComputeCrc16(bytes.AsSpan(0, 0x15E)));
        using NdsImage source = NdsImage.Load(bytes);
        using var output = new MemoryStream();
        await NdsImageResizer.WriteAsync(source, output, new() { Mode = NdsImageResizeMode.Trim }, TestContext.Current.CancellationToken);
        Assert.Equal(0x22D, output.Length);
        Assert.Equal(bytes[..0x22D], output.ToArray());
    }

    [Fact]
    public async Task SourceStreamAliasingDisposalAndCancellationRejectBeforeWriting()
    {
        byte[] bytes = CreatePadded(255);
        using var sourceStream = new MemoryStream(bytes, writable: true);
        using NdsImage source = await NdsImage.OpenAsync(sourceStream, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ArgumentException>(async () => await NdsImageResizer.WriteAsync(source, sourceStream,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal(bytes, sourceStream.ToArray());
        using var output = new MemoryStream();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await NdsImageResizer.WriteAsync(source, output,
            cancellationToken: cancelled.Token).ConfigureAwait(true));
        await source.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await NdsImageResizer.WriteAsync(source, output,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Empty(output.ToArray());
    }

    private static byte[] CreatePadded(byte padding)
    {
        byte[] bytes = SyntheticImage.CreateHeaderOnly();
        bytes.AsSpan(0x22D).Fill(padding);
        return bytes;
    }
}
