namespace NdsForge.Tests;

public sealed class NdsEmptyFileExtentTests
{
    [Theory]
    [InlineData(NdsImageBuildProfile.Deterministic)]
    [InlineData(NdsImageBuildProfile.Ndstool1503)]
    public async Task FinalEmptyFileRemainsWithinPhysicalAndDeclaredExtent(NdsImageBuildProfile profile)
    {
        var builder = new NdsImageBuilder
        {
            Title = "EMPTY TAIL",
            GameCode = "ET01",
            MakerCode = "HB",
            Arm9 = new(NdsProcessor.Arm9, [0xA9, 1, 2], 0x02000000, 0x02000000),
            Arm7 = new(NdsProcessor.Arm7, [0xA7, 3], 0x02380000, 0x02380000),
        };
        builder.FileSystem.AddFile("/a.bin", [1]);
        builder.FileSystem.AddFile("/z.bin", []);

        byte[] data = await builder.BuildAsync(
            new NdsImageBuildOptions { Profile = profile }, TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(data);
        NdsFile payload = image.FileSystem.Files.Single(static file => file.FullPath == "/a.bin");
        NdsFile empty = image.FileSystem.Files.Single(static file => file.FullPath == "/z.bin");

        Assert.Equal(0, empty.Data.Length);
        int alignment = profile == NdsImageBuildProfile.Ndstool1503 ? 0x200 : 4;
        byte padding = profile == NdsImageBuildProfile.Ndstool1503 ? (byte)0 : (byte)0xFF;
        Assert.Equal((payload.Data.End + alignment - 1) & ~(alignment - 1L), empty.Data.Offset);
        Assert.Equal(empty.Data.Offset, image.Length);
        Assert.Equal(image.Length, image.Header.UsedImageSize);
        Assert.All(data.AsSpan(checked((int)payload.Data.End)).ToArray(), value => Assert.Equal(padding, value));
        Assert.True(image.Validate().IsValid);
        Assert.Empty(await empty.ReadAllBytesAsync(TestContext.Current.CancellationToken));
    }
}
