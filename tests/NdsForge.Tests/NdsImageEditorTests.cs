namespace NdsForge.Tests;

public sealed class NdsImageEditorTests
{
    [Fact]
    public async Task NoOpSaveIsByteIdentical()
    {
        byte[] source = SyntheticImage.CreateWithBanner();
        using NdsImage image = NdsImage.Load(source);
        using var destination = new MemoryStream();

        NdsSaveResult result = await image.Edit().SaveAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, result.AppliedChanges);
        Assert.Equal(source, destination.ToArray());
    }

    [Fact]
    public async Task SameSizeReplacementReusesAllocation()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        using var destination = new MemoryStream();
        NdsImageEditor editor = image.Edit().ReplaceFile("/hello.bin", "world"u8);

        NdsSaveResult result = await editor.SaveAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using NdsImage output = NdsImage.Load(destination.ToArray());
        byte[] contents = await output.FileSystem.GetFile("hello.bin")
            .ReadAllBytesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, result.RelocatedFiles);
        Assert.Equal(new NdsRegion(0x228, 5), output.FileSystem.GetFile(0).Data);
        Assert.Equal("world"u8.ToArray(), contents);
    }

    [Fact]
    public async Task EnlargedReplacementRelocatesAndRepairsMetadata()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        using var destination = new MemoryStream();
        byte[] replacement = Enumerable.Repeat((byte)0x5A, 700).ToArray();
        NdsImageEditor editor = image.Edit().ReplaceFile("hello.bin", replacement);

        NdsSaveResult result = await editor.SaveAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using NdsImage output = NdsImage.Load(destination.ToArray());
        byte[] observed = await output.FileSystem.GetFile(0)
            .ReadAllBytesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, result.RelocatedFiles);
        Assert.Equal(0x400, output.FileSystem.GetFile(0).Data.Offset);
        Assert.Equal(0x6BCu, output.Header.UsedImageSize);
        Assert.Equal(replacement, observed);
        Assert.True(output.Validate().IsValid);
    }

    [Fact]
    public void ChangesExposeRelocationPlan()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());

        NdsFileChange change = Assert.Single(image.Edit().ReplaceFile("hello.bin", "expanded"u8).Changes);

        Assert.Equal(0, change.FileId);
        Assert.Equal("/hello.bin", change.Path);
        Assert.Equal(5, change.OriginalLength);
        Assert.Equal(8, change.ReplacementLength);
        Assert.True(change.RequiresRelocation);
    }
}
