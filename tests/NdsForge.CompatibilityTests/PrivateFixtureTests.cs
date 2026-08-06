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

    [Fact]
    public async Task NoOpSaveIsByteIdenticalToPrivateFixture()
    {
        string path = GetFixturePath();
        string output = Path.Combine(Path.GetTempPath(), $"ndsforge-noop-{Guid.NewGuid():N}.nds");
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            using NdsImage image = await NdsImage.OpenAsync(
                path,
                cancellationToken: cancellationToken).ConfigureAwait(true);

            await image.Edit().SaveAsync(output, cancellationToken: cancellationToken).ConfigureAwait(true);

            Assert.Equal(new FileInfo(path).Length, new FileInfo(output).Length);
            using FileStream expected = File.OpenRead(path);
            using FileStream actual = File.OpenRead(output);
            byte[] expectedBuffer = new byte[128 * 1024];
            byte[] actualBuffer = new byte[128 * 1024];
            while (true)
            {
                int expectedCount = await expected.ReadAsync(expectedBuffer, cancellationToken).ConfigureAwait(true);
                int actualCount = await actual.ReadAsync(actualBuffer, cancellationToken).ConfigureAwait(true);
                Assert.Equal(expectedCount, actualCount);
                if (expectedCount == 0)
                {
                    break;
                }

                Assert.True(expectedBuffer.AsSpan(0, expectedCount).SequenceEqual(actualBuffer.AsSpan(0, actualCount)));
            }
        }
        finally
        {
            File.Delete(output);
        }
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
