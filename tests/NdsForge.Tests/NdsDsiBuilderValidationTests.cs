using System.Buffers.Binary;
using System.Security.Cryptography;

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

    [Fact]
    public async Task CallerSigningAuthorityProducesVerifiableDsiHeader()
    {
#pragma warning disable CA5351 // DSi header authenticity is fixed to RSA-1024 by the platform format.
        using RSA rsa = RSA.Create(1024);
#pragma warning restore CA5351
        using var signer = new NdsDsiRsaSignatureProvider(rsa);
        NdsDsiRsaPublicKey publicKey = NdsDsiRsaPublicKey.FromRsa(rsa);
        byte[] hmacKey = [1, 3, 3, 7];
        NdsImageBuilder builder = CreateDsiBuilder();
        builder.DsiMetadata!.Integrity = NdsDsiIntegrityOptions.CreateSignedHmacSha1(hmacKey, signer);

        byte[] data = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(data);
        var validationOptions = new NdsValidationOptions()
            .SetDsiHmacKey(hmacKey)
            .SetDsiRsaPublicKey(publicKey);

        Assert.True(image.Header.Dsi!.VerifyRsaSignature(publicKey));
        Assert.True(image.Validate(validationOptions).IsValid);
        data[0x240] ^= 1;
        using NdsImage tampered = NdsImage.Load(data);
        Assert.Contains(tampered.Validate(validationOptions).Diagnostics, static value => value.Code == "NDS1321");
    }

    [Fact]
    public void RsaModeCannotBeSelectedWithoutSigningAuthority()
    {
        Assert.Throws<ArgumentException>(() =>
            NdsDsiIntegrityOptions.CreateHmacSha1([1], NdsDsiSignatureMode.RsaSha1));
    }

    [Fact]
    public async Task StructuralImportRelocatesProgramAnchoredModcryptArea()
    {
        byte[] sourceBytes = await CreateDsiBuilder().BuildAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using (NdsImage initial = NdsImage.Load(sourceBytes))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                sourceBytes.AsSpan(0x220),
                checked((uint)initial.Header.Arm9i!.Data.Offset));
            BinaryPrimitives.WriteUInt32LittleEndian(sourceBytes.AsSpan(0x224), 1);
        }

        using NdsImage source = NdsImage.Load(sourceBytes);
        long originalOffset = source.Header.Arm9i!.Data.Offset;
        NdsImageBuilder imported = await NdsImageBuilder.FromImageAsync(
            source,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        imported.FileSystem.AddFile("/shift.bin", new byte[0x1000]);

        byte[] rebuiltBytes = await imported.BuildAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using NdsImage rebuilt = NdsImage.Load(rebuiltBytes);
        Assert.NotEqual(originalOffset, rebuilt.Header.Arm9i!.Data.Offset);
        Assert.Equal(rebuilt.Header.Arm9i.Data.Offset, rebuilt.Header.Dsi!.ModcryptArea1.Offset);
        Assert.Equal(1, rebuilt.Header.Dsi.ModcryptArea1.Length);
        Assert.True(rebuilt.Validate().IsValid);

        imported.DsiMetadata!.ModcryptArea1 = default;
        byte[] overriddenBytes = await imported.BuildAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using NdsImage overridden = NdsImage.Load(overriddenBytes);
        Assert.True(overridden.Header.Dsi!.ModcryptArea1.IsEmpty);
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
