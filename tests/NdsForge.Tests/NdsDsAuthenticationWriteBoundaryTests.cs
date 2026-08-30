using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsDsAuthenticationWriteBoundaryTests
{
    [Fact]
    public async Task RelocatedAggregateOnlyOverlayIncludesNewPhysicalPadding()
    {
        using var fixture = new LateDsBuildFixture();
        byte[] bytes = await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        int tableOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x50)));
        bytes[tableOffset + 31] &= 0xFD;
        bytes[tableOffset + 63] &= 0xFD;
        using NdsImage image = NdsImage.Load(bytes);
        using var destination = new MemoryStream();
        NdsSaveResult result = await image.Edit().ReplaceAllocation(0, new byte[1537]).SaveAsync(destination,
            new() { DsIntegrity = fixture.Policy, PaddingByte = 0x7E }, TestContext.Current.CancellationToken);
        using NdsImage output = NdsImage.Load(destination.ToArray());
        Assert.Equal(1, result.RelocatedFiles);
        Assert.Equal(result.UsedImageSize + 511, result.PhysicalImageSize);
        Assert.All(destination.ToArray()[checked((int)result.UsedImageSize)..], static item => Assert.Equal(0x7E, item));
        Assert.Equal(output.Header.DsExtended!.Arm9OverlaysHmac.ToArray(), NdsDsAuthentication.ComputeOverlayHmac(output, fixture.ProgramKey));
        AssertValid(output, fixture.Validation());
    }

    [Fact]
    public async Task BannerOnlyAuthenticationRequiresNeitherProgramKeyNorSecureArea()
    {
        var builder = new NdsImageBuilder
        {
            Arm9 = new(NdsProcessor.Arm9, new byte[16], 0x02000000, 0x02000000),
            Arm7 = new(NdsProcessor.Arm7, new byte[16], 0x03800000, 0x03800000),
            Banner = new NdsBannerBuilder().Build(),
            DsMetadata = new() { ProgramFeatures = NdsProgramFeatures.AuthenticatesBanner, Integrity = NdsDsIntegrityOptions.CreateHmacSha1([], [1, 2, 3]) },
        };
        using NdsImage image = NdsImage.Load(await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        AssertValid(image, new NdsValidationOptions().SetDsBannerHmacKey([1, 2, 3]));
        using var destination = new MemoryStream();
        NdsSaveResult result = await image.Edit().ReplaceBanner(new NdsBannerBuilder().SetTitle(NdsBannerLanguage.English, "Changed").Build())
            .SaveAsync(destination, new() { DsIntegrity = builder.DsMetadata.Integrity }, TestContext.Current.CancellationToken);
        Assert.Empty(result.Diagnostics);
        using NdsImage output = NdsImage.Load(destination.ToArray());
        AssertValid(output, new NdsValidationOptions().SetDsBannerHmacKey([1, 2, 3]));
        builder.Banner = null;
        await Assert.ThrowsAsync<InvalidDataException>(async () => await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task WrongSecureAreaKeyAndChangedEncryptedIdentityFailBeforeMutation()
    {
        using var fixture = new LateDsBuildFixture();
        byte[] bytes = await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        NdsSecureArea.Encrypt(bytes.AsSpan(0x4000, 0x4000), "TEST", fixture.SecureKey).CopyTo(bytes, 0x4000);
        using NdsImage image = NdsImage.Load(bytes);
        var wrongKey = new NdsKey1KeyTable(new byte[NdsKey1KeyTable.ByteLength]);
        using var destination = new MemoryStream([9, 8, 7], writable: true);
        NdsImageEditor editor = image.Edit();
        editor.Header.Title = "EDITED";
        await Assert.ThrowsAsync<NotSupportedException>(async () => await editor.SaveAsync(destination,
            new() { DsIntegrity = NdsDsIntegrityOptions.CreateHmacSha1(fixture.ProgramKey, fixture.BannerKey, wrongKey) },
            TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], destination.ToArray());
        editor.Header.GameCode = "EDIT";
        await Assert.ThrowsAsync<NotSupportedException>(async () => await editor.SaveAsync(destination,
            new() { DsIntegrity = fixture.Policy }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], destination.ToArray());
    }

    [Fact]
    public async Task DecryptedIdentityChangeRecomputesSecureCrcAndProgramAuthentication()
    {
        using var fixture = new LateDsBuildFixture();
        using NdsImage image = NdsImage.Load(await fixture.Builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
        NdsImageEditor editor = image.Edit();
        editor.Header.GameCode = "EDIT";
        using var destination = new MemoryStream();
        await editor.SaveAsync(destination, new() { DsIntegrity = fixture.Policy }, TestContext.Current.CancellationToken);
        using NdsImage output = NdsImage.Load(destination.ToArray());
        Assert.Equal("EDIT", output.Header.GameCode);
        AssertValid(output, fixture.Validation());
        Assert.NotEqual(image.Header.SecureAreaCrc, output.Header.SecureAreaCrc);
    }

    [Fact]
    public async Task PoliciesRejectIncompatibleImagesAndUnpairedSigners()
    {
        using var fixture = new LateDsBuildFixture();
        Assert.Throws<ArgumentException>(() => NdsDsIntegrityOptions.CreateHmacSha1([], []));
        Assert.Throws<ArgumentException>(() => NdsDsIntegrityOptions.CreateHmacSha1([1], [], signaturePublicKey: fixture.PublicKey));
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateHeaderOnly());
        using var destination = new MemoryStream([9, 8, 7], writable: true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await image.Edit().SaveAsync(destination,
            new() { DsIntegrity = fixture.Policy }, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], destination.ToArray());
    }

    private static void AssertValid(NdsImage image, NdsValidationOptions options)
    {
        NdsValidationResult result = image.Validate(options);
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(static item => item.Message)));
        Assert.DoesNotContain(result.Diagnostics, static item => item.Code.StartsWith("NDS15", StringComparison.Ordinal));
    }
}
