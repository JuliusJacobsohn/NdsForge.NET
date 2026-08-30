using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Palettes;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Parses and reconstructs every direct NCLR palette allocation in the private corpus.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusNclrTests
{
    /// <summary>Locks exact colors and metadata to the reviewed compatibility baseline.</summary>
    [Fact]
    public async Task EveryNclrPreservesAndCanonicallyRebuilds()
    {
        int archiveCount = 0;
        long colorCount = 0;
        int fourBitCount = 0;
        int eightBitCount = 0;
        int extendedCount = 0;
        int mappedCount = 0;
        var archiveDigests = new List<byte[]>();
        var unsupportedFormatCounts = new Dictionary<uint, int>();
        var unsupportedDigests = new List<byte[]>();
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            foreach (NdsFileAllocation allocation in image.FileSystem.Allocations)
            {
                using Stream stream = image.OpenRead(allocation.Data);
                Span<byte> signature = new byte[4];
                if (stream.Read(signature) != signature.Length || !signature.SequenceEqual("RLCN"u8))
                {
                    continue;
                }

                stream.Position = 0;
                byte[] encoded = new byte[allocation.Data.Length];
                stream.ReadExactly(encoded);
                uint rawFormat = ReadRawFormat(encoded, "TTLP"u8, 8);
                if (rawFormat is not (3 or 4))
                {
                    InvalidDataException error = Assert.Throws<InvalidDataException>(() => NclrPalette.Parse(encoded));
                    Assert.Contains("depth", error.Message, StringComparison.Ordinal);
                    unsupportedFormatCounts[rawFormat] = unsupportedFormatCounts.GetValueOrDefault(rawFormat) + 1;
                    unsupportedDigests.Add(SHA256.HashData(encoded));
                    continue;
                }

                NclrPalette palette = NclrPalette.Parse(encoded);
                Assert.Equal(encoded, palette.CreateBuilder().Build());
                NclrPalette canonical = NclrPalette.Parse(palette.CreateBuilder().Build(preserveSourceLayout: false));
                Assert.Equal(palette.Colors, canonical.Colors);
                Assert.Equal(palette.PaletteMapping, canonical.PaletteMapping);

                archiveCount++;
                colorCount += palette.Colors.Count;
                fourBitCount += palette.Depth == NitroColorDepth.Indexed4Bpp ? 1 : 0;
                eightBitCount += palette.Depth == NitroColorDepth.Indexed8Bpp ? 1 : 0;
                extendedCount += palette.IsExtendedPalette ? 1 : 0;
                mappedCount += palette.PaletteMapping is null ? 0 : 1;
                archiveDigests.Add(HashPalette(palette));
            }
        }

        using IncrementalHash aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] digest in archiveDigests.OrderBy(static value => Convert.ToHexString(value), StringComparer.Ordinal))
        {
            aggregate.AppendData(digest);
        }

        Assert.Equal(9119, archiveCount);
        Assert.Equal(3391003, colorCount);
        Assert.Equal(6783, fourBitCount);
        Assert.Equal(2336, eightBitCount);
        Assert.Equal(447, extendedCount);
        Assert.Equal(2775, mappedCount);
        Assert.Equal(
            "EB434255BB8082DB0C23774F2A1C37936D40D05F265A95A664FB467458246134",
            Convert.ToHexString(aggregate.GetHashAndReset()));
        Assert.Equal(23, unsupportedFormatCounts[1]);
        Assert.Equal(1, unsupportedFormatCounts[6]);
        Assert.Equal(2, unsupportedFormatCounts[7]);
        Assert.Equal(3, unsupportedFormatCounts.Count);
        Assert.Equal("CD8D346B1EC259ADC7858F43A4ECEBC1E8467DD118D814ECE198EF8F9D1BEAB9", Aggregate(unsupportedDigests));
    }

    /// <summary>Frames format metadata, stored words, and optional mapping into a per-file digest.</summary>
    private static byte[] HashPalette(NclrPalette palette)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendInt32(hash, (int)palette.Depth);
        hash.AppendData([palette.IsExtendedPalette ? (byte)1 : (byte)0]);
        foreach (NitroColor555 color in palette.Colors)
        {
            AppendUInt16(hash, color.PackedValue);
        }

        if (palette.PaletteMapping is not null)
        {
            foreach (ushort value in palette.PaletteMapping)
            {
                AppendUInt16(hash, value);
            }
        }

        return hash.GetHashAndReset();
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendUInt16(IncrementalHash hash, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static uint ReadRawFormat(ReadOnlySpan<byte> data, ReadOnlySpan<byte> blockMagic, int fieldOffset)
    {
        int cursor = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        for (int index = 0; index < blockCount; index++)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 4)..]));
            if (data.Slice(cursor, 4).SequenceEqual(blockMagic))
            {
                return BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + fieldOffset)..]);
            }

            cursor += length;
        }

        throw new InvalidDataException("The palette has no PLTT block.");
    }

    private static string Aggregate(IEnumerable<byte[]> digests)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] digest in digests.OrderBy(static value => Convert.ToHexString(value), StringComparer.Ordinal))
        {
            hash.AppendData(digest);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
