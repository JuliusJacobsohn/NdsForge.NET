using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Exercises explicit capacity on classic and enhanced cartridge fixtures while retaining post-used content.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusCapacityTests
{
    [Theory]
    [InlineData("9A3F3001DEE8ACFFDFB705EF89B36DFAEB9D6EDCFB47CBB067F13375719BF1C9", 16777216L)]
    [InlineData("0B3C6C9F0287880249F04B032E4DA0CCDE1CE9E11CDF6BCF2FE77344A585CB5B", 33554432L)]
    [Trait("CorpusTier", "Full")]
    public async Task FullCapacityBuildRetainsProgramsTrailersAndContentDeclarations(string identity, long capacity)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CorpusExpectationIndexEntry entry = CorpusExpectations.Entries.Single(item => item.RomSha256.Equals(identity, StringComparison.OrdinalIgnoreCase));
        using NdsImage source = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry), cancellationToken: token).ConfigureAwait(true);
        NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(source, token).ConfigureAwait(true);
        if (builder.DsMetadata is not null) { builder.DsMetadata.Integrity = NdsDsIntegrityOptions.Unauthenticated; }
        if (builder.DsiMetadata is not null) { builder.DsiMetadata.Integrity = NdsDsiIntegrityOptions.Unauthenticated; }
        var options = new NdsImageBuildOptions { RequestedDeviceCapacityBytes = capacity };
        byte[] compact = await builder.BuildAsync(options, token).ConfigureAwait(true);
        byte[] padded = await builder.BuildAsync(options with { PadToDeviceCapacity = true }, token).ConfigureAwait(true);
        Assert.Equal(capacity, padded.Length);
        Assert.True(compact.AsSpan().SequenceEqual(padded.AsSpan(0, compact.Length)));
        Assert.True(padded.AsSpan(compact.Length).IndexOfAnyExcept((byte)0xFF) < 0);
        using NdsImage output = NdsImage.Load(padded);
        Assert.True(output.Validate().IsValid);
        Assert.Equal(capacity, output.Header.DeviceCapacityBytes);
        Assert.True(output.SizeInfo.DeclaredContentEnd <= compact.Length);
        Assert.Equal(source.DownloadPlaySignature?.RawData.ToArray(), output.DownloadPlaySignature?.RawData.ToArray());
        NdsProgram?[] before = [source.Header.Arm9, source.Header.Arm7, source.Header.Arm9i, source.Header.Arm7i];
        NdsProgram?[] after = [output.Header.Arm9, output.Header.Arm7, output.Header.Arm9i, output.Header.Arm7i];
        for (int index = 0; index < before.Length; index++)
        {
            if (before[index] is not { } program) { Assert.Null(after[index]); continue; }
            using Stream original = source.OpenRead(program.CompleteData);
            using Stream rebuilt = output.OpenRead(after[index]!.CompleteData);
            Assert.Equal(await SHA256.HashDataAsync(original, token).ConfigureAwait(true),
                await SHA256.HashDataAsync(rebuilt, token).ConfigureAwait(true));
        }
        if (output.Header.Dsi is { } dsi)
        {
            Assert.Equal((long)compact.Length, dsi.TotalImageSize);
            Assert.True(dsi.TotalImageSize > output.Header.UsedImageSize);
        }
    }
}
