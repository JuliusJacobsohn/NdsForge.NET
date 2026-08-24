using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Nitro.Compression;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Exercises every FAT allocation whose first byte resembles a Nitro Huffman stream.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusHuffmanTests
{
    /// <summary>Locks exact output for all genuine streams after comparison with an independently compiled decoder.</summary>
    [Fact]
    public async Task EveryStructurallyValidHuffmanStreamMatchesTheVerifiedCorpusDigest()
    {
        int huffman4Magic = 0;
        int huffman8Magic = 0;
        int huffman4Decoded = 0;
        int huffman8Decoded = 0;
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

                using Stream stream = image.OpenRead(allocation.Data);
                int discriminator = stream.ReadByte();
                if (discriminator is not ((byte)NitroCompressionType.Huffman4) and
                    not ((byte)NitroCompressionType.Huffman8))
                {
                    continue;
                }

                byte type = checked((byte)discriminator);
                huffman4Magic += type == (byte)NitroCompressionType.Huffman4 ? 1 : 0;
                huffman8Magic += type == (byte)NitroCompressionType.Huffman8 ? 1 : 0;
                stream.Position = 0;
                byte[] encoded = new byte[allocation.Data.Length];
                stream.ReadExactly(encoded);
                byte[] decoded;
                try
                {
                    decoded = HuffmanCodec.Decompress(encoded, maximumDecodedLength: 256 * 1024 * 1024);
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                decodedOracle.AppendData([type]);
                AppendFrame(decodedOracle, decoded);
                huffman4Decoded += type == (byte)NitroCompressionType.Huffman4 ? 1 : 0;
                huffman8Decoded += type == (byte)NitroCompressionType.Huffman8 ? 1 : 0;
            }
        }

        Assert.True(huffman4Magic >= huffman4Decoded);
        Assert.True(huffman8Magic >= huffman8Decoded);
        Assert.Equal(0, huffman4Decoded); // Known corpus gap: no 4-bit stream survives strict tree and leaf validation.
        Assert.Equal(65, huffman8Decoded);
        Assert.Equal(
            "DF346F895583C8FC7953D932020F0C7ACD9809A16F96983C1657404B3883569F",
            Convert.ToHexString(decodedOracle.GetHashAndReset()));
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
