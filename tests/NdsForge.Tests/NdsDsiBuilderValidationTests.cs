namespace NdsForge.Tests;

public sealed class NdsDsiBuilderValidationTests
{
    [Fact]
    public async Task IncompleteDsiRecipeFailsBeforeDestinationMutation()
    {
        NdsImageBuilder builder = CreateDsBuilder();
        builder.Kind = NdsImageKind.NintendoDsiExclusive;
        builder.DsiMetadata = new();
        using var destination = new MemoryStream([9, 8, 7], writable: true);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await builder.WriteAsync(destination, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));

        Assert.Equal([9, 8, 7], destination.ToArray());
    }

    [Fact]
    public async Task DsRecipeRejectsUnencodedDsiMetadata()
    {
        NdsImageBuilder builder = CreateDsBuilder();
        builder.DsiMetadata = new();
        using var destination = new MemoryStream([6, 5, 4], writable: true);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await builder.WriteAsync(destination, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));

        Assert.Equal([6, 5, 4], destination.ToArray());
    }

    [Fact]
    public async Task DigestHierarchyRequiresExplicitHmacKey()
    {
        NdsImageBuilder builder = CreateDsiBuilder();
        builder.DsiMetadata!.Digests = new();
        using var destination = new MemoryStream([3, 2, 1], writable: true);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await builder.WriteAsync(destination, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));

        Assert.Equal([3, 2, 1], destination.ToArray());
    }

    [Fact]
    public async Task DsiExclusiveKindRoundTripsWithoutImplicitDowngrade()
    {
        NdsImageBuilder builder = CreateDsiBuilder();
        builder.Kind = NdsImageKind.NintendoDsiExclusive;

        byte[] data = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(data);

        Assert.Equal(NdsImageKind.NintendoDsiExclusive, image.Header.Kind);
        Assert.Equal(NdsProcessor.Arm9i, image.Header.Arm9i!.Processor);
        Assert.Equal(NdsProcessor.Arm7i, image.Header.Arm7i!.Processor);
        Assert.True(image.Validate().IsValid);
    }

    private static NdsImageBuilder CreateDsBuilder() => new()
    {
        Title = "DSI TEST",
        GameCode = "DT01",
        MakerCode = "HB",
        Arm9 = new(NdsProcessor.Arm9, [1], 0x02000000, 0x02000000),
        Arm7 = new(NdsProcessor.Arm7, [2], 0x02380000, 0x02380000),
    };

    private static NdsImageBuilder CreateDsiBuilder()
    {
        NdsImageBuilder builder = CreateDsBuilder();
        builder.Kind = NdsImageKind.NintendoDsiEnhanced;
        builder.Arm9i = new(NdsProcessor.Arm9i, [3], 0x02E00000, 0x02E00000);
        builder.Arm7i = new(NdsProcessor.Arm7i, [4], 0x02E80000, 0x02E80000);
        builder.DsiMetadata = new();
        return builder;
    }
}
