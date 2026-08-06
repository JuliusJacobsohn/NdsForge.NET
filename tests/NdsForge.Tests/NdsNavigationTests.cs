namespace NdsForge.Tests;

public sealed class NdsNavigationTests
{
    [Fact]
    public async Task CopiesFilesAndArbitraryRegionsWithoutClosingDestinations()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        NdsFile file = image.FileSystem.GetFile("/hello.bin");
        using var fileDestination = new MemoryStream();
        using var regionDestination = new MemoryStream();

        await file.CopyToAsync(fileDestination, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await image.CopyToAsync(file.Data, regionDestination, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(fileDestination.CanWrite);
        Assert.True(regionDestination.CanWrite);
        Assert.Equal("hello"u8.ToArray(), fileDestination.ToArray());
        Assert.Equal(fileDestination.ToArray(), regionDestination.ToArray());
    }

    [Fact]
    public async Task ResolvesDirectoriesAndTraversesDepthFirst()
    {
        var builder = new NdsImageBuilder
        {
            GameCode = "NV01",
            MakerCode = "HB",
            Arm9 = new(NdsProcessor.Arm9, [1], 0x0200_0000, 0x0200_0000),
            Arm7 = new(NdsProcessor.Arm7, [2], 0x0238_0000, 0x0238_0000),
        };
        builder.FileSystem.AddFile("/root.bin", [1]);
        builder.FileSystem.AddFile("/a/first.bin", [2]);
        builder.FileSystem.AddFile("/a/b/second.bin", [3]);
        builder.FileSystem.CreateDirectory("/empty");
        byte[] bytes = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using NdsImage image = NdsImage.Load(bytes);

        Assert.Same(image.FileSystem.Root, image.FileSystem.GetDirectory("/"));
        Assert.True(image.FileSystem.TryGetDirectory("a/", out NdsDirectory? directory));
        Assert.Equal("/a", directory.FullPath);
        Assert.False(image.FileSystem.TryGetDirectory("/missing", out _));
        Assert.Equal(["/root.bin"], image.FileSystem.Root.EnumerateFiles(recursive: false).Select(static file => file.FullPath));
        Assert.Equal(
            ["/root.bin", "/a/first.bin", "/a/b/second.bin"],
            image.FileSystem.Root.EnumerateFiles().Select(static file => file.FullPath));
        Assert.Equal(
            ["/a", "/a/b", "/empty"],
            image.FileSystem.Root.EnumerateDirectories().Select(static value => value.FullPath));
    }

    [Fact]
    public void RejectsInvalidDirectoryAndUnwritableCopyDestinations()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        Assert.Throws<ArgumentException>(() => image.FileSystem.GetDirectory("/../bad"));
        Assert.Throws<DirectoryNotFoundException>(() => image.FileSystem.GetDirectory("/missing"));
        using var readOnly = new MemoryStream([], writable: false);
        Assert.Throws<ArgumentException>(() => image.FileSystem.GetFile(0)
            .CopyToAsync(readOnly, TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult());
        Assert.Throws<ArgumentException>(() => image.CopyToAsync(new(0, 1), readOnly, TestContext.Current.CancellationToken)
            .AsTask().GetAwaiter().GetResult());
    }
}
