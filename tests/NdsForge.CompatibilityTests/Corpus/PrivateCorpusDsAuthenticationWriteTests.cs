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
                    Assert.All(result.Diagnostics, static item => Assert.Equal("NDS1550", item.Code));
                    using NdsImage output = await NdsImage.OpenAsync(destination, cancellationToken: token).ConfigureAwait(true);
                    NdsValidationResult checkedOutput = output.Validate(validation);
                    Assert.True(checkedOutput.IsValid, string.Join("; ", checkedOutput.Diagnostics.Select(static item => item.Message)));
                    Assert.DoesNotContain(checkedOutput.Diagnostics, static item => item.Code.StartsWith("NDS15", StringComparison.Ordinal));
                    NdsDsExtendedHeader extension = output.Header.DsExtended!;
                    Assert.Equal(source.Header.NandRomEndUnits, output.Header.NandRomEndUnits);
                    Assert.Equal(source.Header.NandWritableStartUnits, output.Header.NandWritableStartUnits);
                    if (source.Header.NandRomEndUnits != 0)
                    {
                        Assert.Equal("0bb4eac0d9227db2739a4534abea71dc443f0e56e8a65ab861d2a3a9e6ee0bdc", entry.RomSha256);
                        Assert.Equal((ushort)848, output.Header.NandRomEndUnits);
                        Assert.Equal((ushort)848, output.Header.NandWritableStartUnits);
                        Assert.Equal(134217728, output.Header.DeviceCapacityBytes);
                        string expected = operation == 0
                            ? "F013774334F6066DBD2152EF0FC89A73CCF7672F"
                            : "3901C17E831C306B2EA5133C11C8A97A602A15C0";
                        Assert.Equal(expected, Convert.ToHexString(extension.ProgramsHmac.Span));
                    }
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
        CorpusExpectations.AssertDigest("D66D887D3BF87FF9BC0DEC8B144ED85F3460DCA503802038B28293C7DB7FD0EA",
            Convert.ToHexString(digest.GetHashAndReset()));
    }
}
