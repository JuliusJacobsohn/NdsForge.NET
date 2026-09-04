namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Reconstructs cartridge and digital images from independently exported workspace inputs.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusWorkspaceImportTests
{
    [Theory]
    [MemberData(nameof(CorpusExpectations.Cases), MemberType = typeof(CorpusExpectations))]
    [Trait("CorpusTier", "Full")]
    public async Task ImportedWorkspaceReconstructsAllSupportedSourceSemantics(CorpusExpectationIndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await VerifyAsync(CorpusExpectations.Resolve(entry), entry.RomSha256).ConfigureAwait(true);
    }

    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task DigitalWorkspaceImportsRetainTheirCarrierAndPayloads()
    {
        foreach ((string identity, string path) in PrivateDigitalCarrierTests.FindFixtures())
        {
            await VerifyAsync(path, identity).ConfigureAwait(true);
        }
    }

    private static async Task VerifyAsync(string path, string identity)
    {
        string root = Path.Combine(Path.GetTempPath(), "NdsForgeWorkspaceImportTests", Guid.NewGuid().ToString("N"));
        string workspace = Path.Combine(root, "workspace");
        string outputPath = Path.Combine(root, "built.nds");
        CancellationToken token = TestContext.Current.CancellationToken;
        try
        {
            using NdsImage source = await NdsImage.OpenAsync(path, cancellationToken: token).ConfigureAwait(true);
            NdsWorkspaceRecipe recipe = await NdsImageWorkspace.ExportAsync(source, workspace, token).ConfigureAwait(true);
            Assert.Equal(identity, recipe.SourceInventory.ImageSha256, ignoreCase: true);
            NdsImageBuilder builder = await NdsImageWorkspace.ImportAsync(workspace, cancellationToken: token).ConfigureAwait(true);
            if (builder.DsMetadata is not null) { builder.DsMetadata.Integrity = NdsDsIntegrityOptions.PreserveStored; }
            if (builder.DsiMetadata is not null) { builder.DsiMetadata.Integrity = NdsDsiIntegrityOptions.Unauthenticated; }
            if (source.Arm9OverlayAuthentication is { State: NdsOverlayAuthenticationTableState.MissingTablePointer })
            {
                InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await builder.WriteAsync(outputPath, cancellationToken: token).ConfigureAwait(true)).ConfigureAwait(true);
                Assert.Contains("authentication", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.False(File.Exists(outputPath));
                return;
            }
            NdsValidationResult originalValidation = source.Validate();
            await builder.WriteAsync(outputPath, new() { VerifyOutput = originalValidation.IsValid }, token).ConfigureAwait(true);
            using NdsImage output = await NdsImage.OpenAsync(outputPath, cancellationToken: token).ConfigureAwait(true);
            PrivateCorpusTransformTests.AssertIntroducesNoValidationErrors(originalValidation, output.Validate());
            PrivateCorpusTransformTests.AssertSemanticManifestEquality(recipe.SourceInventory,
                await output.CreateManifestAsync(token).ConfigureAwait(true), NdsImageBuildProfile.Deterministic);
            Assert.Equal(source.Header.NandRomEndUnits, output.Header.NandRomEndUnits);
            Assert.Equal(source.Header.NandWritableStartUnits, output.Header.NandWritableStartUnits);
            Assert.Equal(source.CarrierLayout.Kind, output.CarrierLayout.Kind);
            Assert.Equal(source.CarrierLayout.PostHeaderData.ToArray(), output.CarrierLayout.PostHeaderData.ToArray());
            Assert.Equal(source.DownloadPlaySignature?.RawData.ToArray(), output.DownloadPlaySignature?.RawData.ToArray());
            if (source.CarrierLayout is NdsCartridgeLayout cartridge)
            {
                Assert.Equal(cartridge.TwlReservedData.ToArray(), Assert.IsType<NdsCartridgeLayout>(output.CarrierLayout).TwlReservedData.ToArray());
            }
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    }
}
