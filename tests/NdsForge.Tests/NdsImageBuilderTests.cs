namespace NdsForge.Tests;

public sealed class NdsImageBuilderTests
{
    [Fact]
    public async Task BuildsDeterministicValidatedImageWithProgramsBannerAndNitroFs()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.FileSystem.AddFile("/data/two.bin", [2]);
        builder.FileSystem.AddFile("/data/one.bin", [1, 1]);
        builder.Banner = new NdsBannerBuilder()
            .SetTitle(NdsBannerLanguage.English, "Builder Test")
            .Build();

        byte[] first = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        byte[] second = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(first);

        Assert.Equal(first, second);
        Assert.True(image.Validate().IsValid);
        Assert.Equal("BUILD TEST", image.Header.Title);
        Assert.Equal("BT01", image.Header.GameCode);
        Assert.Equal("HB", image.Header.MakerCode);
        Assert.Equal("Builder Test", image.Banner!.Titles[NdsBannerLanguage.English]);
        Assert.Equal(["/data/one.bin", "/data/two.bin"], image.FileSystem.Files.Select(static file => file.FullPath));
        Assert.Equal(
            [1, 1],
            await image.FileSystem.GetFile(0).ReadAllBytesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            [0xA9, 0x01, 0x02],
            await ReadRegionAsync(image, image.Header.Arm9.Data, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildsExplicitEmptyNitroFsDirectoriesWithoutFatEntries()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.FileSystem.CreateDirectory("/empty/child");

        byte[] data = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(data);

        Assert.Equal(["/", "/empty", "/empty/child"], image.FileSystem.Directories.Select(static value => value.FullPath));
        Assert.Empty(image.FileSystem.Files);
        Assert.True(image.Header.FileAllocationTable.IsEmpty);
    }

    [Fact]
    public async Task ReportsConcreteLayoutAndLeavesDestinationOpen()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.FileSystem.AddFile("asset.bin", [4, 5, 6]);
        using var destination = new MemoryStream();

        NdsImageBuildResult result = await builder.WriteAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(destination.CanRead);
        Assert.Equal(destination.Length, result.PhysicalSize);
        Assert.Equal(1, result.FileCount);
        Assert.Equal(1, result.AllocationCount);
        Assert.Equal(0x4000, result.Arm9.Offset);
        Assert.True(result.FileAllocationTable.Offset > result.FileNameTable.Offset);
    }

    [Fact]
    public async Task BuildsOverlayTablesWithFileIdsIndependentFromOverlayIds()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.FileSystem.AddFile("named.bin", [1]);
        builder.AddOverlay(new(
            NdsProcessor.Arm9,
            id: 77,
            contents: [7, 7, 7],
            loadAddress: 0x02001000,
            ramSize: 3));
        builder.AddOverlay(new(
            NdsProcessor.Arm7,
            id: 12,
            contents: [8, 8],
            loadAddress: 0x02390000,
            ramSize: 2,
            bssSize: 4));

        byte[] data = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(data);

        Assert.Equal((uint)77, image.Arm9Overlays[0].Id);
        Assert.Equal((uint)1, image.Arm9Overlays[0].FileId);
        Assert.Null(image.Arm9Overlays[0].File);
        Assert.Equal((uint)12, image.Arm7Overlays[0].Id);
        Assert.Equal((uint)2, image.Arm7Overlays[0].FileId);
        Assert.Equal((uint)4, image.Arm7Overlays[0].BssSize);
        Assert.Equal(
            [7, 7, 7],
            await ReadRegionAsync(image, image.Arm9Overlays[0].Data!.Value, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OverlayCanShareNamedNitroFsAllocationWithoutDuplication()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.FileSystem.AddFile("/overlays/shared.bin", [9, 8, 7]);
        builder.AddOverlay(NdsOverlayDefinition.LinkToFile(
            NdsProcessor.Arm9,
            id: 42,
            filePath: "/overlays/shared.bin",
            loadAddress: 0x02002000,
            ramSize: 3));
        using var destination = new MemoryStream();

        NdsImageBuildResult result = await builder.WriteAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(destination.ToArray());

        Assert.Equal(1, result.FileCount);
        Assert.Equal(1, result.AllocationCount);
        Assert.Equal((uint)0, image.Arm9Overlays[0].FileId);
        Assert.Same(image.FileSystem.GetFile("/overlays/shared.bin"), image.Arm9Overlays[0].File);
    }

    [Fact]
    public async Task MissingLinkedOverlayFileFailsBeforeDestinationMutation()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.AddOverlay(NdsOverlayDefinition.LinkToFile(
            NdsProcessor.Arm9,
            id: 1,
            filePath: "/missing.bin",
            loadAddress: 0x02002000,
            ramSize: 1));
        using var destination = new MemoryStream([1, 2, 3], writable: true);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await builder.WriteAsync(destination, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([1, 2, 3], destination.ToArray());
    }

    [Fact]
    public async Task PreservesArm9SdkFooterOutsideDeclaredProgramLength()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.Arm9!.SetFooter([
            0x21, 0x06, 0xC0, 0xDE,
            1, 2, 3, 4,
            5, 6, 7, 8,
        ]);

        byte[] data = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(data);

        Assert.Equal(3, image.Header.Arm9.Data.Length);
        Assert.Equal(new NdsRegion(image.Header.Arm9.Data.End, 12), image.Header.Arm9.Footer);
        Assert.Equal(15, image.Header.Arm9.CompleteData.Length);
        Assert.Equal(
            [0xA9, 0x01, 0x02, 0x21, 0x06, 0xC0, 0xDE, 1, 2, 3, 4, 5, 6, 7, 8],
            await ReadRegionAsync(image, image.Header.Arm9.CompleteData, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsMissingOrMismatchedProgramsBeforeTruncatingDestination()
    {
        var builder = new NdsImageBuilder
        {
            Arm9 = new(NdsProcessor.Arm7, [1], 0x02000000, 0x02000000),
            Arm7 = new(NdsProcessor.Arm7, [2], 0x02380000, 0x02380000),
        };
        using var destination = new MemoryStream([9, 8, 7], writable: true);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await builder.WriteAsync(destination, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], destination.ToArray());
    }

    [Fact]
    public async Task PathBuildRequiresExplicitOverwriteAndCommitsValidImage()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NdsForgeTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "built.nds");
        try
        {
            NdsImageBuilder builder = CreateBuilder();
            await builder.WriteAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<IOException>(async () =>
                await builder.WriteAsync(path, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));

            await builder.WriteAsync(
                path,
                new() { OverwriteDestination = true },
                TestContext.Current.CancellationToken);
            using NdsImage image = await NdsImage.OpenAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(image.Validate().IsValid);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static NdsImageBuilder CreateBuilder() => new()
    {
        Title = "BUILD TEST",
        GameCode = "BT01",
        MakerCode = "HB",
        Version = 2,
        Arm9 = new(NdsProcessor.Arm9, [0xA9, 0x01, 0x02], 0x02000000, 0x02000000),
        Arm7 = new(NdsProcessor.Arm7, [0xA7, 0x03], 0x02380000, 0x02380000),
    };

    private static async ValueTask<byte[]> ReadRegionAsync(
        NdsImage image,
        NdsRegion region,
        CancellationToken cancellationToken)
    {
        using Stream stream = image.OpenRead(region);
        byte[] data = new byte[region.Length];
        await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(true);
        return data;
    }
}
