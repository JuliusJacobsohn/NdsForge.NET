using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NdsForge.Nitro.Archives;
using NdsForge.Nitro.Audio;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Checks every direct or archive-contained native stream against neutral sample and metadata expectations.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusStrmTests
{
    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task EveryStreamPreservesRebuildsAndMatchesSamplesAndChannelLayout()
    {
        var records = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        int occurrences = 0, invalidArchives = 0, legacy = 0;
        long sampleValues = 0;
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            foreach (NdsFileAllocation allocation in image.FileSystem.Allocations)
            {
                using Stream input = image.OpenRead(allocation.Data);
                byte[] signature = new byte[4];
                if (input.Read(signature) != 4 || !IsContainer(signature)) { continue; }
                input.Position = 0;
                byte[] bytes = new byte[allocation.Data.Length];
                input.ReadExactly(bytes);
                Inspect(bytes, 0);
            }
        }
        Assert.Equal(7537, occurrences);
        Assert.Equal(7052, records.Count);
        Assert.Equal(7, invalidArchives);
        Assert.Equal(7, legacy);
        Assert.Equal(1139964284, sampleValues);
        using IncrementalHash aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] record in records.Values) { aggregate.AppendData(record); }
        CorpusExpectations.AssertDigest("051348A3F3DE32CC168DAB3185C453637FD93BFDA5A8A7F59889B74952E3FDDD", Convert.ToHexString(aggregate.GetHashAndReset()));

        void Inspect(ReadOnlyMemory<byte> bytes, int depth)
        {
            Assert.InRange(depth, 0, 16);
            if (bytes.Length < 4) { return; }
            if (bytes.Span[..4].SequenceEqual("STRM"u8))
            {
                occurrences++;
                StrmFile file = StrmFile.Parse(bytes.Span);
                Assert.Equal(bytes.ToArray(), file.WritePreserved());
                Assert.Equal(bytes.ToArray(), file.CreateBuilder().Build());
                string identity = Convert.ToHexString(SHA256.HashData(bytes.Span[..file.DeclaredLength]));
                if (records.ContainsKey(identity)) { return; }
                StrmFile canonical = StrmFile.Parse(file.CreateBuilder().Build(new() { PreserveSourceLayout = false }));
                var decodeOptions = new NitroWaveDecodeOptions { MaximumSamples = 32 * 1024 * 1024, AdpcmClipping = NitroAdpcmClipping.Signed16 };
                byte[] pcm = SampleBytes(file.Decode(decodeOptions));
                Assert.Equal(pcm, SampleBytes(canonical.Decode(decodeOptions)));
                Assert.Equal(file.SampleValueCount * 2, pcm.Length);
                records.Add(identity, Frame(identity, file, SHA256.HashData(pcm)));
                legacy += file.ExcludesAdpcmStateHeaderFromLength ? 1 : 0;
                sampleValues += file.SampleValueCount;
            }
            else if (bytes.Span[..4].SequenceEqual("NARC"u8))
            {
                NarcArchive archive;
                try { archive = NarcArchive.Parse(bytes.Span); }
                catch (InvalidDataException) { invalidArchives++; return; }
                foreach (NarcFile file in archive.Files) { Inspect(file.Data, depth + 1); }
            }
            else if (bytes.Span[..4].SequenceEqual("SDAT"u8))
            {
                Assert.True(bytes.Length >= 64);
                int table = checked((int)U32(bytes.Span, 32));
                Assert.InRange(table, 64, bytes.Length - 12);
                Assert.True(bytes.Span.Slice(table, 4).SequenceEqual("FAT "u8));
                int count = checked((int)U32(bytes.Span, table + 8));
                Assert.InRange(count, 0, (bytes.Length - table - 12) / 16);
                for (int i = 0; i < count; i++)
                {
                    int offset = checked((int)U32(bytes.Span, table + 12 + i * 16));
                    int length = checked((int)U32(bytes.Span, table + 16 + i * 16));
                    Assert.InRange(offset, 0, bytes.Length);
                    Assert.InRange(length, 0, bytes.Length - offset);
                    Inspect(bytes.Slice(offset, length), depth + 1);
                }
            }
        }
    }

    private static byte[] Frame(string identity, StrmFile file, byte[] sampleHash)
    {
        using var frame = new MemoryStream();
        using var writer = new BinaryWriter(frame, Encoding.UTF8, true);
        writer.Write(Convert.FromHexString(identity));
        long[] fields = [(int)file.Encoding, file.ChannelCount, file.SampleRate, file.Timer, file.RawLoopFlag, file.RawLoopStartSample,
            file.BlocksPerChannel, file.NormalBlockByteLength, file.NormalBlockSampleCount, file.FinalBlockByteLength, file.FinalBlockSampleCount];
        foreach (long field in fields) { writer.Write(field); }
        writer.Write(file.SampleCount);
        writer.Write(file.SampleValueCount);
        writer.Write(file.ExcludesAdpcmStateHeaderFromLength);
        for (int c = 0; c < file.ChannelCount; c++)
        {
            using IncrementalHash channel = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int b = 0; b < file.BlocksPerChannel; b++) { channel.AppendData(file.GetBlock(b, c).EncodedData.Span); }
            writer.Write(channel.GetHashAndReset());
        }
        writer.Write(sampleHash);
        writer.Flush();
        return frame.ToArray();
    }

    private static byte[] SampleBytes(short[] samples)
    {
        byte[] bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++) { BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]); }
        return bytes;
    }

    private static bool IsContainer(ReadOnlySpan<byte> signature) => signature.SequenceEqual("STRM"u8) || signature.SequenceEqual("SDAT"u8) || signature.SequenceEqual("NARC"u8);
    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
}
