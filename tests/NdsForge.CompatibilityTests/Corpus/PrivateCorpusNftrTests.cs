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

        Assert.Equal(10, fontCount);
        Assert.Equal(7296, glyphCount);
        Assert.Equal(7999, mappingSlotCount);
        Assert.Equal(7296, mappedCharacterCount);
        Assert.Equal(1, depthCounts[1]);
        Assert.Equal(4, depthCounts[2]);
        Assert.Equal(5, depthCounts[3]);
        Assert.Equal(1, encodingCounts[NftrTextEncoding.Utf8]);
        Assert.Equal(6, encodingCounts[NftrTextEncoding.Utf16]);
        Assert.Equal(3, encodingCounts[NftrTextEncoding.ShiftJis]);
        Assert.Equal(34, methodCounts[NftrCharacterMapMethod.Direct]);
        Assert.Equal(31, methodCounts[NftrCharacterMapMethod.Table]);
        Assert.Equal(9, methodCounts[NftrCharacterMapMethod.Scan]);
        Assert.Equal("E39CA4C0173B79AC842E019DD5F6A74E42843489F37959B83C5E6C2B71232676", Aggregate(digests));
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
