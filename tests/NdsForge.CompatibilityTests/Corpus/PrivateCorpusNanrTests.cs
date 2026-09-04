using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Graphics.Animations;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Exercises every direct NANR animation bank in the private legal-dump corpus.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusNanrTests
{
    /// <summary>Locks exact sequence and cell-reference semantics to the reviewed compatibility baseline.</summary>
    [Fact]
    public async Task EveryNanrPreservesAndMatchesGoldenSemantics()
    {
        int bankCount = 0;
        long sequenceCount = 0;
        long frameCount = 0;
        var typeCounts = new Dictionary<ushort, long>();
        var digests = new List<byte[]>();
        byte[] signature = new byte[4];
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry), cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            foreach (NdsFileAllocation allocation in image.FileSystem.Allocations)
            {
                using Stream stream = image.OpenRead(allocation.Data);
                if (stream.Read(signature) != signature.Length || !signature.AsSpan().SequenceEqual("RNAN"u8)) continue;
                stream.Position = 0;
                byte[] encoded = new byte[allocation.Data.Length];
                stream.ReadExactly(encoded);
                NanrAnimationBank bank = NanrAnimationBank.Parse(encoded);
                Assert.Equal(encoded, bank.WritePreserved());
                Assert.Equal(bank.DeclaredFrameCount, bank.Sequences.Sum(static sequence => sequence.Frames.Count));
                bankCount++;
                sequenceCount += bank.Sequences.Count;
                frameCount += bank.DeclaredFrameCount;
                foreach (NanrSequence sequence in bank.Sequences)
                    typeCounts[sequence.DataType] = typeCounts.GetValueOrDefault(sequence.DataType) + 1;
                digests.Add(Hash(bank));
            }
        }

        Assert.Equal(5719, bankCount);
        Assert.Equal(37364, sequenceCount);
        Assert.Equal(161873, frameCount);
        Assert.Equal(31717, typeCounts[0]);
        Assert.Equal(1605, typeCounts[1]);
        Assert.Equal(4042, typeCounts[2]);
        Assert.Equal("1C100DBBB2400A03E5523A5027EECC36C0D031771FAE0102582913EB3E89B2D0", Aggregate(digests));
    }

    private static byte[] Hash(NanrAnimationBank value)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, value.Version);
        Append(hash, value.DeclaredFrameCount);
        Append(hash, value.BankConstant);
        Append(hash, value.FrameDescriptorOffset);
        Append(hash, value.FramePayloadOffset);
        foreach (NanrSequence sequence in value.Sequences)
        {
            Append(hash, sequence.DataType);
            Append(hash, sequence.PlaybackMode);
            Append(hash, sequence.LoopStartFrame);
            Append(hash, sequence.SequenceFlags);
            Append(hash, sequence.FrameOffset);
            foreach (NanrFrame frame in sequence.Frames)
            {
                Append(hash, frame.DataOffset);
                Append(hash, frame.Duration);
                Append(hash, frame.DescriptorFlags);
                Append(hash, frame.CellIndex);
            }
        }
        hash.AppendData(value.LabelData.Span);
        hash.AppendData(value.UserExtendedInfo.Span);
        return hash.GetHashAndReset();
    }

    private static string Aggregate(IEnumerable<byte[]> digests)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] digest in digests.OrderBy(static item => Convert.ToHexString(item), StringComparer.Ordinal))
            hash.AppendData(digest);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

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
