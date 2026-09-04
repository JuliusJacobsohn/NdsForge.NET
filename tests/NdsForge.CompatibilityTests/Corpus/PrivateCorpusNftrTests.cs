using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Graphics.Fonts;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Exercises every direct NFTR bitmap font in the private legal-dump corpus.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusNftrTests
{
    /// <summary>Locks independently reviewed glyph, metric, and character-map semantics.</summary>
    [Fact]
    public async Task EveryNftrPreservesRebuildsAndMatchesGoldenSemantics()
    {
        int fontCount = 0;
        long glyphCount = 0;
        long mappingSlotCount = 0;
        long mappedCharacterCount = 0;
        var depthCounts = new Dictionary<byte, long>();
        var encodingCounts = new Dictionary<NftrTextEncoding, long>();
        var methodCounts = new Dictionary<NftrCharacterMapMethod, long>();
        var digests = new List<byte[]>();
        byte[] signature = new byte[4];
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry), cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            foreach (NdsFileAllocation allocation in image.FileSystem.Allocations)
            {
                using Stream stream = image.OpenRead(allocation.Data);
                if (stream.Read(signature) != signature.Length || !signature.AsSpan().SequenceEqual("RTFN"u8)) continue;
                stream.Position = 0;
                byte[] encoded = new byte[allocation.Data.Length];
                stream.ReadExactly(encoded);

                NftrFont font = NftrFont.Parse(encoded);
                Assert.Equal(encoded, font.CreateBuilder().Build());
                NftrFont canonical = NftrFont.Parse(font.CreateBuilder().Build(preserveSourceLayout: false));
                Assert.Equal(Hash(font), Hash(canonical));

                fontCount++;
                glyphCount += font.Glyphs.Count;
                mappedCharacterCount += font.CharacterMaps.Sum(static map => map.Mappings.Count);
                mappingSlotCount += font.CharacterMaps.Sum(static map => map.Method == NftrCharacterMapMethod.Scan
                    ? map.Mappings.Count
                    : map.LastCharacter - map.FirstCharacter + 1);
                depthCounts[font.BitsPerPixel] = depthCounts.GetValueOrDefault(font.BitsPerPixel) + 1;
                encodingCounts[font.Encoding] = encodingCounts.GetValueOrDefault(font.Encoding) + 1;
                foreach (NftrCharacterMap map in font.CharacterMaps)
                    methodCounts[map.Method] = methodCounts.GetValueOrDefault(map.Method) + 1;
                digests.Add(Hash(font));
            }
        }

        Assert.Multiple(
            () => Assert.Equal(105, fontCount),
            () => Assert.Equal(63351, glyphCount),
            () => Assert.Equal(69348, mappingSlotCount),
            () => Assert.Equal(63351, mappedCharacterCount),
            () => Assert.Equal(62, depthCounts[1]),
            () => Assert.Equal(23, depthCounts[2]),
            () => Assert.Equal(12, depthCounts[3]),
            () => Assert.Equal(8, depthCounts[4]),
            () => Assert.Equal(4, depthCounts.Count),
            () => Assert.Equal(48, encodingCounts[NftrTextEncoding.Utf8]),
            () => Assert.Equal(36, encodingCounts[NftrTextEncoding.Utf16]),
            () => Assert.Equal(16, encodingCounts[NftrTextEncoding.ShiftJis]),
            () => Assert.Equal(5, encodingCounts[NftrTextEncoding.Windows1252]),
            () => Assert.Equal(4, encodingCounts.Count),
            () => Assert.Equal(210, methodCounts[NftrCharacterMapMethod.Direct]),
            () => Assert.Equal(220, methodCounts[NftrCharacterMapMethod.Table]),
            () => Assert.Equal(92, methodCounts[NftrCharacterMapMethod.Scan]),
            () => CorpusExpectations.AssertDigest("4E8B9C9FA22513A043BCAEDA619B01DDA81A9627868A32383AADC8B2D3F6C1D7", Aggregate(digests)));
    }

    private static byte[] Hash(NftrFont value)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, value.Version);
        hash.AppendData([value.FontType, value.LineHeight]);
        Append(hash, value.FallbackGlyphIndex);
        Append(hash, value.DefaultMetrics);
        hash.AppendData([(byte)value.Encoding, value.CellWidth, value.CellHeight]);
        Append(hash, value.GlyphDataLength);
        Append(hash, value.GlyphFlags);
        hash.AppendData([value.BitsPerPixel, value.RotationFlags]);
        foreach (NftrGlyph glyph in value.Glyphs)
        {
            Append(hash, glyph.Metrics);
            hash.AppendData(glyph.StoredPixels.ToArray());
        }
        foreach (NftrCharacterMap map in value.CharacterMaps)
        {
            Append(hash, map.FirstCharacter);
            Append(hash, map.LastCharacter);
            Append(hash, (uint)map.Method);
            foreach (NftrCharacterMapping mapping in map.Mappings)
            {
                Append(hash, mapping.CharacterCode);
                Append(hash, mapping.GlyphIndex);
            }
        }
        return hash.GetHashAndReset();
    }

    private static string Aggregate(IEnumerable<byte[]> digests)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] digest in digests.OrderBy(static item => Convert.ToHexString(item), StringComparer.Ordinal))
            hash.AppendData(digest);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, NftrGlyphMetrics value) =>
        hash.AppendData([(byte)value.BearingX, value.GlyphWidth, value.AdvanceWidth]);

    private static void Append(IncrementalHash hash, ushort value)
    {
        Span<byte> data = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(data, value);
        hash.AppendData(data);
    }

    private static void Append(IncrementalHash hash, uint value)
    {
        Span<byte> data = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        hash.AppendData(data);
    }
}
