using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsNandHeaderTests
{
    public static IEnumerable<object[]> RawBoundaries()
    {
        foreach (byte unitCode in new byte[] { 0, 2, 3 })
        {
            foreach ((ushort end, ushort start) in new (ushort, ushort)[]
            { (0, 0), (1, 1), (848, 848), (65535, 65535), (1, 2), (2, 1), (0, 1), (1, 0) })
            {
                yield return [unitCode, end, start];
            }
        }
    }

    [Theory]
    [MemberData(nameof(RawBoundaries))]
    public async Task RawWordsAndModeDependentAddressesRemainLossless(byte unitCode, ushort end, ushort start)
    {
        byte[] bytes = unitCode == 0 ? SyntheticImage.CreateWithBanner() : SyntheticImage.CreateDsiEnhanced();
        bytes[0x12] = unitCode;
        using NdsImage baseline = NdsImage.Load(bytes.ToArray());
        SetBoundaries(bytes, end, start);
        using NdsImage image = NdsImage.Load(bytes);
        long unit = unitCode == 0 ? 131072 : 524288;
        Assert.Equal(end, image.Header.NandRomEndUnits);
        Assert.Equal(start, image.Header.NandWritableStartUnits);
        Assert.Equal(unit * end, image.Header.NandRomEndOffset);
        Assert.Equal(unit * start, image.Header.NandWritableStartOffset);
        NdsImageManifest manifest = await image.CreateManifestAsync(TestContext.Current.CancellationToken);
        Assert.Equal(end, manifest.Header.NandRomEndUnits);
        Assert.Equal(start, NdsImageManifest.ParseJson(manifest.ToJson()).Header.NandWritableStartUnits);
        Assert.Equal(baseline.SizeInfo.DeclaredContentEnd, image.SizeInfo.DeclaredContentEnd);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ImportedBoundariesSurviveBothBuildProfilesWithoutForcedPadding(bool dsi, bool legacy)
    {
        NdsImageBuilder builder = CreateBuilder(dsi);
        builder.NandRomEndUnits = 848;
        builder.NandWritableStartUnits = 848;
        var options = new NdsImageBuildOptions { Profile = legacy ? NdsImageBuildProfile.Ndstool1503 : NdsImageBuildProfile.Deterministic };
        byte[] bytes = await builder.BuildAsync(options, TestContext.Current.CancellationToken);
        using NdsImage source = NdsImage.Load(bytes);
        Assert.Equal(dsi ? 444596224 : 111149056, source.Header.NandRomEndOffset);
        Assert.Equal(dsi ? 536870912 : 134217728, source.Header.DeviceCapacityBytes);
        Assert.True(source.Length < 2 * 1024 * 1024);
        Assert.True(source.Validate().IsValid);
        NdsImageBuilder imported = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal((ushort)848, imported.NandRomEndUnits);
        Assert.Equal((ushort)848, imported.NandWritableStartUnits);
        Assert.Equal(bytes, await imported.BuildAsync(options, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    public async Task IndependentAndPartiallyUnspecifiedBoundariesAreNotNormalized(ushort end, ushort start)
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.NandRomEndUnits = end;
        builder.NandWritableStartUnits = start;
        byte[] bytes = await builder.BuildAsync(new() { PadToDeviceCapacity = true }, TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.Equal(262144, bytes.Length);
        Assert.Equal(end, image.Header.NandRomEndUnits);
        Assert.Equal(start, image.Header.NandWritableStartUnits);
        Assert.Equal(end == 0 || start == 0, image.Validate().Diagnostics.Any(item => item.Code == "NDS1593"));
        Assert.True(image.Validate().IsValid);
        Assert.True(image.SizeInfo.DeclaredContentEnd < image.Header.NandWritableStartOffset || start == 0);
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("overlap")]
    [InlineData("rom-content")]
    [InlineData("writable-content")]
    [InlineData("ds-overflow")]
    [InlineData("dsi-overflow")]
    [InlineData("digital")]
    public async Task ConflictingStructuralRequestsFailBeforeMutationEvenWithoutVerification(string mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        NdsImageBuilder builder = CreateBuilder(mode is "dsi-overflow" or "digital");
        builder.NandRomEndUnits = 848;
        builder.NandWritableStartUnits = 848;
        if (mode == "overlap") { builder.NandRomEndUnits = 849; }
        if (mode == "rom-content") { builder.NandRomEndUnits = 1; builder.FileSystem.AddFile("/large", new byte[131072]); }
        if (mode == "writable-content") { builder.NandRomEndUnits = 0; builder.NandWritableStartUnits = 1; builder.FileSystem.AddFile("/large", new byte[131072]); }
        if (mode.EndsWith("overflow", StringComparison.Ordinal)) { builder.NandRomEndUnits = builder.NandWritableStartUnits = ushort.MaxValue; }
        if (mode == "digital") { builder.Carrier = NdsImageCarrier.DigitalSrl; builder.DsiMetadata!.TitleId = 0x0003000441424344; }
        using var destination = new MemoryStream();
        destination.Write([9, 8, 7]);
        await Assert.ThrowsAsync<ArgumentException>(async () => await builder.WriteAsync(destination, new()
        {
            RequestedDeviceCapacityBytes = mode == "capacity" ? 33554432 : null,
            VerifyOutput = false,
        }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], destination.ToArray());
        Assert.Equal(3, destination.Position);
    }

    [Theory]
    [InlineData(848, 848, "NDS1590")]
    [InlineData(2, 1, "NDS1592")]
    [InlineData(0, 1, "NDS1593")]
    public void AmbiguousDeclarationsProduceWarningsWithoutInventingMissingFileBytes(ushort end, ushort start, string code)
    {
        byte[] bytes = SyntheticImage.CreateWithBanner();
        SetBoundaries(bytes, end, start);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.Contains(image.Validate().Diagnostics, item => item.Code == code && item.Severity == NdsDiagnosticSeverity.Warning);
        Assert.DoesNotContain(image.SizeInfo.Diagnostics, item => item.Code == "NDS1571");
    }

    [Fact]
    public async Task StructuralBoundaryEditsAreVisibleInSemanticDifferences()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.NandRomEndUnits = builder.NandWritableStartUnits = 2;
        using NdsImage before = NdsImage.Load(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        builder.NandRomEndUnits = builder.NandWritableStartUnits = 3;
        using NdsImage after = NdsImage.Load(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        NdsImageDiff diff = await NdsImageComparer.CompareAsync(before, after, TestContext.Current.CancellationToken);
        Assert.Contains(diff.Differences, item => item.Path == "Header.NandRomEndUnits");
        Assert.Contains(diff.Differences, item => item.Path == "Header.NandWritableStartUnits");
    }

    [Theory]
    [InlineData(false, 32768)]
    [InlineData(true, 8192)]
    public async Task ExactFourGiBBoundaryIsRepresentableWithoutAllocatingCapacity(bool dsi, ushort units)
    {
        NdsImageBuilder builder = CreateBuilder(dsi);
        builder.NandRomEndUnits = builder.NandWritableStartUnits = units;
        using NdsImage image = NdsImage.Load(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0x100000000, image.Header.NandRomEndOffset);
        Assert.Equal(0x100000000, image.Header.DeviceCapacityBytes);
        Assert.True(image.Length < 2 * 1024 * 1024);
    }

    [Fact]
    public async Task BoundaryChangesParticipateInHeaderAuthentication()
    {
        using var fixture = new LateDsBuildFixture();
        using NdsImage before = NdsImage.Load(await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        fixture.Builder.NandRomEndUnits = fixture.Builder.NandWritableStartUnits = 848;
        using NdsImage after = NdsImage.Load(await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotEqual(before.Header.DsExtended!.ProgramsHmac.ToArray(), after.Header.DsExtended!.ProgramsHmac.ToArray());
        Assert.True(after.Header.DsExtended.VerifyRsaSignature(fixture.PublicKey));
        Assert.True(after.Validate(fixture.Validation()).IsValid);
    }

    [Fact]
    public async Task ReadDiagnosticsSeparateContentCrossingFromDigitalAmbiguity()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.FileSystem.AddFile("/large", new byte[131072]);
        byte[] bytes = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        SetBoundaries(bytes, 1, 1);
        using NdsImage crossing = NdsImage.Load(bytes);
        Assert.Contains(crossing.Validate().Diagnostics, item => item.Code == "NDS1591");
        builder = CreateBuilder(true);
        builder.Carrier = NdsImageCarrier.DigitalSrl;
        builder.DsiMetadata!.TitleId = 0x0003000441424344;
        bytes = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        SetBoundaries(bytes, 848, 848);
        using NdsImage digital = NdsImage.Load(bytes);
        Assert.Contains(digital.Validate().Diagnostics, item => item.Code == "NDS1594");
        Assert.DoesNotContain(digital.Validate().Diagnostics, item => item.Code == "NDS1590");
    }

    [Theory]
    [InlineData("current")]
    [InlineData("older")]
    [InlineData("wrong")]
    [InlineData("partial")]
    public async Task WorkspaceInventoriesVerifyNewFieldsAndAcceptOlderHashProtectedRecipes(string mode)
    {
        byte[] bytes = SyntheticImage.CreateWithBanner();
        SetBoundaries(bytes, 848, 848);
        using var fixture = new WorkspaceFixture();
        await fixture.ExportAsync(bytes);
        await fixture.ModifyRecipeAsync(json =>
        {
            var header = json["sourceInventory"]!["header"]!.AsObject();
            if (mode is "older" or "partial") { header.Remove("nandRomEndUnits"); }
            if (mode == "older") { header.Remove("nandWritableStartUnits"); }
            if (mode == "wrong") { header["nandRomEndUnits"] = 849; }
        });
        if (mode is "wrong" or "partial")
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageWorkspace.PackFileAsync(
                fixture.Workspace, fixture.Output, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.False(File.Exists(fixture.Output));
        }
        else
        {
            await NdsImageWorkspace.PackFileAsync(fixture.Workspace, fixture.Output, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("file")]
    [InlineData("banner")]
    [InlineData("trailer")]
    public async Task PreservationWritesCannotCrossTheNandBoundaryEvenWithVerificationDisabled(string mode)
    {
        byte[] bytes = new byte[0x20000];
        SyntheticImage.CreateWithBanner().CopyTo(bytes, 0);
        uint used = mode == "trailer" ? 0x1FFFCU - 136 : 0x1FFFCU;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80), used);
        if (mode == "trailer") { new byte[] { 0x61, 0x63, 1, 0 }.CopyTo(bytes, (int)used); }
        SetBoundaries(bytes, 1, 1);
        using NdsImage image = NdsImage.Load(bytes);
        NdsImageEditor editor = image.Edit();
        if (mode == "banner") { editor.ReplaceBanner(new NdsBannerBuilder(0x0103).Build()); }
        else { editor.ReplaceFile("/hello.bin", new byte[mode == "trailer" ? 8 : 16]); }
        using var output = new MemoryStream();
        output.Write([8, 7, 6]);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(async () => await editor.SaveAsync(output,
            new() { VerifyOutput = false, RelocatedFileAlignment = 1 }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Contains("NAND", error.Message, StringComparison.Ordinal);
        Assert.Equal([8, 7, 6], output.ToArray());
        Assert.Equal(3, output.Position);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public async Task PreservationWriteMayEndExactlyAtKnownBoundary(ushort end, ushort start)
    {
        byte[] bytes = new byte[0x20000];
        SyntheticImage.CreateWithBanner().CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80), 0x1FFF0);
        SetBoundaries(bytes, end, start);
        using NdsImage image = NdsImage.Load(bytes);
        using var output = new MemoryStream();
        await image.Edit().ReplaceFile("/hello.bin", new byte[16]).SaveAsync(output,
            new() { RelocatedFileAlignment = 1 }, TestContext.Current.CancellationToken);
        using NdsImage result = NdsImage.Load(output.ToArray());
        Assert.Equal(131072U, result.Header.UsedImageSize);
        Assert.Equal(end, result.Header.NandRomEndUnits);
        Assert.Equal(start, result.Header.NandWritableStartUnits);
    }

    internal static void SetBoundaries(byte[] bytes, ushort end, ushort start)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x94), end);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x96), start);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x15E), NdsChecksums.ComputeCrc16(bytes.AsSpan(0, 0x15E)));
    }

    private static NdsImageBuilder CreateBuilder(bool dsi = false) => new()
    {
        Kind = dsi ? NdsImageKind.NintendoDsiEnhanced : NdsImageKind.NintendoDs,
        Arm9 = new(NdsProcessor.Arm9, [1], 0x02000000, 0x02000000),
        Arm7 = new(NdsProcessor.Arm7, [2], 0x02380000, 0x02380000),
        Arm9i = dsi ? new(NdsProcessor.Arm9i, [3], 0x02400000, 0x02400000) : null,
        Arm7i = dsi ? new(NdsProcessor.Arm7i, [4], 0x02E80000, 0x02E80000) : null,
        DsiMetadata = dsi ? new() : null,
    };
}
