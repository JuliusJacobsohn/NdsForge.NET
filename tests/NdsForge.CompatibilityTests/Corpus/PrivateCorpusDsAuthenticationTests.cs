using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Freezes aggregate late-DS authentication coverage with synthetic caller credentials and neutral digests.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusDsAuthenticationTests
{
    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task ExplicitStoredProgramInputsAndCompleteBannersMatchCorpusDigests()
    {
        byte[] programKey = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
        byte[] bannerKey = Enumerable.Range(0, 64).Select(static value => (byte)(255 - value)).ToArray();
        int programCount = 0;
        int bannerCount = 0;
        int storedProgramDigestCount = 0;
        int storedBannerDigestCount = 0;
        int missingCredentialFindings = 0;
        using IncrementalHash programs = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using IncrementalHash banners = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries
            .OrderBy(static item => item.RomSha256, StringComparer.Ordinal))
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            if (image.Header.DsExtended is not NdsDsExtendedHeader extension)
            {
                continue;
            }

            NdsDiagnostic[] authenticationFindings = image.Validate(new() { ValidateDsAuthentication = true }).Diagnostics
                .Where(static item => item.Code.StartsWith("NDS15", StringComparison.Ordinal)).ToArray();
            Assert.All(authenticationFindings, static item =>
            {
                Assert.Equal(NdsDiagnosticSeverity.Warning, item.Severity);
                Assert.True(item.Code is "NDS1501" or "NDS1511" or "NDS1530", item.Code);
            });
            missingCredentialFindings += authenticationFindings.Length;

            if (extension.HasProgramAuthentication)
            {
                programCount++;
                storedProgramDigestCount += extension.ProgramsHmac.Span.ContainsAnyExcept((byte)0) ? 1 : 0;
                byte[] arm9 = await ReadAsync(image, image.Header.Arm9.Data).ConfigureAwait(true);
                byte[] arm7 = await ReadAsync(image, image.Header.Arm7.Data).ConfigureAwait(true);
                programs.AppendData(Convert.FromHexString(entry.RomSha256));
                programs.AppendData(NdsDsAuthentication.ComputeProgramsHmac(
                    image.Header.RawData.Span[..0x160], arm9, arm7, programKey));
            }

            if (extension.HasBannerAuthentication)
            {
                bannerCount++;
                storedBannerDigestCount += extension.BannerHmac.Span.ContainsAnyExcept((byte)0) ? 1 : 0;
                Assert.NotNull(image.Banner);
                banners.AppendData(Convert.FromHexString(entry.RomSha256));
                banners.AppendData(NdsDsAuthentication.ComputeBannerHmac(image.Banner, bannerKey));
            }
        }

        Assert.Equal(67, programCount);
        Assert.Equal(50, storedProgramDigestCount);
        Assert.Equal(45, bannerCount);
        Assert.Equal(33, storedBannerDigestCount);
        Assert.Equal(179, missingCredentialFindings);
        CorpusExpectations.AssertDigest(
            "3A567B2922082C822059C3E6355B8FD739772DA375AF33D7D53B8C84AC904C8B",
            Convert.ToHexString(programs.GetHashAndReset()));
        CorpusExpectations.AssertDigest(
            "8FB4CF202819BD4ACB33535F10517CC9283E60AFC3C9BE86E57F074562AAE58B",
            Convert.ToHexString(banners.GetHashAndReset()));
    }

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

    private static async Task<byte[]> ReadAsync(NdsImage image, NdsRegion region)
    {
        byte[] data = new byte[checked((int)region.Length)];
        using Stream stream = image.OpenRead(region);
        await stream.ReadExactlyAsync(data, TestContext.Current.CancellationToken).ConfigureAwait(true);
        return data;
    }
}
