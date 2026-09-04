using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Images;

namespace NdsForge.CompatibilityTests.Corpus;

[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusBannerConversionTests
{
    [Fact]
    [Trait("CorpusTier", "Full")]
    public Task AllStaticIconsAndAnimatedPaletteCombinationsRetainVisibleColors() => VerifyGroupAsync(
        CorpusExpectations.Entries.Select(static entry => (entry.RomSha256, CorpusExpectations.Resolve(entry))), 142, 9, 218,
        "0EF40B89FF375A221B9C1F872D6C1239074024A1C75065EEAFD414F73FFBDA47",
        "83C20F86D42A032CBB7C1EA067335543C6036235BC6664A292F1F5179E263B95");

    [Fact]
    [Trait("CorpusTier", "Full")]
    public Task DigitalIconsAndAnimatedPaletteCombinationsRetainVisibleColors() => VerifyGroupAsync(
        PrivateDigitalCarrierTests.FindFixtures().Select(static entry => (entry.Key, entry.Value)), 5, 1, 8,
        "73094B8B3F2F88EA9F928ED82E36582B091E077F8C8CC8C6839713880E2DA12C",
        "A0323B5CE6EAAFB1C4785CAA0B406CCB0CBFA8E35F4ACD77351AD0949B353593");

    private static async Task VerifyGroupAsync(IEnumerable<(string Identity, string Path)> sources,
        int expectedImages, int expectedAnimated, int expectedFrames, string staticDigest, string animationDigest)
    {
        using var statics = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var animations = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int animatedCount = 0;
        int frameCount = 0;
        int imageCount = 0;
        foreach ((string identity, string path) in sources.OrderBy(static item => item.Identity, StringComparer.OrdinalIgnoreCase))
        {
            using NdsImage image = await NdsImage.OpenAsync(path, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            NdsBanner banner = Assert.IsType<NdsBanner>(image.Banner);
            statics.AppendData(Convert.FromHexString(identity));
            statics.AppendData(VerifyConversion(banner.RenderIconRgba32()));
            imageCount++;
            if (!banner.IsAnimated) { continue; }
            animations.AppendData(Convert.FromHexString(identity));
            for (int tile = 0; tile < 8; tile++)
            {
                for (int palette = 0; palette < 8; palette++) { animations.AppendData(VerifyConversion(banner.RenderAnimatedIconRgba32(tile, palette))); }
            }
            IReadOnlyList<NdsBannerAnimationStep> steps = banner.GetAnimationSteps();
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(steps.Count);
            foreach (NdsBannerAnimationStep step in steps)
            {
                writer.Write((int)step.Duration);
                writer.Write((int)step.TileFrame);
                writer.Write((int)step.PaletteFrame);
                writer.Write(step.FlipHorizontal);
                writer.Write(step.FlipVertical);
            }
            writer.Flush();
            animations.AppendData(ms.ToArray());
            animatedCount++;
            frameCount += steps.Count;
        }
        Assert.Equal(expectedImages, imageCount);
        Assert.Equal(expectedAnimated, animatedCount);
        Assert.Equal(expectedFrames, frameCount);
        CorpusExpectations.AssertDigest(staticDigest, Convert.ToHexString(statics.GetHashAndReset()));
        CorpusExpectations.AssertDigest(animationDigest, Convert.ToHexString(animations.GetHashAndReset()));
    }

    private static byte[] VerifyConversion(byte[] rgba)
    {
        RgbaColor32[] pixels = new RgbaColor32[1024];
        for (int i = 0; i < pixels.Length; i++) { pixels[i] = new(rgba[i * 4], rgba[(i * 4) + 1], rgba[(i * 4) + 2], rgba[(i * 4) + 3]); }
        IndexedImage4 converted = IndexedImage4.FromRgba32(32, 32, pixels, new() { PaletteOverflow = IndexedPaletteOverflow.Reject });
        Assert.False(converted.WasColorReduced);
        byte[] rendered = new NdsBannerBuilder().SetIndexedIcon(converted.PaletteIndices.Span, converted.Palette.Span).Build().RenderIconRgba32();
        byte[] expected = Visible(rgba);
        Assert.Equal(expected, Visible(rendered));
        return expected;
    }

    private static byte[] Visible(byte[] rgba)
    {
        byte[] result = new byte[3072];
        for (int i = 0; i < 1024; i++)
        {
            if (rgba[(i * 4) + 3] == 0) { continue; }
            result[i * 3] = 255;
            ushort color = (ushort)((rgba[i * 4] >> 3) | ((rgba[(i * 4) + 1] >> 3) << 5) | ((rgba[(i * 4) + 2] >> 3) << 10));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan((i * 3) + 1), color);
        }
        return result;
    }
}
