using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NdsForge.Nitro.Text;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Exercises every direct BMG message bundle in the private legal-dump corpus.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusBmgTests
{
    /// <summary>Locks lossless message, metadata, and control-sequence semantics to the reviewed compatibility baseline.</summary>
    [Fact]
    public async Task EveryBmgPreservesAndMatchesGoldenSemantics()
    {
        int bundleCount = 0;
        long messageCount = 0;
        long controlCount = 0;
        long missingTrailingCount = 0;
        long missingPaddingCount = 0;
        var encodingCounts = new Dictionary<BmgEncoding, long>();
        var digests = new List<byte[]>();
        byte[] signature = new byte[8];
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry), cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            foreach (NdsFileAllocation allocation in image.FileSystem.Allocations)
            {
                using Stream stream = image.OpenRead(allocation.Data);
                if (stream.Read(signature) != signature.Length || !signature.AsSpan().SequenceEqual("MESGbmg1"u8)) continue;
                stream.Position = 0;
                byte[] encoded = new byte[allocation.Data.Length];
                stream.ReadExactly(encoded);
                BmgMessageBundle bundle = BmgMessageBundle.Parse(encoded);
                Assert.Equal(encoded, bundle.WritePreserved());
                bundleCount++;
                messageCount += bundle.Messages.Count;
                controlCount += bundle.Messages.Sum(static message =>
                    message.Parts.Count(static part => part.Kind == BmgMessagePartKind.Control));
                if (bundle.HasMissingTrailingSection) missingTrailingCount++;
                if (bundle.HasMissingTrailingPadding) missingPaddingCount++;
                encodingCounts[bundle.Encoding] = encodingCounts.GetValueOrDefault(bundle.Encoding) + 1;
                foreach (BmgMessage message in bundle.Messages)
                {
                    if (bundle.Encoding != BmgEncoding.ShiftJis) _ = message.GetText();
                }
                digests.Add(Hash(bundle));
            }
        }

        Assert.Equal(563, bundleCount);
        Assert.Equal(69943, messageCount);
        Assert.Equal(141795, controlCount);
        Assert.Equal(231, missingTrailingCount);
        Assert.Equal(76, encodingCounts[BmgEncoding.Windows1252]);
        Assert.Equal(446, encodingCounts[BmgEncoding.Utf16]);
        Assert.Equal(41, encodingCounts[BmgEncoding.ShiftJis]);
        Assert.Equal(114, missingPaddingCount);
        Assert.Equal("4A8F51A30181671E83EBBC247D0098856B7A72A8AD2D90BB4CEC91CB709C78DD", Aggregate(digests));
    }

    private static byte[] Hash(BmgMessageBundle value)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData([(byte)value.ByteOrder, (byte)value.Encoding,
            value.HasMissingTrailingSection ? (byte)1 : (byte)0,
            value.HasMissingTrailingPadding ? (byte)1 : (byte)0]);
        Append(hash, value.MessageId); Append(hash, value.HeaderField14); Append(hash, value.HeaderField18);
        Append(hash, value.HeaderField1C); Append(hash, value.DeclaredSectionCount);
        foreach (BmgMessage message in value.Messages)
        {
            Append(hash, message.DataOffset);
            hash.AppendData(message.Attributes.Span);
            foreach (BmgMessagePart part in message.Parts)
            {
                hash.AppendData([(byte)part.Kind, part.ControlCode ?? 0, part.SerializedLength]);
                hash.AppendData(part.Data.Span);
            }
        }
        foreach (BmgAuxiliarySection section in value.AuxiliarySections)
        {
            hash.AppendData(Encoding.ASCII.GetBytes(section.Signature));
            hash.AppendData(section.Data.Span);
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

    private static void Append(IncrementalHash hash, uint value)
    {
        Span<byte> data = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        hash.AppendData(data);
    }
}
