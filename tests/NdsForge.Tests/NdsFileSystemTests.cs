namespace NdsForge.Tests;

public sealed class NdsFileSystemTests
{
    [Fact]
    public async Task ParsesAndReadsNamedFile()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());

        NdsFile file = image.FileSystem.GetFile("hello.bin");
        byte[] contents = await file.ReadAllBytesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, file.Id);
        Assert.Equal("/hello.bin", file.FullPath);
        Assert.Equal("hello"u8.ToArray(), contents);
        Assert.Same(file, image.FileSystem.GetFile("/hello.bin"));
    }

    [Fact]
    public async Task EightBitNameBytesSurviveStructuralRebuild()
    {
        byte[] data = SyntheticImage.CreateHeaderOnly();
        data[0x211] = 0x91;
        using NdsImage source = NdsImage.Load(data);
        NdsFile sourceFile = Assert.Single(source.FileSystem.Files);
        Assert.Equal('\u0091', sourceFile.Name[0]);

        NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(
            source,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        byte[] rebuiltBytes = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using NdsImage rebuilt = NdsImage.Load(rebuiltBytes);

        Assert.Equal(sourceFile.Name, Assert.Single(rebuilt.FileSystem.Files).Name);
    }

    [Fact]
    public void FileStreamIsBoundedAndSeekable()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        using Stream stream = image.FileSystem.GetFile(0).OpenRead();
        stream.Seek(-2, SeekOrigin.End);
        Span<byte> result = stackalloc byte[4];

        int count = stream.Read(result);

        Assert.Equal(2, count);
        Assert.Equal("lo"u8.ToArray(), result[..count].ToArray());
    }

    [Theory]
    [InlineData("../hello.bin")]
    [InlineData("/folder/../hello.bin")]
    [InlineData("/hello.bin/")]
    public void RejectsAmbiguousLookupPaths(string path)
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());

        Assert.Throws<ArgumentException>(() => image.FileSystem.GetFile(path));
    }

    [Fact]
    public void RejectsFatRangeOutsideImage()
    {
        byte[] data = SyntheticImage.CreateHeaderOnly();
        data[0x227] = 0x7F;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => NdsImage.Load(data));

        Assert.Contains("FAT entry 0", exception.Message, StringComparison.Ordinal);
    }
}
