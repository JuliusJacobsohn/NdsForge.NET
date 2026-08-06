namespace NdsForge.Tests;

public sealed class NdsExtractionTests
{
    [Fact]
    public async Task ExtractsSelectedComponentsAndNitroFs()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ndsforge-extract-{Guid.NewGuid():N}");
        try
        {
            using NdsImage image = NdsImage.Load(SyntheticImage.CreateWithBanner());

            NdsExtractionResult result = await image.ExtractAsync(
                directory,
                new()
                {
                    Components = NdsImageComponent.Header |
                        NdsImageComponent.Banner |
                        NdsImageComponent.NitroFileSystem,
                },
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(3, result.WrittenFiles);
            Assert.Equal("hello"u8.ToArray(), await File.ReadAllBytesAsync(
                Path.Combine(directory, "data", "hello.bin"),
                TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal(0x200, new FileInfo(Path.Combine(directory, "header.bin")).Length);
            Assert.Equal(0x840, new FileInfo(Path.Combine(directory, "banner.bin")).Length);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExistingFileFailsByDefaultAndCanBeSkipped()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ndsforge-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "data"));
        string file = Path.Combine(directory, "data", "hello.bin");
        await File.WriteAllTextAsync(
            file,
            "keep",
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        try
        {
            using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());

            await Assert.ThrowsAsync<IOException>(async () => await image.ExtractAsync(
                directory,
                new() { Components = NdsImageComponent.NitroFileSystem },
                TestContext.Current.CancellationToken).ConfigureAwait(true));
            NdsExtractionResult result = await image.ExtractAsync(
                directory,
                new()
                {
                    Components = NdsImageComponent.NitroFileSystem,
                    OverwritePolicy = NdsOverwritePolicy.Skip,
                },
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(1, result.SkippedFiles);
            Assert.Equal("keep", await File.ReadAllTextAsync(
                file,
                TestContext.Current.CancellationToken).ConfigureAwait(true));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
