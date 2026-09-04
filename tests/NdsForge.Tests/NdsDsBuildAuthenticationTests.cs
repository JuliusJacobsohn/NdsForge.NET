using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsDsBuildAuthenticationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignedBuildMaintainsEveryAuthenticationLayerAndIsDeterministic(bool compressedArm9)
    {
        using var fixture = new LateDsBuildFixture(compressedArm9);
        using var stream = new MemoryStream();
        NdsImageBuildResult result = await fixture.Builder.WriteAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(result.Diagnostics);
        byte[] first = stream.ToArray();
        Assert.Equal(first, await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        using NdsImage image = NdsImage.Load(first);
        AssertValid(image, fixture);
        Assert.Equal(compressedArm9 ? "D20A943111E2069B1730F547EF645A779CD6F183" : "CF3C05A46BB0FD65C7BC2297E13109D1F8091910",
            Convert.ToHexString(image.Header.DsExtended!.ProgramsHmac.Span));
        Assert.Equal(compressedArm9 ? "D5D234D82F6C17508F44C86F014BD8EEEB3B79DC" : "0272E0226433E61137AB20B7A592CF831E1DDFBC",
            Convert.ToHexString(image.Header.DsExtended.Arm9OverlaysHmac.Span));
        Assert.Equal("BE7D340AA71A5387E4A8AF828A86B94277A53D3D", Convert.ToHexString(image.Header.DsExtended.BannerHmac.Span));
        Assert.Equal("DD8204E04C387DC4E5346E4C7B6A03E1D671D65E", Convert.ToHexString(image.Arm9Overlays[0].AuthenticationRecord!.HmacSha1.Span));
        Assert.Equal([0u, 1u], image.Arm9Overlays.Select(static item => item.FileId));
        Assert.Equal(2, image.FileSystem.GetFile("/named.bin").Id);
        Assert.Equal(image.Header.Arm9.Data.Offset + 0x4000, image.Header.DsExtended!.Arm9ParametersOffset);
        Assert.Equal(image.Header.Arm7.Data.Offset + 0x20, image.Header.DsExtended.Arm7ParametersOffset);
        Assert.All(NdsDsAuthentication.GetOverlayHashRegions(image), region => Assert.True(region.End <= image.Length));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecompressionRepairsClassicRecordsProgramAggregateAndRsa(bool compressedArm9)
    {
        using var fixture = new LateDsBuildFixture(compressedArm9);
        using NdsImage source = NdsImage.Load(await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken);
        Assert.Null(builder.DsMetadata!.Integrity);
        builder.DsMetadata.Integrity = fixture.Policy;
        builder.ReplaceOverlay(NdsProcessor.Arm9, 1, Enumerable.Repeat((byte)0x77, 4096).ToArray(), NdsOverlayCompressionMode.Blz);
        byte[] bytes = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage result = NdsImage.Load(bytes);
        AssertValid(result, fixture);
        Assert.Equal(compressedArm9 ? "A91F821D60DBA32551818A2E2C7691D547DEDC9D" : "A76F6E0FEF04F465BDC3320C6ED4EB8989DAF1BA",
            Convert.ToHexString(result.Header.DsExtended!.ProgramsHmac.Span));
        Assert.Equal(compressedArm9 ? "DBDDC8B7E08BBA7A764E423AEB7BE16F35C4E92B" : "BA21574F5D8168526853031BF98EC5632535F662",
            Convert.ToHexString(result.Header.DsExtended.Arm9OverlaysHmac.Span));
        Assert.Equal("22107EFE5C73EE33D4314433A82F9EB0116F2BE5", Convert.ToHexString(result.Arm9Overlays[0].AuthenticationRecord!.HmacSha1.Span));
        Assert.True(result.Arm9Overlays[0].IsCompressed);
        Assert.Equal(4096u, result.Arm9Overlays[0].RamSize);
        Assert.Equal(result.Arm9Overlays[0].Data!.Value.Length, result.Arm9Overlays[0].CompressedSize);
        Assert.NotEqual(source.Arm9Overlays[0].AuthenticationRecord!.HmacSha1.ToArray(), result.Arm9Overlays[0].AuthenticationRecord!.HmacSha1.ToArray());
        Assert.NotEqual(source.Header.DsExtended!.ProgramsHmac.ToArray(), result.Header.DsExtended!.ProgramsHmac.ToArray());
        Assert.NotEqual(source.Header.DsExtended.Arm9OverlaysHmac.ToArray(), result.Header.DsExtended.Arm9OverlaysHmac.ToArray());
        Assert.NotEqual(source.Header.DsExtended.RsaSignature.ToArray(), result.Header.DsExtended.RsaSignature.ToArray());
        Assert.Equal(source.Header.DsExtended.BannerHmac.ToArray(), result.Header.DsExtended.BannerHmac.ToArray());
        if (compressedArm9)
        {
            Assert.Equal(result.Header.Arm9.LoadAddress + result.Header.Arm9.Data.Length, result.Header.Arm9.Parameters!.CompressedEndAddress);
        }
    }

    [Fact]
    public async Task PreservePolicyRetainsOpaqueExtensionAndReportsUnverifiedAuthentication()
    {
        using var fixture = new LateDsBuildFixture();
        using NdsImage source = NdsImage.Load(await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken);
        builder.DsMetadata!.Integrity = NdsDsIntegrityOptions.PreserveStored;
        using var stream = new MemoryStream();
        NdsImageBuildResult result = await builder.WriteAsync(stream, new() { HeaderSize = 0x8000 }, TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, static item => item.Code == "NDS1540");
        using NdsImage image = NdsImage.Load(stream.ToArray());
        Assert.Equal(source.Header.RawData.Span[0x180..0x1000], image.Header.RawData.Span[0x180..0x1000]);
        Assert.Equal(0xC000u, image.Header.DsExtended!.Arm9ParametersOffset);
        Assert.Equal(image.Header.Arm7.Data.Offset + 0x20, image.Header.DsExtended.Arm7ParametersOffset);
        Assert.Equal(0xA0, image.Header.DsiFlags);
        Assert.False(image.Header.DsExtended.VerifyRsaSignature(fixture.PublicKey));
    }

    [Fact]
    public async Task ClearPolicyRemovesOnlyAuthenticationFieldsAndDeclarationBits()
    {
        using var fixture = new LateDsBuildFixture();
        fixture.Builder.DsMetadata!.Integrity = NdsDsIntegrityOptions.Unauthenticated;
        byte[] bytes = await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        byte[] expected = fixture.Builder.DsMetadata.ExportExtensionTemplate();
        expected[0x3F] = 0x89;
        expected.AsSpan(0x33C - 0x180, 20).Clear();
        expected.AsSpan(0x378 - 0x180, 40).Clear();
        expected.AsSpan(0xF80 - 0x180, 128).Clear();
        Assert.Equal(expected, bytes[0x180..0x1000]);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.True(image.Validate().IsValid);
    }

    [Fact]
    public async Task MissingPolicyAndCredentialsFailBeforeStreamMutation()
    {
        using var fixture = new LateDsBuildFixture();
        NdsDsIntegrityOptions?[] policies = [null,
            NdsDsIntegrityOptions.CreateHmacSha1(fixture.ProgramKey, fixture.BannerKey),
            NdsDsIntegrityOptions.CreateHmacSha1(fixture.ProgramKey, [], fixture.SecureKey),
            NdsDsIntegrityOptions.CreateHmacSha1([], fixture.BannerKey, fixture.SecureKey)];
        foreach (NdsDsIntegrityOptions? policy in policies)
        {
            fixture.Builder.DsMetadata!.Integrity = policy;
            using var stream = new MemoryStream([9, 8, 7], writable: true);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await fixture.Builder.WriteAsync(stream, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal([9, 8, 7], stream.ToArray());
        }
    }

    [Fact]
    public async Task UnsignedRegenerationClearsOldRsaAndReportsMissingSigningAuthority()
    {
        using var fixture = new LateDsBuildFixture();
        fixture.Builder.DsMetadata!.Integrity = NdsDsIntegrityOptions.CreateHmacSha1(fixture.ProgramKey, fixture.BannerKey, fixture.SecureKey);
        using var stream = new MemoryStream();
        NdsImageBuildResult result = await fixture.Builder.WriteAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, static item => item.Code == "NDS1542");
        Assert.Equal(new byte[128], stream.ToArray()[0xF80..0x1000]);
        using NdsImage image = NdsImage.Load(stream.ToArray());
        Assert.True(image.Validate(new NdsValidationOptions().SetDsProgramHmacKey(fixture.ProgramKey)
            .SetDsBannerHmacKey(fixture.BannerKey).SetSecureAreaKeyTable(fixture.SecureKey)).IsValid);
    }

    [Fact]
    public async Task InvalidSignerCannotReplaceAnExistingOutputPath()
    {
        using var fixture = new LateDsBuildFixture();
        fixture.Builder.DsMetadata!.Integrity = NdsDsIntegrityOptions.CreateHmacSha1(
            fixture.ProgramKey, fixture.BannerKey, fixture.SecureKey, new InvalidSigner(), fixture.PublicKey);
        string path = Path.Combine(Path.GetTempPath(), $"ndsforge-late-ds-atomic-{Guid.NewGuid():N}.nds");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 9, 8, 7 }, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await fixture.Builder.WriteAsync(path, new() { OverwriteDestination = true }, TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".ndsforge-*"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MetadataCopiesTemplatesAndRejectsUnboundedPointersAndWideFlags()
    {
        byte[] template = new byte[0xE80];
        template[0x3F] = 0x40;
        var metadata = new NdsDsBuildMetadata().SetExtensionTemplate(template);
        template[0] = 99;
        byte[] exported = metadata.ExportExtensionTemplate();
        exported[0] = 100;
        Assert.Equal(0, metadata.ExportExtensionTemplate()[0]);
        Assert.Throws<ArgumentException>(() => metadata.SetExtensionTemplate(new byte[1]));
        using var fixture = new LateDsBuildFixture();
        fixture.Builder.DsMetadata!.Arm9ParametersRelativeOffset = uint.MaxValue;
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        fixture.Builder.DsMetadata.Arm9ParametersRelativeOffset = 0x4000;
        fixture.Builder.DsMetadata.ProgramFeatures = (NdsProgramFeatures)256;
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
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
