using System.Security.Cryptography;
using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Images;

namespace NdsForge.CompatibilityTests.Corpus;

[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusLogoTests
{
    [Fact]
    [Trait("CorpusTier", "Full")]
    public Task CartridgeLogoFieldsMatchAllDecodedPixels() => VerifyAsync(
        CorpusExpectations.Entries.Select(static entry => (entry.RomSha256, CorpusExpectations.Resolve(entry))), 142,
        "87797B9832918E64C971126275AD5DBB0EA73427C0CA5AD15DFCA8BC1D5A225C");

    [Fact]
    [Trait("CorpusTier", "Full")]
    public Task DigitalLogoFieldsMatchAllDecodedPixels() => VerifyAsync(
        PrivateDigitalCarrierTests.FindFixtures().Select(static entry => (entry.Key, entry.Value)), 5,
        "8FF93158437E4A56A10B2EC216B080EB8A91762D31D939164B199358CA411A97");

    private static async Task VerifyAsync(IEnumerable<(string Identity, string Path)> sources, int expectedCount, string expectedDigest)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        foreach ((string identity, string path) in sources.OrderBy(static entry => entry.Identity, StringComparer.OrdinalIgnoreCase))
        {
            using NdsImage image = await NdsImage.OpenAsync(path, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            byte[] encoded = image.Header.RawData.Slice(0xC0, 156).ToArray();
            Assert.Equal("08A0153CFD6B0EA54B938F7D209933FA849DA0D56F5A34C481060C9FF2FAD818", Convert.ToHexString(SHA256.HashData(encoded)));
            CartridgeLogo logo = CartridgeLogo.Parse(encoded);
            Assert.Equal(encoded, logo.WritePreserved());
            byte[] pixelHash = SHA256.HashData(logo.Pixels.Span);
            Assert.Equal("05E18EC1DE56082C9026BBADD475458BCF8292994B89638A8C9E88D1E3A17DE6", Convert.ToHexString(pixelHash));
            Assert.Equal(682, logo.Pixels.ToArray().Count(static value => value == 1));
            byte[] canonical = logo.WriteCanonical();
            Assert.Equal("14604D651228CAAD47438CE6D3BAC078B9E65E257F6E172BBA8B7DBE01425852", Convert.ToHexString(SHA256.HashData(canonical)));
            Assert.NotEqual(encoded, canonical);
            Assert.Equal(logo.Pixels.ToArray(), CartridgeLogo.Parse(canonical).Pixels.ToArray());
            Assert.Equal(canonical, CartridgeLogo.FromPixels(logo.Pixels.Span).WritePreserved());
            RgbaColor32 foreground = new(220, 40, 80);
            RgbaColor32 background = new(0, 0, 0, 0);
            RgbaImage32 raster = logo.Render(foreground, background);
            Assert.Equal(canonical, CartridgeLogo.FromRgba32(raster.Pixels.ToArray(), foreground, background).WriteCanonical());
            digest.AppendData(Convert.FromHexString(identity));
            digest.AppendData(pixelHash);
            count++;
        }
        Assert.Equal(expectedCount, count);
        CorpusExpectations.AssertDigest(expectedDigest, Convert.ToHexString(digest.GetHashAndReset()));
    }
}
