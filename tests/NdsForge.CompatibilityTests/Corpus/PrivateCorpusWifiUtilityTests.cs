using System.Security.Cryptography;
using System.Text;
using NdsForge.Nitro.Archives;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Locks all directly named utility archives to neutral structural, payload, and canonical-output identities.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusWifiUtilityTests
{
    private static readonly Dictionary<string, (int Count, string Aligned4, string Aligned32)> Expected =
        new Dictionary<string, (int, string, string)>(StringComparer.Ordinal)
        {
            ["1D2EC39E86493319E724DC1DEEF8DA848B794A4BB36176218FD6532727DB4774"] =
                (12, "ACAEAAB37F1F63F78FAA6BEB5BC4B34EDC274E6252C1656B8EB948326655C71D", "02412FD7EDA0A25D2FD496A7347796060EB64E26D9D723F06A72BB1795783E01"),
            ["3566C133F22189086235B940A9F482D963A9B892D6AA91331737294A611EE668"] =
                (8, "FDBF03FA54EA4A7910D411781D34F4EB6926A91C7874ADA886A02A0B6AEDBACA", "4B69C7FF3F87F919ED9C1CDC50D2B90C2C8445D43DF9855C2008F2B4BC91E022"),
            ["923611FF1131D11EEC8BC4FFEF414BCA17712B79244FB6742A43B3CD5EDF24FB"] =
                (12, "2540237DFE7E4FF1C5BA16ED1DE0AABBC7AE9924F60A9FCA6928F4491F91FBB0", "CAD8DBDFBE4C964F73211F2E52E7A1445DB99741EF14EECEC2DA0728C0A8C19D"),
            ["DB8EE36170897F60791F713B7669C50A125E62C6AD775FDA98466C85FE48E81E"] =
                (47, "CC61B75F8798C72CCD3FCCAE1656E80F745397EA48EF79E767922DF38ADBDDD6", "9B42CF6505E9C0E91C18635E3AE7AA65494B9D5884DBAEE870CBE43A0B9FEB56"),
        };

    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task EveryUtilityArchiveRetainsAllMetadataAndMatchesCanonicalLayouts()
    {
        var unique = new Dictionary<string, WifiUtilityArchive>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        int sourceCount = 0;
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            bool found = false;
            foreach (NdsFile file in image.FileSystem.Files.Where(static file => file.Name.Equals("utility.bin", StringComparison.OrdinalIgnoreCase)))
            {
                byte[] bytes = await file.ReadAllBytesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
                string identity = Convert.ToHexString(SHA256.HashData(bytes));
                Assert.True(Expected.ContainsKey(identity), $"Unexpected utility identity {identity}.");
                WifiUtilityArchive archive = WifiUtilityArchive.Parse(bytes);
                Assert.Equal(bytes, archive.WritePreserved());
                Assert.Equal(bytes, archive.CreateBuilder().Build());
                occurrences[identity] = occurrences.GetValueOrDefault(identity) + 1;
                unique.TryAdd(identity, archive);
                found = true;
            }
            if (found) { sourceCount++; }
        }
        Assert.Equal(65, sourceCount);
        Assert.Equal(79, occurrences.Values.Sum());
        Assert.Equal(4, unique.Count);
        Assert.Equal(1472, unique.Values.Sum(static archive => archive.Files.Count));
        using var framed = new MemoryStream();
        using var writer = new BinaryWriter(framed, Encoding.UTF8, leaveOpen: true);
        foreach ((string identity, WifiUtilityArchive archive) in unique.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            Assert.Equal(Expected[identity].Count, occurrences[identity]);
            VerifyCanonical(archive, 4, Expected[identity].Aligned4);
            VerifyCanonical(archive, 32, Expected[identity].Aligned32);
            writer.Write(Convert.FromHexString(identity));
            WriteInventory(writer, archive);
        }
        writer.Flush();
        Assert.Equal(96572, framed.Length);
        CorpusExpectations.AssertDigest("E04AF1E0FD5D2A0552F0187C3C73AC6EC80643DFC430481F7A9BA5A2F5A0CE2F",
            Convert.ToHexString(SHA256.HashData(framed.ToArray())));
    }

    private static void VerifyCanonical(WifiUtilityArchive source, int alignment, string expected)
    {
        byte[] bytes = source.CreateBuilder().Build(new() { PreserveSourceLayout = false, FileAlignment = alignment, TableAlignment = alignment });
        CorpusExpectations.AssertDigest(expected, Convert.ToHexString(SHA256.HashData(bytes)));
        WifiUtilityArchive rebuilt = WifiUtilityArchive.Parse(bytes);
        Assert.Equal(source.Files.Select(static file => (file.Id, file.FullPath, file.ParentId)),
            rebuilt.Files.Select(static file => (file.Id, file.FullPath, file.ParentId)));
        foreach (WifiUtilityFile file in source.Files) { Assert.Equal(file.Data.ToArray(), rebuilt.Files[file.Id].Data.ToArray()); }
    }

    private static void WriteInventory(BinaryWriter writer, WifiUtilityArchive archive)
    {
        writer.Write(archive.NameTableOffset);
        writer.Write(archive.NameTableLength);
        writer.Write(archive.AllocationTableOffset);
        writer.Write(archive.AllocationTableLength);
        writer.Write(archive.Directories.Count);
        writer.Write(archive.Files.Count);
        foreach (WifiUtilityDirectory directory in archive.Directories)
        {
            writer.Write((int)directory.Id);
            writer.Write(directory.ParentId ?? archive.Directories.Count);
            writer.Write((int)directory.FirstFileId);
            writer.Write(directory.NameSubtableOffset);
            writer.Write(directory.FullPath);
        }
        foreach (WifiUtilityFile file in archive.Files)
        {
            writer.Write(file.Id);
            writer.Write(file.Offset);
            writer.Write(file.Data.Length);
            writer.Write(file.FullPath ?? string.Empty);
            writer.Write(SHA256.HashData(file.Data.Span));
        }
    }
}
