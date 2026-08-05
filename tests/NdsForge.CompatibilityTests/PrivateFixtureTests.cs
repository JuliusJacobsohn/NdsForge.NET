namespace NdsForge.CompatibilityTests;

public sealed class PrivateFixtureTests
{
    [Fact]
    public async Task PrivateFixtureParsesAndReadsNitroFs()
    {
        string path = GetFixturePath();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        using NdsImage image = await NdsImage.OpenAsync(
            path,
            cancellationToken: cancellationToken).ConfigureAwait(true);
        NdsFile firstFile = Assert.Single(image.FileSystem.Files.Take(1));
        byte[] contents = await firstFile.ReadAllBytesAsync(cancellationToken).ConfigureAwait(true);

        Assert.NotEmpty(image.Header.GameCode);
        Assert.True(image.FileSystem.Files.Count > 0);
        Assert.Equal(firstFile.Data.Length, contents.LongLength);
        Assert.True(image.Validate().IsValid);
    }

    private static string GetFixturePath()
    {
        string? path = Environment.GetEnvironmentVariable("NDSFORGE_TEST_ROM");
        if (string.IsNullOrWhiteSpace(path))
        {
            Assert.Skip("Set NDSFORGE_TEST_ROM to a legally obtained local image to run compatibility tests.");
        }

        Assert.True(File.Exists(path), $"Private fixture does not exist: {path}");
        return path;
    }
}
