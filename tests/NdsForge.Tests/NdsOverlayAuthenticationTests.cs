namespace NdsForge.Tests;

public sealed class NdsOverlayAuthenticationTests
{
    [Fact]
    public void ParsesAndVerifiesACompletePlainArm9Table()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateWithOverlayAuthentication());

        NdsOverlayAuthenticationTable table = Assert.IsType<NdsOverlayAuthenticationTable>(
            image.Arm9OverlayAuthentication);
        NdsOverlay overlay = Assert.Single(image.Arm9Overlays);
        NdsOverlayAuthenticationRecord record = Assert.IsType<NdsOverlayAuthenticationRecord>(
            overlay.AuthenticationRecord);

        Assert.Equal(NdsOverlayAuthenticationTableState.Complete, table.State);
        Assert.Equal(NdsProgramStorageEncoding.Plain, table.ProgramStorage);
        Assert.Equal(0x100u, table.RelativeOffset);
        Assert.Equal(0x400, table.DecodedProgramLength);
        Assert.Equal(0x400, table.UncompressedPrefixLength);
        Assert.Equal(new NdsRegion(0x100, 20), table.DecodedProgramRegion);
        Assert.Same(record, Assert.Single(table.Records));
        Assert.Equal(0, record.OverlayIndex);
        Assert.Equal(7u, record.OverlayId);
        Assert.Equal(20, record.HmacSha1.Length);
        Assert.True(image.Validate().IsValid);
    }

    [Fact]
    public void ExplicitKeyDistinguishesAStaleRecordFromMissingKeyPlacement()
    {
        byte[] bytes = SyntheticImage.CreateWithOverlayAuthentication();
        bytes[0x1680] ^= 0x80;
        using NdsImage image = NdsImage.Load(bytes);

        NdsValidationResult inferred = image.Validate();
        NdsDiagnostic unavailable = Assert.Single(inferred.Diagnostics, static value => value.Code == "NDS1213");
        Assert.Equal(NdsDiagnosticSeverity.Warning, unavailable.Severity);

        NdsValidationResult explicitKey = image.Validate(
            new NdsValidationOptions().SetArm9OverlayHmacKey(SyntheticImage.CreateOverlayAuthenticationKey()));
        NdsDiagnostic stale = Assert.Single(explicitKey.Diagnostics, static value => value.Code == "NDS1215");
        Assert.Equal(NdsDiagnosticSeverity.Error, stale.Severity);
        Assert.False(explicitKey.IsValid);
    }

    [Fact]
    public void ReportsMissingAndOutOfRangeTableDeclarations()
    {
        using NdsImage missing = NdsImage.Load(SyntheticImage.CreateWithOverlayAuthentication(tableOffset: 0));
        using NdsImage outside = NdsImage.Load(SyntheticImage.CreateWithOverlayAuthentication(tableOffset: 0x3F8));

        Assert.Equal(
            NdsOverlayAuthenticationTableState.MissingTablePointer,
            Assert.IsType<NdsOverlayAuthenticationTable>(missing.Arm9OverlayAuthentication).State);
        Assert.Contains(missing.Validate().Diagnostics, static value => value.Code == "NDS1211");
        Assert.Equal(
            NdsOverlayAuthenticationTableState.TableOutOfRange,
            Assert.IsType<NdsOverlayAuthenticationTable>(outside.Arm9OverlayAuthentication).State);
        Assert.Contains(outside.Validate().Diagnostics, static value => value.Code == "NDS1212");
    }

    [Fact]
    public void EnforcesTheDecodedProgramResourceLimitBeforeRetention()
    {
        byte[] bytes = SyntheticImage.CreateWithOverlayAuthentication();
        var options = new NdsReadOptions { MaximumDecodedProgramBytes = 0x3FF };

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => NdsImage.Load(bytes, options));

        Assert.Contains("decoded-program limit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationCopiesAnExplicitKey()
    {
        byte[] key = SyntheticImage.CreateOverlayAuthenticationKey();
        NdsValidationOptions options = new NdsValidationOptions().SetArm9OverlayHmacKey(key);
        key.AsSpan().Clear();
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateWithOverlayAuthentication());

        Assert.True(image.Validate(options).IsValid);
    }

    [Fact]
    public async Task ImportedBuilderRepairsAChangedNamedOverlayAtomically()
    {
        byte[] oldDigest;
        NdsImageBuilder builder;
        using (NdsImage source = NdsImage.Load(SyntheticImage.CreateWithOverlayAuthentication()))
        {
            oldDigest = source.Arm9Overlays[0].AuthenticationRecord!.HmacSha1.ToArray();
            builder = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken);
        }

        builder.ReplaceOverlay(NdsProcessor.Arm9, 7, "changed"u8, NdsOverlayCompressionMode.Uncompressed);
        byte[] rebuilt = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage output = NdsImage.Load(rebuilt);

        NdsOverlay overlay = Assert.Single(output.Arm9Overlays);
        Assert.Equal("changed"u8.ToArray(), await output.FileSystem.GetFile(checked((int)overlay.FileId))
            .ReadAllBytesAsync(TestContext.Current.CancellationToken));
        Assert.NotEqual(oldDigest, overlay.AuthenticationRecord!.HmacSha1.ToArray());
        Assert.False(overlay.IsCompressed);
        Assert.Equal((uint)"changed"u8.Length, overlay.RamSize);
        Assert.True(output.Validate().IsValid);
    }

    [Fact]
    public async Task OverlayRecompressionUpdatesStoredSizeAndAuthenticatesEncodedBytes()
    {
        NdsImageBuilder builder;
        using (NdsImage source = NdsImage.Load(SyntheticImage.CreateWithOverlayAuthentication()))
        {
            builder = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken);
        }

        byte[] decoded = Enumerable.Repeat((byte)0x5A, 512).ToArray();
        builder.ReplaceOverlay(NdsProcessor.Arm9, 7, decoded, NdsOverlayCompressionMode.Blz);
        byte[] rebuilt = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage output = NdsImage.Load(rebuilt);

        NdsOverlay overlay = Assert.Single(output.Arm9Overlays);
        byte[] stored = await output.FileSystem.GetFile(checked((int)overlay.FileId))
            .ReadAllBytesAsync(TestContext.Current.CancellationToken);
        Assert.True(overlay.IsCompressed);
        Assert.Equal((uint)stored.Length, overlay.CompressedSize);
        Assert.Equal((uint)decoded.Length, overlay.RamSize);
        Assert.Equal(decoded, NdsForge.Shared.BlzEngine.Decompress(stored, decoded.Length));
        Assert.True(output.Validate().IsValid);
    }

    [Fact]
    public async Task ChangedTableInsideBlzArm9IsReencodedAndRepairsCompressedEnd()
    {
        NdsImageBuilder builder;
        using (NdsImage source = NdsImage.Load(SyntheticImage.CreateWithCompressedArm9OverlayAuthentication()))
        {
            Assert.Equal(NdsProgramStorageEncoding.Blz, source.Arm9OverlayAuthentication!.ProgramStorage);
            builder = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken);
        }

        builder.ReplaceOverlay(NdsProcessor.Arm9, 7, "different"u8, NdsOverlayCompressionMode.Uncompressed);
        byte[] rebuilt = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage output = NdsImage.Load(rebuilt);

        Assert.Equal(NdsProgramStorageEncoding.Blz, output.Arm9OverlayAuthentication!.ProgramStorage);
        Assert.Equal(
            output.Header.Arm9.LoadAddress + output.Header.Arm9.Data.Length,
            output.Header.Arm9.Parameters!.CompressedEndAddress);
        Assert.Equal("different"u8.ToArray(), await output.FileSystem.GetFile(0)
            .ReadAllBytesAsync(TestContext.Current.CancellationToken));
        Assert.True(output.Validate().IsValid);
    }

    [Fact]
    public async Task AuthenticatedBuildsRejectMissingOrUnrepairablePolicyBeforeWriting()
    {
        NdsImageBuilder missing;
        using (NdsImage source = NdsImage.Load(SyntheticImage.CreateWithOverlayAuthentication()))
        {
            missing = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken);
        }

        missing.Arm9OverlayAuthentication = null;
        using var destination = new MemoryStream([9, 8, 7], writable: true);
        InvalidDataException absent = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await missing.WriteAsync(destination, cancellationToken: TestContext.Current.CancellationToken)
                .ConfigureAwait(true));
        Assert.Contains("require explicit", absent.Message, StringComparison.Ordinal);
        Assert.Equal([9, 8, 7], destination.ToArray());

        byte[] staleBytes = SyntheticImage.CreateWithOverlayAuthentication();
        staleBytes[0x1680] ^= 0x80;
        NdsImageBuilder stale;
        using (NdsImage source = NdsImage.Load(staleBytes))
        {
            stale = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken);
        }

        Assert.False(stale.Arm9OverlayAuthentication!.CanRegenerate);
        InvalidDataException unrepairable = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await stale.BuildAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Contains("cannot be regenerated", unrepairable.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PreservationEditorRejectsAChangeThatRequiresStructuralAuthenticationRepair()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateWithOverlayAuthentication());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            image.Edit().ReplaceAllocation(0, "changed"u8));

        Assert.Contains("ReplaceOverlay", error.Message, StringComparison.Ordinal);
    }
}
