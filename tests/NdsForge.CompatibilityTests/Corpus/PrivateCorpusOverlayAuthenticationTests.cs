using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Verifies every independently characterized classic-DS per-overlay authentication table.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusOverlayAuthenticationTests
{
    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task EveryDownloadPlayRecordMatchesDecodedProgramMetadataAndStoredPayloads()
    {
        int imageCount = 0;
        int recordCount = 0;
        int compressedProgramCount = 0;
        using IncrementalHash canonical = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries
            .OrderBy(static item => item.RomSha256, StringComparer.Ordinal))
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            NdsOverlayAuthenticationTable? table = image.Arm9OverlayAuthentication;
            if (table is null)
            {
                continue;
            }

            imageCount++;
            Assert.Equal(NdsOverlayAuthenticationTableState.Complete, table.State);
            Assert.Equal(image.Arm9Overlays.Count, table.Records.Count);
            Assert.All(image.Arm9Overlays, static overlay => Assert.NotNull(overlay.AuthenticationRecord));
            Assert.DoesNotContain(
                image.Validate().Diagnostics,
                static diagnostic => diagnostic.Code.StartsWith("NDS121", StringComparison.Ordinal));
            recordCount += table.Records.Count;
            compressedProgramCount += table.ProgramStorage == NdsProgramStorageEncoding.Blz ? 1 : 0;

            canonical.AppendData(Convert.FromHexString(entry.RomSha256));
            using var data = new MemoryStream();
            using (var writer = new BinaryWriter(data, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(checked((int)table.RelativeOffset));
                writer.Write(table.DecodedProgramLength);
                writer.Write(table.ProgramStorage == NdsProgramStorageEncoding.Blz);
                writer.Write(table.UncompressedPrefixLength);
                writer.Write(table.Records.Count);
                foreach (NdsOverlayAuthenticationRecord record in table.Records)
                {
                    writer.Write(record.HmacSha1.Span);
                }
            }

            canonical.AppendData(data.GetBuffer().AsSpan(0, checked((int)data.Length)));
        }

        Assert.Equal(11, imageCount);
        Assert.Equal(1075, recordCount);
        Assert.Equal(9, compressedProgramCount);
        Assert.Equal(
            "6E27868A85C1EBFC793107EA58F6767342AB13344B06BCD0FCE1AC535CEA955B",
            Convert.ToHexString(canonical.GetHashAndReset()));
    }
}
