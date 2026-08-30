using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Freezes aggregate late-DS authentication coverage with synthetic caller credentials and neutral digests.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusDsAuthenticationTests
{
    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task EveryLateDsOverlayAggregateMatchesItsPhysicalCoverage()
    {
        byte[] key = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
        int imageCount = 0;
        int storedDigestCount = 0;
        int emptyOverlayCount = 0;
        int absentDigestWithOverlaysCount = 0;
        using IncrementalHash canonical = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries
            .OrderBy(static item => item.RomSha256, StringComparer.Ordinal))
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            if (image.Header.DsExtended is not { HasProgramAuthentication: true } extension)
            {
                continue;
            }

            imageCount++;
            bool hasStoredDigest = extension.Arm9OverlaysHmac.Span.ContainsAnyExcept((byte)0);
            storedDigestCount += hasStoredDigest ? 1 : 0;
            if (image.Arm9Overlays.Count == 0)
            {
                emptyOverlayCount++;
                Assert.False(hasStoredDigest);
                Assert.Empty(NdsDsAuthentication.GetOverlayHashRegions(image));
            }
            else if (!hasStoredDigest)
            {
                absentDigestWithOverlaysCount++;
            }

            canonical.AppendData(Convert.FromHexString(entry.RomSha256));
            canonical.AppendData(NdsDsAuthentication.ComputeOverlayHmac(image, key));
        }

        Assert.Equal(67, imageCount);
        Assert.Equal(42, storedDigestCount);
        Assert.Equal(8, emptyOverlayCount);
        Assert.Equal(17, absentDigestWithOverlaysCount);
        CorpusExpectations.AssertDigest(
            "3F09FBD8DEE5D0F5E9EFCEB9AAB0C3870C2C98A1422A595713AB2118452B283B",
            Convert.ToHexString(canonical.GetHashAndReset()));
    }
}
