namespace NdsForge.Tests;

public sealed class NdsDsEditAuthenticationTests
{
    [Fact]
    public async Task NoOpNeedsNoKeyAndPreservesEveryByte()
    {
        using var fixture = new LateDsBuildFixture();
        byte[] original = await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(original);
        using var destination = new MemoryStream();
        NdsSaveResult result = await image.Edit().SaveAsync(destination, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(original, destination.ToArray());
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeaderFileAndBannerEditsRegenerateAllDependentFields(bool compressedArm9)
    {
        using var fixture = new LateDsBuildFixture(compressedArm9);
        using NdsImage image = NdsImage.Load(await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        NdsImageEditor editor = image.Edit();
        editor.Header.Title = "EDITED TITLE";
        editor.ReplaceFile("/named.bin", new byte[8193]);
        editor.ReplaceBanner(new NdsBannerBuilder().SetTitle(NdsBannerLanguage.English, "Edited banner").Build());
        using var destination = new MemoryStream();
        NdsSaveResult result = await editor.SaveAsync(destination, new() { DsIntegrity = fixture.Policy }, TestContext.Current.CancellationToken);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.RelocatedFiles);
        using NdsImage output = NdsImage.Load(destination.ToArray());
        AssertValid(output, fixture);
        Assert.Equal(compressedArm9 ? "6D199DB323B44FBD17F6E465A02EAF5C8F2533FC" : "EEBF279F7B845F1E5F8F29EE7BD816A53EA6F861",
            Convert.ToHexString(output.Header.DsExtended!.ProgramsHmac.Span));
        Assert.Equal("43BAF4704D3EC8500EED9C537662DF33F9D76120", Convert.ToHexString(output.Header.DsExtended.BannerHmac.Span));
        Assert.Equal("EDITED TITLE", output.Header.Title);
        Assert.NotEqual(image.Header.DsExtended!.ProgramsHmac.ToArray(), output.Header.DsExtended!.ProgramsHmac.ToArray());
        Assert.NotEqual(image.Header.DsExtended.BannerHmac.ToArray(), output.Header.DsExtended.BannerHmac.ToArray());
        Assert.Equal(image.Header.DsExtended.Arm9OverlaysHmac.ToArray(), output.Header.DsExtended.Arm9OverlaysHmac.ToArray());
    }

    [Fact]
    public async Task MissingPolicyOrCredentialsCannotTruncateDestination()
    {
        using var fixture = new LateDsBuildFixture();
        using NdsImage image = NdsImage.Load(await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        NdsDsIntegrityOptions?[] policies = [null, NdsDsIntegrityOptions.CreateHmacSha1(fixture.ProgramKey, fixture.BannerKey),
            NdsDsIntegrityOptions.CreateHmacSha1(fixture.ProgramKey, [], fixture.SecureKey)];
        foreach (NdsDsIntegrityOptions? policy in policies)
        {
            using var destination = new MemoryStream([9, 8, 7], writable: true);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await image.Edit().ReplaceFile("/named.bin", [1])
                .SaveAsync(destination, new() { DsIntegrity = policy }, TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal([9, 8, 7], destination.ToArray());
        }
    }

    [Fact]
    public async Task PreservePolicyReportsStaleStateAndClearPolicyRetainsUnrelatedBits()
    {
        using var fixture = new LateDsBuildFixture();
        using NdsImage image = NdsImage.Load(await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        foreach (NdsDsIntegrityOptions policy in new[] { NdsDsIntegrityOptions.PreserveStored, NdsDsIntegrityOptions.Unauthenticated })
        {
            NdsImageEditor editor = image.Edit();
            editor.Header.Title = "CHANGED";
            using var destination = new MemoryStream();
            NdsSaveResult result = await editor.SaveAsync(destination, new() { DsIntegrity = policy }, TestContext.Current.CancellationToken);
            using NdsImage output = NdsImage.Load(destination.ToArray());
            Assert.Equal((byte)0x89, (byte)(output.Header.RawData.Span[0x1BF] & ~0x60));
            if (policy == NdsDsIntegrityOptions.PreserveStored)
            {
                Assert.Contains(result.Diagnostics, static item => item.Code == "NDS1540");
                Assert.Equal(image.Header.RawData.Span[0x180..0x1000], output.Header.RawData.Span[0x180..0x1000]);
                Assert.False(output.Header.DsExtended!.VerifyRsaSignature(fixture.PublicKey));
            }
            else
            {
                Assert.Empty(result.Diagnostics);
                Assert.Null(output.Header.DsExtended);
                byte[] saved = destination.ToArray();
                Assert.Equal(new byte[128], saved[0xF80..0x1000]);
                Assert.Equal(new byte[40], saved[0x378..0x3A0]);
                Assert.Equal(new byte[20], saved[0x33C..0x350]);
            }
        }
    }

    [Fact]
    public async Task InvalidSignerCannotPublishEditedPath()
    {
        using var fixture = new LateDsBuildFixture();
        using NdsImage image = NdsImage.Load(await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        string path = Path.Combine(Path.GetTempPath(), $"ndsforge-late-ds-edit-{Guid.NewGuid():N}.nds");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 9, 8, 7 }, TestContext.Current.CancellationToken);
            var policy = NdsDsIntegrityOptions.CreateHmacSha1(fixture.ProgramKey, fixture.BannerKey,
                fixture.SecureKey, new InvalidSigner(), fixture.PublicKey);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await image.Edit().ReplaceFile("/named.bin", [1])
                .SaveAsync(path, new() { OverwriteDestination = true, DsIntegrity = policy }, TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".ndsforge-*"));
        }
        finally { File.Delete(path); }
    }

    private static void AssertValid(NdsImage image, LateDsBuildFixture fixture)
    {
        NdsValidationResult validation = image.Validate(fixture.Validation());
        Assert.True(validation.IsValid, string.Join("; ", validation.Diagnostics.Select(static item => item.Message)));
        Assert.DoesNotContain(validation.Diagnostics, static item => item.Code.StartsWith("NDS15", StringComparison.Ordinal));
    }

    private sealed class InvalidSigner : INdsDsiSignatureProvider
    {
        public void SignHeader(ReadOnlySpan<byte> signedHeader, Span<byte> destination) => destination.Clear();
    }
}
