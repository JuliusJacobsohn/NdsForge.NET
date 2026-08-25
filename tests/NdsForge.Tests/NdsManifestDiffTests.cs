namespace NdsForge.Tests;

public sealed class NdsManifestDiffTests
{
    [Fact]
    public async Task DsiManifestCarriesLosslessHeaderSemanticsAndDiffsThem()
    {
        byte[] leftBytes = SyntheticImage.CreateDsiEnhanced();
        byte[] rightBytes = leftBytes.ToArray();
        rightBytes[0x180] ^= 0x40;
        using NdsImage left = NdsImage.Load(leftBytes);
        using NdsImage right = NdsImage.Load(rightBytes);

        NdsImageManifest manifest = await left.CreateManifestAsync(TestContext.Current.CancellationToken);
        NdsManifestDsi dsi = Assert.IsType<NdsManifestDsi>(manifest.Dsi);
        NdsImageManifest parsed = NdsImageManifest.ParseJson(manifest.ToJson());
        NdsImageDiff diff = await NdsImageComparer.CompareAsync(
            left,
            right,
            TestContext.Current.CancellationToken);

        Assert.Equal(0xA3, manifest.Header.DsiFlags);
        Assert.Equal(0U, manifest.Header.DebugRomOffset);
        Assert.Equal(0x99AABBCCU, dsi.ScfgExtMask);
        Assert.Equal("010203040506", dsi.SharedDataFileSizesHex);
        Assert.Equal(Convert.ToHexStringLower(leftBytes.AsSpan(0x180, 0x30)), dsi.MemoryBankSettingsHex);
        Assert.Equal(Convert.ToHexStringLower(leftBytes.AsSpan(0x2F0, 0x10)), dsi.AgeRatingsHex);
        Assert.True(NdsImageComparer.Compare(manifest, parsed).AreEquivalent);
        Assert.Contains(diff.Differences, static value => value.Path == "Dsi.MemoryBankSettingsHex");
        Assert.Throws<InvalidDataException>(() => NdsImageManifest.ParseJson(
            manifest.ToJson(indented: false).Replace(
                dsi.MemoryBankSettingsHex,
                "zz" + dsi.MemoryBankSettingsHex[2..],
                StringComparison.Ordinal)));
    }

    [Fact]
    public async Task CaptureProducesStableHashesAndRoundTripsStrictJson()
    {
        byte[] bytes = SyntheticImage.CreateWithBanner();
        using NdsImage image = NdsImage.Load(bytes);

        NdsImageManifest manifest = await NdsImageManifest.CaptureAsync(
            image,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        string json = manifest.ToJson();
        NdsImageManifest parsed = NdsImageManifest.ParseJson(json);

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("TEST", manifest.Header.GameCode);
        Assert.Equal(64, manifest.ImageSha256.Length);
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            manifest.ImageSha256);
        Assert.Equal("/hello.bin", Assert.Single(manifest.Files).Path);
        Assert.Equal("/", Assert.Single(manifest.Directories));
        Assert.Equal(64, Assert.Single(manifest.Allocations).Sha256.Length);
        Assert.NotNull(manifest.Banner);
        Assert.Contains("\"kind\": \"NintendoDs\"", json, StringComparison.Ordinal);
        Assert.True(NdsImageComparer.Compare(manifest, parsed).AreEquivalent);
    }

    [Fact]
    public async Task PreCanceledCaptureDoesNotReturnAPartialManifest()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await image.CreateManifestAsync(cancellation.Token).ConfigureAwait(true));
    }

    [Fact]
    public async Task JsonStreamOperationsLeaveCallerOwnedStreamsOpen()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        NdsImageManifest manifest = await NdsImageManifest.CaptureAsync(
            image,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        using var stream = new MemoryStream();

        await manifest.WriteJsonAsync(
            stream,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        stream.Position = 0;
        NdsImageManifest parsed = await NdsImageManifest.ReadJsonAsync(
            stream,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(stream.CanRead);
        Assert.True(stream.CanWrite);
        Assert.Equal(manifest.ImageSha256, parsed.ImageSha256);
    }

    [Fact]
    public async Task SemanticDiffSeparatesContentIdentityAndHeaderChanges()
    {
        byte[] leftBytes = await BuildAsync("LEFT", "/data.bin", [1, 2, 3]).ConfigureAwait(true);
        byte[] rightBytes = await BuildAsync("RIGHT", "/data.bin", [1, 9, 3]).ConfigureAwait(true);
        using NdsImage left = NdsImage.Load(leftBytes);
        using NdsImage right = NdsImage.Load(rightBytes);

        NdsImageDiff diff = await NdsImageComparer.CompareAsync(
            left,
            right,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(diff.AreEquivalent);
        Assert.Contains(diff.Differences, static value =>
            value.Path == "Header.Title" && value.Kind == NdsDifferenceKind.Modified &&
            value.Before == "LEFT" && value.After == "RIGHT");
        Assert.Contains(diff.Differences, static value =>
            value.Path == "Files[/data.bin].Sha256" && value.Kind == NdsDifferenceKind.Modified);
        Assert.Contains(diff.Differences, static value =>
            value.Path == "Allocations[0].Sha256" && value.Kind == NdsDifferenceKind.Modified);
    }

    [Fact]
    public async Task SemanticDiffRecognizesUniqueContentPreservingFileMove()
    {
        byte[] leftBytes = await BuildAsync("SAME", "/old.bin", [4, 5, 6]).ConfigureAwait(true);
        byte[] rightBytes = await BuildAsync("SAME", "/folder/new.bin", [4, 5, 6]).ConfigureAwait(true);
        using NdsImage left = NdsImage.Load(leftBytes);
        using NdsImage right = NdsImage.Load(rightBytes);

        NdsImageDiff diff = await NdsImageComparer.CompareAsync(
            left,
            right,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Contains(diff.Differences, static value =>
            value.Path == "Files.Path" && value.Kind == NdsDifferenceKind.Moved &&
            value.Before == "/old.bin" && value.After == "/folder/new.bin");
        Assert.Contains(diff.Differences, static value =>
            value.Path == "Directories[/folder]" && value.Kind == NdsDifferenceKind.Added);
        Assert.DoesNotContain(diff.Differences, static value =>
            value.Path is "Files[/old.bin]" or "Files[/folder/new.bin]" &&
            value.Kind is NdsDifferenceKind.Added or NdsDifferenceKind.Removed);
    }

    [Fact]
    public async Task StrictManifestParserRejectsSchemaHashAndUnknownMemberTampering()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        NdsImageManifest manifest = await NdsImageManifest.CaptureAsync(
            image,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        string json = manifest.ToJson(indented: false);

        Assert.Throws<InvalidDataException>(() =>
            NdsImageManifest.ParseJson(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() =>
            NdsImageManifest.ParseJson(json.Replace(manifest.ImageSha256, "not-a-hash", StringComparison.Ordinal)));
        Assert.Throws<System.Text.Json.JsonException>(() =>
            NdsImageManifest.ParseJson(json[..^1] + ",\"unexpected\":true}"));
    }

    private static async ValueTask<byte[]> BuildAsync(string title, string filePath, byte[] contents)
    {
        var builder = new NdsImageBuilder
        {
            Title = title,
            GameCode = "MF01",
            MakerCode = "HB",
            Arm9 = new(NdsProcessor.Arm9, [1, 2, 3, 4], 0x0200_0000, 0x0200_0000),
            Arm7 = new(NdsProcessor.Arm7, [5, 6], 0x0238_0000, 0x0238_0000),
        };
        builder.FileSystem.AddFile(filePath, contents);
        return await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
    }
}
