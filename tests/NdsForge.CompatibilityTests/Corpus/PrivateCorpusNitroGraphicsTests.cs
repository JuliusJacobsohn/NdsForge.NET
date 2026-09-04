using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Graphics.Maps;
using NdsForge.Graphics.Tiles;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Exercises every direct NCGR and NSCR allocation in the private legal-dump corpus.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusNitroGraphicsTests
{
    /// <summary>Locks exact indexed pixels and map entries to the reviewed compatibility baseline.</summary>
    [Fact]
    public async Task EveryNcgrAndNscrPreservesAndCanonicallyRebuilds()
    {
        int ncgrCount = 0;
        long pixelCount = 0;
        int rejectedNcgrMagic = 0;
        var unsupportedNcgrFormatCounts = new Dictionary<uint, int>();
        var unsupportedNcgrDigests = new List<byte[]>();
        int nscrCount = 0;
        long entryCount = 0;
        var ncgrDigests = new List<byte[]>();
        var nscrDigests = new List<byte[]>();
        byte[] signature = new byte[4];
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            foreach (NdsFileAllocation allocation in image.FileSystem.Allocations)
            {
                using Stream stream = image.OpenRead(allocation.Data);
                if (stream.Read(signature) != signature.Length ||
                    (!signature.SequenceEqual("RGCN"u8) && !signature.SequenceEqual("RCSN"u8)))
                {
                    continue;
                }

                stream.Position = 0;
                byte[] encoded = new byte[allocation.Data.Length];
                stream.ReadExactly(encoded);
                if (signature.SequenceEqual("RGCN"u8))
                {
                    if (BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(4)) != 0xFEFF)
                    {
                        Assert.Throws<InvalidDataException>(() => NcgrCharacterGraphics.Parse(encoded));
                        rejectedNcgrMagic++;
                        continue;
                    }

                    uint rawFormat = ReadRawFormat(encoded);
                    if (rawFormat is not (3 or 4))
                    {
                        InvalidDataException error = Assert.Throws<InvalidDataException>(
                            () => NcgrCharacterGraphics.Parse(encoded));
                        Assert.Contains("depth", error.Message, StringComparison.Ordinal);
                        unsupportedNcgrFormatCounts[rawFormat] = unsupportedNcgrFormatCounts.GetValueOrDefault(rawFormat) + 1;
                        unsupportedNcgrDigests.Add(SHA256.HashData(encoded));
                        continue;
                    }

                    NcgrCharacterGraphics graphics = NcgrCharacterGraphics.Parse(encoded);
                    Assert.Equal(encoded, graphics.CreateBuilder().Build());
                    NcgrCharacterGraphics canonical = NcgrCharacterGraphics.Parse(
                        graphics.CreateBuilder().Build(preserveSourceLayout: false));
                    Assert.Equal(graphics.Width, canonical.Width);
                    Assert.Equal(graphics.Height, canonical.Height);
                    Assert.Equal(graphics.Depth, canonical.Depth);
                    Assert.Equal(graphics.Mapping, canonical.Mapping);
                    Assert.Equal(graphics.StorageFlags, canonical.StorageFlags);
                    Assert.Equal(graphics.Pixels, canonical.Pixels);
                    Assert.Equal(graphics.SourceRegion, canonical.SourceRegion);
                    ncgrCount++;
                    pixelCount += graphics.Pixels.Count;
                    ncgrDigests.Add(HashNcgr(graphics));
                }
                else
                {
                    NscrScreenMap map = NscrScreenMap.Parse(encoded);
                    Assert.Equal(encoded, map.CreateBuilder().Build());
                    NscrScreenMap canonical = NscrScreenMap.Parse(
                        map.CreateBuilder().Build(preserveSourceLayout: false));
                    Assert.Equal(map.Width, canonical.Width);
                    Assert.Equal(map.Height, canonical.Height);
                    Assert.Equal(map.PaletteSelection, canonical.PaletteSelection);
                    Assert.Equal(map.BackgroundKind, canonical.BackgroundKind);
                    Assert.Equal(map.Entries, canonical.Entries);
                    nscrCount++;
                    entryCount += map.Entries.Count;
                    nscrDigests.Add(HashNscr(map));
                }
            }
        }

        Assert.Equal(10797, ncgrCount);
        Assert.Equal(215348672, pixelCount);
        Assert.Equal(13, rejectedNcgrMagic);
        Assert.Equal(4526, nscrCount);
        Assert.Equal(5368694, entryCount);
        Assert.Equal("1F508B45E4CD120EDBA8D4A01DF795B25C6A0382F35DE872245F800DCBEA8BC1", Aggregate(ncgrDigests));
        Assert.Equal("F9234C67D4A2BAC80FCC2CDD2499B382B575D2602D3FEA94B2F7C198BAE81AA8", Aggregate(nscrDigests));
        Assert.Equal(23, unsupportedNcgrFormatCounts[1]);
        Assert.Equal(1, unsupportedNcgrFormatCounts[6]);
        Assert.Equal(2, unsupportedNcgrFormatCounts[7]);
        Assert.Equal(3, unsupportedNcgrFormatCounts.Count);
        Assert.Equal("231B0C53F3820D74B904D2C7779559B845A2BB41A6D797A75189E6362E87037D", Aggregate(unsupportedNcgrDigests));
    }

    private static byte[] HashNcgr(NcgrCharacterGraphics value)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendInt32(hash, value.Width);
        AppendInt32(hash, value.Height);
        AppendInt32(hash, (int)value.Depth);
        AppendInt32(hash, (int)value.Mapping);
        AppendUInt32(hash, value.StorageFlags);
        hash.AppendData(value.Pixels.ToArray());
        return hash.GetHashAndReset();
    }

    private static byte[] HashNscr(NscrScreenMap value)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendInt32(hash, value.Width);
        AppendInt32(hash, value.Height);
        AppendInt32(hash, (int)value.PaletteSelection);
        AppendInt32(hash, (int)value.BackgroundKind);
        foreach (NscrMapEntry entry in value.Entries)
        {
            AppendUInt16(hash, entry.PackedValue);
        }

        return hash.GetHashAndReset();
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

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendUInt32(IncrementalHash hash, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendUInt16(IncrementalHash hash, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static uint ReadRawFormat(ReadOnlySpan<byte> data)
    {
        int cursor = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        for (int index = 0; index < blockCount; index++)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 4)..]));
            if (data.Slice(cursor, 4).SequenceEqual("RAHC"u8))
            {
                return BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 12)..]);
            }

            cursor += length;
        }

        throw new InvalidDataException("The graphics resource has no CHAR block.");
    }
}
