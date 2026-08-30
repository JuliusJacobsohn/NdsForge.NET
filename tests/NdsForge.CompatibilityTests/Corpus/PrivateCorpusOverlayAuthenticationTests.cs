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
        int declaredImageCount = 0;
        int completeImageCount = 0;
        int missingTablePointerCount = 0;
        int missingTableOverlayCount = 0;
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

            declaredImageCount++;
            if (table.State == NdsOverlayAuthenticationTableState.MissingTablePointer)
            {
                missingTablePointerCount++;
                missingTableOverlayCount += image.Arm9Overlays.Count;
                Assert.Empty(table.Records);
                Assert.All(image.Arm9Overlays, static overlay => Assert.Null(overlay.AuthenticationRecord));
                Assert.Contains(image.Validate().Diagnostics, static diagnostic => diagnostic.Code == "NDS1211");
                continue;
            }

            completeImageCount++;
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

        Assert.Equal(16, declaredImageCount);
        Assert.Equal(15, completeImageCount);
        Assert.Equal(1, missingTablePointerCount);
        Assert.Equal(44, missingTableOverlayCount);
        Assert.Equal(1399, recordCount);
        Assert.Equal(13, compressedProgramCount);
        Assert.Equal(
            "5EFB9C1A7B4326A4D0B3B29B6D16E9F0293B4A3488062E63F0E51E19DBB755B0",
            Convert.ToHexString(canonical.GetHashAndReset()));
    }
}
