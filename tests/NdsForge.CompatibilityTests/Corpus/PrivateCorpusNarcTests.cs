using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NdsForge.Nitro.Archives;
using NdsForge.Nitro.Containers;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Parses, preserves, and reconstructs every direct NARC allocation in the private ROM corpus.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusNarcTests
{
    /// <summary>Locks all valid archive metadata and payloads after exact compiled-unpacker comparison.</summary>
    [Fact]
    public async Task EveryValidNarcPreservesAndRebuildsWithoutSemanticDrift()
    {
        int magicCount = 0;
        int archiveCount = 0;
        int littleEndianCount = 0;
        int bigEndianCount = 0;
        long fileCount = 0;
        long namedFileCount = 0;
        var archiveDigests = new List<byte[]>();
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            foreach (NdsFileAllocation allocation in image.FileSystem.Allocations)
            {
                using Stream stream = image.OpenRead(allocation.Data);
                Span<byte> signature = new byte[4];
                if (stream.Read(signature) != signature.Length || !signature.SequenceEqual("NARC"u8))
                {
                    continue;
                }

                magicCount++;
                stream.Position = 0;
                byte[] encoded = new byte[allocation.Data.Length];
                stream.ReadExactly(encoded);
                NarcArchive archive;
                try
                {
                    archive = NarcArchive.Parse(encoded);
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                Assert.Equal(encoded, archive.CreateBuilder().Build());
                byte[] canonical = archive.CreateBuilder().Build(new NarcWriteOptions { PreserveSourceLayout = false });
                NarcArchive reparsed = NarcArchive.Parse(canonical);
                Assert.Equal(archive.Files.Count, reparsed.Files.Count);
                Assert.All(archive.Files, file => Assert.True(file.Data.Span.SequenceEqual(reparsed.GetFile(file.Id).Data.Span)));

                archiveCount++;
                littleEndianCount += archive.HeaderByteOrder == NitroByteOrder.LittleEndian ? 1 : 0;
                bigEndianCount += archive.HeaderByteOrder == NitroByteOrder.BigEndian ? 1 : 0;
                fileCount += archive.Files.Count;
                namedFileCount += archive.Files.Count(static file => file.FullPath is not null);
                archiveDigests.Add(HashArchive(archive));
            }
        }

        using IncrementalHash aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] digest in archiveDigests.OrderBy(static value => Convert.ToHexString(value), StringComparer.Ordinal))
        {
            aggregate.AppendData(digest);
        }

        Assert.Multiple(
            () => Assert.Equal(13637, magicCount),
            () => Assert.Equal(13630, archiveCount),
            () => Assert.Equal(0, littleEndianCount),
            () => Assert.Equal(13630, bigEndianCount),
            () => Assert.Equal(940219, fileCount),
            () => Assert.Equal(58031, namedFileCount),
            () => CorpusExpectations.AssertDigest(
                "4A99CD11E1DEE7385E96C676FE0AEF4F97F94F1815E4635E277F7A3EBC3EF8DF",
                Convert.ToHexString(aggregate.GetHashAndReset())));
    }

    /// <summary>Frames IDs, optional byte-preserving paths, and exact payloads into one per-archive digest.</summary>
    private static byte[] HashArchive(NarcArchive archive)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData([(byte)archive.HeaderByteOrder]);
        foreach (NarcFile file in archive.Files)
        {
            AppendInt32(hash, file.Id);
            byte[] path = Encoding.UTF8.GetBytes(file.FullPath ?? string.Empty);
            AppendInt32(hash, path.Length);
            hash.AppendData(path);
            AppendInt32(hash, file.Data.Length);
            hash.AppendData(file.Data.Span);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>Uses an explicit little-endian integer frame independent of the test host architecture.</summary>
    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> encoded = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(encoded, value);
        hash.AppendData(encoded);
    }
}
