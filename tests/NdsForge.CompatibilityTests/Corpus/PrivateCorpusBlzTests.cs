using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Nitro.Compression;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Exercises backward compression against every payload whose reviewed table metadata declares BLZ.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusBlzTests
{
    /// <summary>Requires every declared program and overlay stream to decode to its independently stored runtime size.</summary>
    [Fact]
    public async Task AllDeclaredBlzPayloadsDecodeWithinTheirRuntimeBounds()
    {
        int programs = 0;
        int overlays = 0;
        using IncrementalHash decodedOracle = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            if (image.Header.Arm9.Parameters?.IsCompressed == true)
            {
                byte[] encoded = ReadRegion(image, image.Header.Arm9.Data);
                Assert.True(BlzCodec.TryInspect(encoded, out BlzInfo info));
                Assert.True(info.UncompressedPrefixLength >= 0x4000);
                byte[] decoded = BlzCodec.Decompress(encoded);
                Assert.Equal(
                    encoded.AsSpan(0, info.UncompressedPrefixLength).ToArray(),
                    decoded.AsSpan(0, info.UncompressedPrefixLength).ToArray());
                AppendFrame(decodedOracle, decoded);
                programs++;
            }

            foreach (NdsOverlay overlay in image.Arm9Overlays.Concat(image.Arm7Overlays).Where(static item => item.IsCompressed))
            {
                NdsRegion region = Assert.IsType<NdsRegion>(overlay.Data);
                byte[] encoded = ReadRegion(image, region);
                Assert.Equal((uint)encoded.Length, overlay.CompressedSize);
                Assert.True(BlzCodec.TryInspect(encoded, out BlzInfo info));
                byte[] decoded = BlzCodec.Decompress(encoded);
                Assert.Equal(overlay.RamSize, (uint)decoded.Length);
                AppendFrame(decodedOracle, decoded);
                overlays++;
            }
        }

        Assert.Multiple(
            () => Assert.Equal(58, programs),
            () => Assert.Equal(4945, overlays),
            () => CorpusExpectations.AssertDigest(
                "46E8F5E39AB57B790028E32EECC42377E47F8510A5FDC10B8D82312825513EE9",
                Convert.ToHexString(decodedOracle.GetHashAndReset())));
    }

    /// <summary>Materializes one already bounded region so the codec receives the exact declared allocation.</summary>
    private static byte[] ReadRegion(NdsImage image, NdsRegion region)
    {
        using Stream stream = image.OpenRead(region);
        byte[] data = new byte[region.Length];
        stream.ReadExactly(data);
        return data;
    }

    /// <summary>Hashes a length-delimited output so concatenation cannot obscure component boundaries.</summary>
    private static void AppendFrame(IncrementalHash hash, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, data.Length);
        hash.AppendData(length);
        hash.AppendData(data);
    }
}
