using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Graphics.Sprites;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Exercises every direct NCER cell bank in the private legal-dump corpus.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusNcerTests
{
    /// <summary>Locks exact OAM semantics after field-for-field comparison with compiled Texim.</summary>
    [Fact]
    public async Task EveryNcerPreservesAndCanonicallyRebuilds()
    {
        int archiveCount = 0;
        long cellCount = 0;
        long objectCount = 0;
        var digests = new List<byte[]>();
        byte[] signature = new byte[4];
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry), cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            foreach (NdsFileAllocation allocation in image.FileSystem.Allocations)
            {
                using Stream stream = image.OpenRead(allocation.Data);
                if (stream.Read(signature) != signature.Length || !signature.AsSpan().SequenceEqual("RECN"u8)) continue;
                stream.Position = 0;
                byte[] encoded = new byte[allocation.Data.Length];
                stream.ReadExactly(encoded);
                NcerCellBank bank = NcerCellBank.Parse(encoded);
                Assert.Equal(encoded, bank.CreateBuilder().Build());
                NcerCellBank canonical = NcerCellBank.Parse(bank.CreateBuilder().Build(preserveSourceLayout: false));
                Assert.Equal(bank.Attributes, canonical.Attributes);
                Assert.Equal(bank.Mapping, canonical.Mapping);
                Assert.Equal(bank.LabelData, canonical.LabelData);
                Assert.Equal(bank.UserExtendedInfo, canonical.UserExtendedInfo);
                Assert.Equal(bank.Cells.Count, canonical.Cells.Count);
                for (int index = 0; index < bank.Cells.Count; index++)
                {
                    Assert.Equal(bank.Cells[index].Attributes, canonical.Cells[index].Attributes);
                    Assert.Equal(bank.Cells[index].Bounds, canonical.Cells[index].Bounds);
                    Assert.Equal(bank.Cells[index].Objects, canonical.Cells[index].Objects);
                }
                archiveCount++;
                cellCount += bank.Cells.Count;
                objectCount += bank.Cells.Sum(cell => cell.Objects.Count);
                digests.Add(Hash(bank));
            }
        }
        Assert.Equal(3126, archiveCount);
        Assert.Equal(50509, cellCount);
        Assert.Equal(178097, objectCount);
        Assert.Equal("D65287A5A9EF76DAAEEC97E52A4F5EC090BD00858DC443F8B3CDF1A699FA18AF", Aggregate(digests));
    }

    private static byte[] Hash(NcerCellBank value)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, value.Attributes); AppendInt32(hash, (int)value.Mapping);
        foreach (NcerCell cell in value.Cells)
        {
            Append(hash, cell.Attributes);
            if (cell.Bounds is NcerCellBounds bounds)
            {
                Append(hash, (ushort)bounds.MinimumX); Append(hash, (ushort)bounds.MinimumY);
                Append(hash, (ushort)bounds.MaximumX); Append(hash, (ushort)bounds.MaximumY);
            }
            foreach (NitroObjectEntry obj in cell.Objects)
            {
                Append(hash, obj.Attribute0); Append(hash, obj.Attribute1); Append(hash, obj.Attribute2);
            }
        }
        hash.AppendData(value.LabelData.Span); hash.AppendData(value.UserExtendedInfo.Span);
        return hash.GetHashAndReset();
    }

    private static string Aggregate(IEnumerable<byte[]> digests)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] digest in digests.OrderBy(static item => Convert.ToHexString(item), StringComparer.Ordinal)) hash.AppendData(digest);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, ushort value)
    {
        Span<byte> data = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(data, value);
        hash.AppendData(data);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> data = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(data, value);
        hash.AppendData(data);
    }
}
