using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Verifies safe source-preserving trim boundaries and re-expansion using complete private cartridge fixtures.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusResizeTests
{
    [Theory]
    [InlineData("9A3F3001DEE8ACFFDFB705EF89B36DFAEB9D6EDCFB47CBB067F13375719BF1C9", 10590376L, "775850559B85CEA024D0BD58259FDCE7BC95DB4F1520F8F0EFBF07BBB712775D", true)]
    [InlineData("0B3C6C9F0287880249F04B032E4DA0CCDE1CE9E11CDF6BCF2FE77344A585CB5B", 32838656L, "3F26F89123D20662F7D63CE3862F207C0629960CE5ED4639BEAD662DCD0E9DC1", false)]
    [Trait("CorpusTier", "Full")]
    public async Task TrimRetainsAllPostUsedContentAndReExpansionRestoresExactSource(string identity, long expectedTrimmedLength, string paddingDigest, bool validSource)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CorpusExpectationIndexEntry entry = CorpusExpectations.Entries.Single(item => item.RomSha256.Equals(identity, StringComparison.OrdinalIgnoreCase));
        using NdsImage source = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry), cancellationToken: token).ConfigureAwait(true);
        using var trimmedStream = new MemoryStream();
        Assert.Equal(validSource, source.Validate().IsValid);
        if (!validSource)
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await NdsImageResizer.WriteAsync(source, trimmedStream,
                new() { Mode = NdsImageResizeMode.Trim }, token).ConfigureAwait(true));
            Assert.Empty(trimmedStream.ToArray());
        }
        NdsImageResizeResult trimmed = await NdsImageResizer.WriteAsync(source, trimmedStream,
            new() { Mode = NdsImageResizeMode.Trim, VerifyOutput = validSource }, token).ConfigureAwait(true);
        Assert.Equal(expectedTrimmedLength, trimmed.OutputLength);
        Assert.Equal(new NdsRegion(expectedTrimmedLength, source.Length - expectedTrimmedLength), trimmed.RemovedData);
        Assert.Empty(trimmed.Diagnostics);
        using NdsImage compact = NdsImage.Load(trimmedStream.ToArray());
        Assert.Equal(source.Header.RawData.ToArray(), compact.Header.RawData.ToArray());
        Assert.Equal(source.DownloadPlaySignature?.RawData.ToArray(), compact.DownloadPlaySignature?.RawData.ToArray());
        Assert.Equal(source.Validate().Diagnostics.Select(static item => item.Code), compact.Validate().Diagnostics.Select(static item => item.Code));
        using var expandedStream = new MemoryStream();
        NdsImageResizeResult expanded = await NdsImageResizer.WriteAsync(compact, expandedStream,
            new() { Mode = NdsImageResizeMode.PadToDeviceCapacity, VerifyOutput = validSource }, token).ConfigureAwait(true);
        Assert.Equal(source.Length, expanded.OutputLength);
        Assert.Equal(paddingDigest, Convert.ToHexString(SHA256.HashData(expandedStream.GetBuffer().AsSpan(
            checked((int)expectedTrimmedLength), checked((int)expanded.AddedData!.Value.Length)))));
        expandedStream.Position = 0;
        Assert.Equal(identity, Convert.ToHexString(await SHA256.HashDataAsync(expandedStream, token).ConfigureAwait(true)));
    }
}
