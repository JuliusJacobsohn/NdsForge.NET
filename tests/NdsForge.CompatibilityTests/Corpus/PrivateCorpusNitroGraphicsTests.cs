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

        Assert.Equal(5035, ncgrCount);
        Assert.Equal(126167104, pixelCount);
        Assert.Equal(13, rejectedNcgrMagic);
        Assert.Equal(1231, nscrCount);
        Assert.Equal(1274624, entryCount);
        Assert.Equal("CE1D1001D20C5A52F3DCD5F2B59E479304379203B564E32509732ED54D0649B9", Aggregate(ncgrDigests));
        Assert.Equal("87A85766F5BC042C448D4D41587EABC90C7C24C973835492064D3EA8A7AF6B1B", Aggregate(nscrDigests));
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
}
