using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Replays late-DS structural regeneration and overlay replacement using synthetic caller-owned credentials.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusDsAuthenticationWriteTests
{
    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task RegeneratedAndReplacedImagesMatchCompleteAuthenticationDigests()
    {
        byte[] programKey = Enumerable.Range(0, 64).Select(static index => (byte)index).ToArray();
        byte[] bannerKey = Enumerable.Range(0, 64).Select(static index => (byte)(255 - index)).ToArray();
        var secureKey = new NdsKey1KeyTable(Enumerable.Range(0, NdsKey1KeyTable.ByteLength)
            .Select(static index => (byte)(index * 37 + 11)).ToArray());
        using RSA rsa = RSA.Create(1024);
        using var signer = new NdsDsiRsaSignatureProvider(rsa);
        NdsDsiRsaPublicKey publicKey = NdsDsiRsaPublicKey.FromRsa(rsa);
        NdsDsIntegrityOptions policy = NdsDsIntegrityOptions.CreateHmacSha1(programKey, bannerKey, secureKey, signer, publicKey);
        NdsValidationOptions validation = new NdsValidationOptions().SetDsProgramHmacKey(programKey)
            .SetDsBannerHmacKey(bannerKey).SetSecureAreaKeyTable(secureKey).SetDsRsaPublicKey(publicKey);
        int images = 0, regenerated = 0, replaced = 0, rejected = 0, withoutOverlays = 0, classicRecords = 0;
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string destination = Path.Combine(Path.GetTempPath(), $"ndsforge-corpus-auth-{Guid.NewGuid():N}.nds");
        try
        {
            foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries.OrderBy(static item => item.RomSha256, StringComparer.Ordinal))
            {
                CancellationToken token = TestContext.Current.CancellationToken;
                using NdsImage source = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry), cancellationToken: token).ConfigureAwait(true);
                if (source.Header.DsExtended?.HasProgramAuthentication != true) { continue; }
                images++;
                NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(source, token).ConfigureAwait(true);
                builder.DsMetadata!.Integrity = policy;
                for (byte operation = 0; operation < 2; operation++)
                {
                    if (operation == 1)
                    {
                        if (source.Arm9Overlays.Count == 0) { withoutOverlays++; continue; }
                        builder.ReplaceOverlay(NdsProcessor.Arm9, source.Arm9Overlays[0].Id,
                            Enumerable.Repeat((byte)0x77, 4096).ToArray(), NdsOverlayCompressionMode.Blz);
                    }

                    if (source.Arm9OverlayAuthentication?.State == NdsOverlayAuthenticationTableState.MissingTablePointer)
                    {
                        await File.WriteAllBytesAsync(destination, new byte[] { 9, 8, 7 }, token).ConfigureAwait(true);
                        await Assert.ThrowsAsync<InvalidDataException>(async () => await builder.WriteAsync(destination,
                            new() { OverwriteDestination = true }, token).ConfigureAwait(true)).ConfigureAwait(true);
                        Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(destination, token).ConfigureAwait(true));
                        rejected++;
                        continue;
                    }

                    NdsImageBuildResult result = await builder.WriteAsync(destination, new() { OverwriteDestination = true }, token).ConfigureAwait(true);
                    Assert.Empty(result.Diagnostics);
                    using NdsImage output = await NdsImage.OpenAsync(destination, cancellationToken: token).ConfigureAwait(true);
                    NdsValidationResult checkedOutput = output.Validate(validation);
                    Assert.True(checkedOutput.IsValid, string.Join("; ", checkedOutput.Diagnostics.Select(static item => item.Message)));
                    Assert.DoesNotContain(checkedOutput.Diagnostics, static item => item.Code.StartsWith("NDS15", StringComparison.Ordinal));
                    NdsDsExtendedHeader extension = output.Header.DsExtended!;
                    digest.AppendData(Convert.FromHexString(entry.RomSha256));
                    digest.AppendData(new byte[] { operation });
                    digest.AppendData(extension.ProgramsHmac.Span);
                    digest.AppendData(extension.Arm9OverlaysHmac.Span);
                    digest.AppendData(extension.BannerHmac.Span);
                    classicRecords += output.Arm9Overlays.Count(static item => item.IsAuthenticated);
                    if (operation == 0) { regenerated++; } else { replaced++; }
                }
            }
        }
        finally { File.Delete(destination); }

        Assert.Equal(67, images);
        Assert.Equal(66, regenerated);
        Assert.Equal(58, replaced);
        Assert.Equal(2, rejected);
        Assert.Equal(8, withoutOverlays);
        Assert.Equal(630, classicRecords);
        CorpusExpectations.AssertDigest("82A3AA09A4081C283A4D4D03FBF6EDC9B2BE688EBE33700EC13F272423B1743E",
            Convert.ToHexString(digest.GetHashAndReset()));
    }
}
