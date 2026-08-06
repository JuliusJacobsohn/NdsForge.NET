using System.Security.Cryptography;

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

    [Fact]
    public async Task StructuralRebuildPreservesRealFixtureSemantics()
    {
        string path = GetFixturePath();
        string output = Path.Combine(Path.GetTempPath(), $"ndsforge-rebuild-{Guid.NewGuid():N}.nds");
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            using NdsImage source = await NdsImage.OpenAsync(path, cancellationToken: cancellationToken).ConfigureAwait(true);
            NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(source, cancellationToken).ConfigureAwait(true);

            await builder.WriteAsync(output, cancellationToken: cancellationToken).ConfigureAwait(true);
            using NdsImage rebuilt = await NdsImage.OpenAsync(output, cancellationToken: cancellationToken).ConfigureAwait(true);

            Assert.True(rebuilt.Validate().IsValid);
            Assert.Equal(source.Header.Title, rebuilt.Header.Title);
            Assert.Equal(source.Header.GameCode, rebuilt.Header.GameCode);
            Assert.Equal(
                source.FileSystem.Directories.Select(static directory => directory.FullPath).Order(StringComparer.Ordinal),
                rebuilt.FileSystem.Directories.Select(static directory => directory.FullPath).Order(StringComparer.Ordinal));
            Assert.Equal(
                source.FileSystem.Files.Select(static file => file.FullPath).Order(StringComparer.Ordinal),
                rebuilt.FileSystem.Files.Select(static file => file.FullPath).Order(StringComparer.Ordinal));
            Assert.Equal(source.Arm9Overlays.Select(static overlay => overlay.Id), rebuilt.Arm9Overlays.Select(static overlay => overlay.Id));
            Assert.Equal(source.Arm7Overlays.Select(static overlay => overlay.Id), rebuilt.Arm7Overlays.Select(static overlay => overlay.Id));
            Assert.Equal(source.Banner?.RawData.ToArray(), rebuilt.Banner?.RawData.ToArray());

            await AssertRegionHashEqualAsync(source, source.Header.Arm9.CompleteData, rebuilt, rebuilt.Header.Arm9.CompleteData, cancellationToken)
                .ConfigureAwait(true);
            await AssertRegionHashEqualAsync(source, source.Header.Arm7.Data, rebuilt, rebuilt.Header.Arm7.Data, cancellationToken)
                .ConfigureAwait(true);
            foreach (NdsFile sourceFile in source.FileSystem.Files)
            {
                NdsFile rebuiltFile = rebuilt.FileSystem.GetFile(sourceFile.FullPath);
                await AssertRegionHashEqualAsync(source, sourceFile.Data, rebuilt, rebuiltFile.Data, cancellationToken)
                    .ConfigureAwait(true);
            }
        }
        finally
        {
            File.Delete(output);
        }
    }

    private static async ValueTask AssertRegionHashEqualAsync(
        NdsImage expectedImage,
        NdsRegion expectedRegion,
        NdsImage actualImage,
        NdsRegion actualRegion,
        CancellationToken cancellationToken)
    {
        Assert.Equal(expectedRegion.Length, actualRegion.Length);
        using Stream expected = expectedImage.OpenRead(expectedRegion);
        using Stream actual = actualImage.OpenRead(actualRegion);
        byte[] expectedHash = await SHA256.HashDataAsync(expected, cancellationToken).ConfigureAwait(true);
        byte[] actualHash = await SHA256.HashDataAsync(actual, cancellationToken).ConfigureAwait(true);
        Assert.Equal(expectedHash, actualHash);
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
