namespace NdsForge.Tests;

public sealed class NdsDirectoryImportTests
{
    [Fact]
    public async Task ImportsNestedAndEmptyDirectoriesIntoAValidatedImage()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "nested", "empty"));
            await File.WriteAllBytesAsync(
                Path.Combine(root, "nested", "data.bin"),
                [1, 2, 3],
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            var fileSystem = new NdsFileSystemBuilder();

            NdsDirectoryImportResult result = await fileSystem.ImportDirectoryAsync(
                root,
                "/assets",
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(1, result.FilesImported);
            Assert.Equal(3, result.BytesImported);
            Assert.Contains("/assets/nested/empty", fileSystem.Directories);
            Assert.Equal([1, 2, 3], fileSystem.GetFile("/assets/nested/data.bin").Contents.ToArray());
            byte[] bytes = await BuildAsync(fileSystem).ConfigureAwait(true);
            using NdsImage image = NdsImage.Load(bytes);
            Assert.Contains(image.FileSystem.Directories, static value => value.FullPath == "/assets/nested/empty");
            Assert.True(image.Validate().IsValid);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CollisionPoliciesFailTransactionallyKeepOrReplace()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "same.bin"), [9], TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(Path.Combine(root, "other.bin"), [8, 7], TestContext.Current.CancellationToken).ConfigureAwait(true);
            var fileSystem = new NdsFileSystemBuilder();
            fileSystem.AddFile("/same.bin", [1]);

            await Assert.ThrowsAsync<IOException>(async () =>
                await fileSystem.ImportDirectoryAsync(
                    root,
                    cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Single(fileSystem.Files);
            Assert.Throws<FileNotFoundException>(() => fileSystem.GetFile("/other.bin"));

            NdsDirectoryImportResult kept = await fileSystem.ImportDirectoryAsync(
                root,
                options: new() { CollisionPolicy = NdsFileCollisionPolicy.KeepExisting },
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal([1], fileSystem.GetFile("/same.bin").Contents.ToArray());
            Assert.Equal(1, kept.EntriesSkipped);
            Assert.Equal(2, kept.BytesImported);

            NdsDirectoryImportResult replaced = await fileSystem.ImportDirectoryAsync(
                root,
                options: new() { CollisionPolicy = NdsFileCollisionPolicy.Replace },
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal([9], fileSystem.GetFile("/same.bin").Contents.ToArray());
            Assert.Equal(2, replaced.FilesImported);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ResourceFailureAndCancellationLeaveBuilderUnchanged()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "large.bin"), new byte[32], TestContext.Current.CancellationToken).ConfigureAwait(true);
            var fileSystem = new NdsFileSystemBuilder();
            fileSystem.AddFile("/existing.bin", [1]);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await fileSystem.ImportDirectoryAsync(
                    root,
                    options: new() { MaximumTotalBytes = 16 },
                    cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await fileSystem.ImportDirectoryAsync(root, cancellationToken: cancellation.Token).ConfigureAwait(true));

            Assert.Single(fileSystem.Files);
            Assert.Equal([1], fileSystem.GetFile("/existing.bin").Contents.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RepeatedImportsComposeMultipleRootsDeterministically()
    {
        string first = CreateTemporaryDirectory();
        string second = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(first, "a.bin"), [1], TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllBytesAsync(Path.Combine(second, "b.bin"), [2], TestContext.Current.CancellationToken).ConfigureAwait(true);
            var fileSystem = new NdsFileSystemBuilder();

            await fileSystem.ImportDirectoryAsync(first, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            await fileSystem.ImportDirectoryAsync(second, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(["/a.bin", "/b.bin"], fileSystem.Files.Select(static value => value.Path).ToArray());
            byte[] firstBuild = await BuildAsync(fileSystem).ConfigureAwait(true);
            byte[] secondBuild = await BuildAsync(fileSystem).ConfigureAwait(true);
            Assert.Equal(firstBuild, secondBuild);
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ndsforge-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static ValueTask<byte[]> BuildAsync(NdsFileSystemBuilder fileSystem)
    {
        var builder = new NdsImageBuilder
        {
            GameCode = "IM01",
            MakerCode = "HB",
            Arm9 = new(NdsProcessor.Arm9, [1, 2], 0x0200_0000, 0x0200_0000),
            Arm7 = new(NdsProcessor.Arm7, [3, 4], 0x0238_0000, 0x0238_0000),
        };
        foreach (string directory in fileSystem.Directories)
        {
            builder.FileSystem.CreateDirectory(directory);
        }

        foreach (NdsBuildFile file in fileSystem.Files)
        {
            builder.FileSystem.AddFile(file.Path, file.Contents.Span);
        }

        return builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
    }
}
