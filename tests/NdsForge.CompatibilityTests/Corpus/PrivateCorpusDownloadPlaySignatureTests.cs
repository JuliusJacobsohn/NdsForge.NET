using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Locks trailer presence and stored bytes to corpus identities while exercising lossless copies and semantic writes.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusDownloadPlaySignatureTests
{
    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task EveryStoredTrailerMatchesItsDigestAndSurvivesCopiesRebuildsAndEdits()
    {
        int count = 0;
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string destination = Path.Combine(Path.GetTempPath(), $"ndsforge-corpus-trailer-{Guid.NewGuid():N}.nds");
        try
        {
            foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries.OrderBy(static item => item.RomSha256, StringComparer.Ordinal))
            {
                CancellationToken token = TestContext.Current.CancellationToken;
                using NdsImage image = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry), cancellationToken: token).ConfigureAwait(true);
                if (image.DownloadPlaySignature is not { } trailer)
                {
                    Assert.Null(image.DownloadPlaySignatureRegion);
                    Assert.DoesNotContain(image.Validate().Diagnostics, static item => item.Code == "NDS1551");
                    continue;
                }

                count++;
                Assert.Equal(new NdsRegion(image.Header.UsedImageSize, 136), image.DownloadPlaySignatureRegion);
                digest.AppendData(Convert.FromHexString(entry.RomSha256));
                digest.AppendData(trailer.RawData.Span);
                NdsSaveResult copied = await image.Edit().SaveAsync(destination,
                    new() { OverwriteDestination = true }, token).ConfigureAwait(true);
                Assert.Empty(copied.Diagnostics);
                await using (FileStream stream = File.OpenRead(destination))
                {
                    Assert.Equal(entry.RomSha256, Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(true)), ignoreCase: true);
                }

                NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(image, token).ConfigureAwait(true);
                if (builder.DsMetadata is not null) { builder.DsMetadata.Integrity = NdsDsIntegrityOptions.PreserveStored; }
                NdsImageBuildResult rebuilt = await builder.WriteAsync(destination, new() { OverwriteDestination = true }, token).ConfigureAwait(true);
                Assert.Contains(rebuilt.Diagnostics, static item => item.Code == "NDS1550");
                await AssertTrailerAsync(destination, trailer, token).ConfigureAwait(true);

                NdsImageEditor editor = image.Edit();
                editor.Header.Title = "TRAILER TEST";
                NdsSaveResult edited = await editor.SaveAsync(destination, new()
                {
                    OverwriteDestination = true,
                    DsIntegrity = image.Header.DsExtended is null ? null : NdsDsIntegrityOptions.PreserveStored,
                }, token).ConfigureAwait(true);
                Assert.Contains(edited.Diagnostics, static item => item.Code == "NDS1550");
                await AssertTrailerAsync(destination, trailer, token).ConfigureAwait(true);
            }
        }
        finally { File.Delete(destination); }

        Assert.Equal(14, count);
        CorpusExpectations.AssertDigest("683180104A5B9F2A9EF706923A57DCAFB1A6ACC3A8B28AF708179200F861FD6F",
            Convert.ToHexString(digest.GetHashAndReset()));
    }

    private static async Task AssertTrailerAsync(string path, NdsDownloadPlaySignature expected, CancellationToken cancellationToken)
    {
        using NdsImage output = await NdsImage.OpenAsync(path, cancellationToken: cancellationToken).ConfigureAwait(true);
        Assert.NotNull(output.DownloadPlaySignature);
        Assert.Equal(expected.RawData.ToArray(), output.DownloadPlaySignature.RawData.ToArray());
        Assert.Equal(expected.Seed, output.DownloadPlaySignature.Seed);
        Assert.True(output.DownloadPlaySignatureRegion!.Value.End <= output.Length);
    }
}
