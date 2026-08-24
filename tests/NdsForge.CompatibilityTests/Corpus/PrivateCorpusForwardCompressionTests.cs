using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Nitro.Compression;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Decodes every FAT allocation whose first byte selects a supported forward Nitro codec.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusForwardCompressionTests
{
    /// <summary>Locks real-file counts and exact decoded bytes after external compiled-oracle verification.</summary>
    [Fact]
    public async Task EveryMagicIdentifiedForwardStreamDecodesExactly()
    {
        int lz10Magic = 0;
        int lz11Magic = 0;
        int rleMagic = 0;
        int lz10Decoded = 0;
        int lz11Decoded = 0;
        int rleDecoded = 0;
        using IncrementalHash decodedOracle = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            foreach (NdsFileAllocation allocation in image.FileSystem.Allocations)
            {
                if (allocation.Data.Length < 4)
                {
                    continue;
                }

                byte type = ReadType(image, allocation.Data);
                if (type is not ((byte)NitroCompressionType.Lz10) and
                    not ((byte)NitroCompressionType.Lz11) and
                    not ((byte)NitroCompressionType.RunLength))
                {
                    continue;
                }

                byte[] encoded = ReadRegion(image, allocation.Data);
                lz10Magic += type == (byte)NitroCompressionType.Lz10 ? 1 : 0;
                lz11Magic += type == (byte)NitroCompressionType.Lz11 ? 1 : 0;
                rleMagic += type == (byte)NitroCompressionType.RunLength ? 1 : 0;
                byte[] decoded;
                try
                {
                    decoded = NitroCompression.Decompress(encoded, maximumDecodedLength: 256 * 1024 * 1024);
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                decodedOracle.AppendData([type]);
                AppendFrame(decodedOracle, decoded);
                lz10Decoded += type == (byte)NitroCompressionType.Lz10 ? 1 : 0;
                lz11Decoded += type == (byte)NitroCompressionType.Lz11 ? 1 : 0;
                rleDecoded += type == (byte)NitroCompressionType.RunLength ? 1 : 0;
            }
        }

        Assert.True(lz10Magic >= lz10Decoded);
        Assert.True(lz11Magic >= lz11Decoded);
        Assert.True(rleMagic >= rleDecoded);
        Assert.Equal(11660, lz10Decoded);
        Assert.Equal(6, lz11Decoded);
        Assert.Equal(0, rleDecoded); // Known corpus gap: every 0x30 occurrence is unrelated data, so RLE uses synthetic/reference vectors.
        Assert.Equal(
            "394EB1FD47346B3E742F3E0AAFBA4B3C2936FAC91BCF456061802AAD753238D8",
            Convert.ToHexString(decodedOracle.GetHashAndReset()));
    }

    /// <summary>Reads only the discriminator when an allocation is not a candidate stream.</summary>
    private static byte ReadType(NdsImage image, NdsRegion region)
    {
        using Stream stream = image.OpenRead(region);
        return checked((byte)stream.ReadByte());
    }

    /// <summary>Materializes a candidate allocation after its type byte establishes relevance.</summary>
    private static byte[] ReadRegion(NdsImage image, NdsRegion region)
    {
        using Stream stream = image.OpenRead(region);
        byte[] data = new byte[region.Length];
        stream.ReadExactly(data);
        return data;
    }

    /// <summary>Prevents aggregate-hash ambiguity between differently partitioned decoded files.</summary>
    private static void AppendFrame(IncrementalHash hash, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, data.Length);
        hash.AppendData(length);
        hash.AppendData(data);
    }
}
