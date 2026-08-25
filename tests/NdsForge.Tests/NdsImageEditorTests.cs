namespace NdsForge.Tests;

public sealed class NdsImageEditorTests
{
    [Fact]
    public async Task NoOpSaveIsByteIdentical()
    {
        byte[] source = SyntheticImage.CreateWithBanner();
        using NdsImage image = NdsImage.Load(source);
        using var destination = new MemoryStream();

        NdsSaveResult result = await image.Edit().SaveAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, result.AppliedChanges);
        Assert.Equal(source, destination.ToArray());
    }

    [Fact]
    public async Task SameSizeReplacementReusesAllocation()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        using var destination = new MemoryStream();
        NdsImageEditor editor = image.Edit().ReplaceFile("/hello.bin", "world"u8);

        NdsSaveResult result = await editor.SaveAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using NdsImage output = NdsImage.Load(destination.ToArray());
        byte[] contents = await output.FileSystem.GetFile("hello.bin")
            .ReadAllBytesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, result.RelocatedFiles);
        Assert.Equal(new NdsRegion(0x228, 5), output.FileSystem.GetFile(0).Data);
        Assert.Equal("world"u8.ToArray(), contents);
    }

    [Fact]
    public async Task EnlargedReplacementRelocatesAndRepairsMetadata()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        using var destination = new MemoryStream();
        byte[] replacement = Enumerable.Repeat((byte)0x5A, 700).ToArray();
        NdsImageEditor editor = image.Edit().ReplaceFile("hello.bin", replacement);

        NdsSaveResult result = await editor.SaveAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using NdsImage output = NdsImage.Load(destination.ToArray());
        byte[] observed = await output.FileSystem.GetFile(0)
            .ReadAllBytesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, result.RelocatedFiles);
        Assert.Equal(0x400, output.FileSystem.GetFile(0).Data.Offset);
        Assert.Equal(0x6BCu, output.Header.UsedImageSize);
        Assert.Equal(replacement, observed);
        Assert.True(output.Validate().IsValid);
    }

    [Fact]
    public void ChangesExposeRelocationPlan()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());

        NdsFileChange change = Assert.Single(image.Edit().ReplaceFile("hello.bin", "expanded"u8).Changes);

        Assert.Equal(0, change.FileId);
        Assert.Equal("/hello.bin", change.Path);
        Assert.Equal(5, change.OriginalLength);
        Assert.Equal(8, change.ReplacementLength);
        Assert.True(change.RequiresRelocation);
    }

    [Fact]
    public async Task HeaderEditsAreValidatedAndChecksummed()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        using var destination = new MemoryStream();
        NdsImageEditor editor = image.Edit();
        editor.Header.Title = "NEW TITLE";
        editor.Header.GameCode = "ABCD";
        editor.Header.MakerCode = "ZZ";
        editor.Header.Version = 7;
        editor.Header.DebugRomOffset = 0x300;
        editor.Header.DebugRomSize = 3;
        editor.Header.DebugLoadAddress = 0x027F_1000;

        await editor.SaveAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using NdsImage output = NdsImage.Load(destination.ToArray());

        Assert.Equal("NEW TITLE", output.Header.Title);
        Assert.Equal("ABCD", output.Header.GameCode);
        Assert.Equal("ZZ", output.Header.MakerCode);
        Assert.Equal(7, output.Header.Version);
        Assert.Equal(new NdsRegion(0x300, 3), output.Header.DebugRom);
        Assert.Equal(0x027F_1000U, output.Header.DebugLoadAddress);
        Assert.True(output.Validate().IsValid);
    }

    [Fact]
    public async Task InvalidHeaderTextIsRejectedBeforeWritingMetadata()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        using var destination = new MemoryStream();
        NdsImageEditor editor = image.Edit();
        editor.Header.GameCode = "TOO-LONG";

        await Assert.ThrowsAsync<InvalidDataException>(async () => await editor.SaveAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task BannerReplacementRoundTripsAndVerifies()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateWithBanner());
        using var destination = new MemoryStream();
        NdsBanner banner = new NdsBannerBuilder()
            .SetTitle(NdsBannerLanguage.English, "Replacement")
            .Build();

        await image.Edit().ReplaceBanner(banner).SaveAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using NdsImage output = NdsImage.Load(destination.ToArray());

        Assert.Equal("Replacement", output.Banner?.Titles[NdsBannerLanguage.English]);
        Assert.True(output.Validate().IsValid);
    }

    [Fact]
    public async Task NoOpDamagedImageStaysByteIdenticalWhenVerificationIsDisabled()
    {
        byte[] source = SyntheticImage.CreateHeaderOnly();
        source[0x15E] ^= 0x40;
        using NdsImage image = NdsImage.Load(source);
        using var destination = new MemoryStream();

        await image.Edit().SaveAsync(
            destination,
            new() { VerifyOutput = false },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(source, destination.ToArray());
    }

    [Fact]
    public async Task NamedHeaderAndLogoRepairsTouchOnlyChecksumFields()
    {
        byte[] source = SyntheticImage.CreateHeaderOnly();
        source[0xC0] ^= 0x20;
        source[0x15C] ^= 0x01;
        source[0x15E] ^= 0x02;
        using NdsImage image = NdsImage.Load(source);
        NdsImageEditor editor = image.Edit().RepairNintendoLogoCrc();
        using var destination = new MemoryStream();

        await editor.SaveAsync(destination, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        byte[] repaired = destination.ToArray();
        using NdsImage output = NdsImage.Load(repaired);

        Assert.Equal(NdsRepairKind.HeaderCrc | NdsRepairKind.NintendoLogoCrc, editor.Plan.Repairs);
        Assert.True(editor.Plan.HasChanges);
        Assert.True(output.Validate().IsValid);
        Assert.Equal(source.AsSpan(0, 0x15C).ToArray(), repaired.AsSpan(0, 0x15C).ToArray());
        Assert.Equal(source.AsSpan(0x160).ToArray(), repaired.AsSpan(0x160).ToArray());
    }

    [Fact]
    public async Task BannerRepairPreservesPayloadAndReservedBytes()
    {
        byte[] source = SyntheticImage.CreateWithBanner();
        source[0x302] ^= 0x10;
        using NdsImage image = NdsImage.Load(source);
        NdsImageEditor editor = image.Edit().RepairBannerCrcs();
        using var destination = new MemoryStream();

        await editor.SaveAsync(destination, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        byte[] repaired = destination.ToArray();
        using NdsImage output = NdsImage.Load(repaired);

        Assert.Equal(NdsRepairKind.BannerCrcs, editor.Plan.Repairs);
        Assert.True(output.Validate().IsValid);
        Assert.Equal(source.AsSpan(0x304, 0x83C).ToArray(), repaired.AsSpan(0x304, 0x83C).ToArray());
    }
}
