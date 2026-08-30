namespace NdsForge.Tests;

public sealed class NdsImageResizePathTests
{
    [Fact]
    public async Task LinkedDestinationAndAncestorAreRejectedWithoutTouchingTargets()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NdsForgeTests", Guid.NewGuid().ToString("N"));
        string targetDirectory = Path.Combine(directory, "target");
        string target = Path.Combine(targetDirectory, "keep.bin");
        string fileLink = Path.Combine(directory, "linked.nds");
        string directoryLink = Path.Combine(directory, "redirect");
        Directory.CreateDirectory(targetDirectory);
        try
        {
            await File.WriteAllBytesAsync(target, new byte[] { 9, 8, 7 }, TestContext.Current.CancellationToken);
            try
            {
                File.CreateSymbolicLink(fileLink, target);
                Directory.CreateSymbolicLink(directoryLink, targetDirectory);
            }
            catch (UnauthorizedAccessException) { Assert.Skip("Creating symbolic links requires host permission for this filesystem test."); }
            using NdsImage source = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
            foreach (string path in new[] { fileLink, Path.Combine(directoryLink, "new.nds") })
            {
                await Assert.ThrowsAsync<IOException>(async () => await NdsImageResizer.WriteFileAsync(source, path,
                    new() { OverwriteDestination = true }, TestContext.Current.CancellationToken).ConfigureAwait(true));
            }
            Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFiles(targetDirectory));
        }
        finally
        {
            File.Delete(fileLink);
            if (Directory.Exists(directoryLink)) { Directory.Delete(directoryLink); }
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PathWritesRequireExplicitOverwriteAndPreserveTargetsOnFailure()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NdsForgeTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "resized.nds");
        try
        {
            using NdsImage source = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
            NdsImageResizeResult initial = await NdsImageResizer.WriteFileAsync(source, path, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(0x4000, initial.OutputLength);
            await Assert.ThrowsAsync<IOException>(async () => await NdsImageResizer.WriteFileAsync(source, path,
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageResizer.WriteFileAsync(source, path,
                new() { Mode = NdsImageResizeMode.Trim, OverwriteDestination = true }, TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal(SyntheticImage.CreateHeaderOnly(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFiles(directory));
            await NdsImageResizer.WriteFileAsync(source, path,
                new() { Mode = NdsImageResizeMode.Trim, PaddingByte = 0, OverwriteDestination = true }, TestContext.Current.CancellationToken);
            Assert.Equal(0x22D, new FileInfo(path).Length);
            await Assert.ThrowsAsync<IOException>(async () => await NdsImageResizer.WriteFileAsync(source, directory,
                new() { OverwriteDestination = true }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); } }
    }

    [Fact]
    public async Task CancelledPathWritesDoNotCreateOutput()
    {
        string path = Path.Combine(Path.GetTempPath(), $"NdsForge-resize-{Guid.NewGuid():N}.nds");
        using NdsImage source = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await NdsImageResizer.WriteFileAsync(source, path,
            cancellationToken: cancelled.Token).ConfigureAwait(true));
        Assert.False(File.Exists(path));
    }
}
