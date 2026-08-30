using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Verifies raw partition declarations and their independent cartridge-capacity constraints.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusNandTests
{
    private const string Identity = "0BB4EAC0D9227DB2739A4534ABEA71DC443F0E56E8A65AB861D2A3A9E6EE0BDC";

    [Fact]
    public async Task CompleteCartridgeCorpusHasOneDeclaredNandLayout()
    {
        int declared = 0;
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(image.Header.RawData.Span[0x94..]), image.Header.NandRomEndUnits);
            Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(image.Header.RawData.Span[0x96..]), image.Header.NandWritableStartUnits);
            if (image.Header.NandRomEndUnits == 0 && image.Header.NandWritableStartUnits == 0) { continue; }
            declared++;
            Assert.Equal(Identity, entry.RomSha256.ToUpperInvariant());
            AssertBoundaries(image);
            Assert.Equal("0B2B49BA09DF25B3176FC19CE24F7977F112532CE230560CB7A8B2F9E73A4455",
                Convert.ToHexString(SHA256.HashData(image.Header.RawData.Span.Slice(0x94, 0x2C))));
        }
        Assert.Equal(1, declared);
    }

    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task DeclaredPartitionsSurviveCompactPaddedAndSourcePreservingWrites()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CorpusExpectationIndexEntry entry = CorpusExpectations.Entries.Single(item => item.RomSha256.Equals(Identity, StringComparison.OrdinalIgnoreCase));
        using NdsImage source = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry), cancellationToken: token).ConfigureAwait(true);
        Assert.Equal(134217728, source.Length);
        Assert.Equal(17400892U, source.Header.UsedImageSize);
        Assert.Equal(10570, source.Header.HeaderCrc);
        NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(source, token).ConfigureAwait(true);
        if (builder.DsMetadata is not null) { builder.DsMetadata.Integrity = NdsDsIntegrityOptions.Unauthenticated; }
        byte[] compact = await builder.BuildAsync(cancellationToken: token).ConfigureAwait(true);
        using (NdsImage output = NdsImage.Load(compact))
        {
            AssertBoundaries(output);
            Assert.Equal(16554360, compact.Length);
            Assert.True(output.Validate().IsValid);
            NdsImageManifest manifest = await output.CreateManifestAsync(token).ConfigureAwait(true);
            Assert.Equal((ushort)848, manifest.Header.NandRomEndUnits);
            Assert.Equal((ushort)848, manifest.Header.NandWritableStartUnits);
        }
        byte[] padded = await builder.BuildAsync(new() { PadToDeviceCapacity = true }, token).ConfigureAwait(true);
        Assert.Equal(134217728, padded.Length);
        Assert.True(compact.AsSpan().SequenceEqual(padded.AsSpan(0, compact.Length)));
        Assert.True(padded.AsSpan(compact.Length).IndexOfAnyExcept((byte)255) < 0);
        using var rejected = new MemoryStream();
        rejected.Write([1, 2, 3]);
        await Assert.ThrowsAsync<ArgumentException>(async () => await builder.WriteAsync(rejected,
            new() { RequestedDeviceCapacityBytes = 33554432, VerifyOutput = false }, token).ConfigureAwait(true));
        Assert.Equal([1, 2, 3], rejected.ToArray());

        using var trimmed = new MemoryStream();
        await NdsImageResizer.WriteAsync(source, trimmed, new() { Mode = NdsImageResizeMode.Trim }, token).ConfigureAwait(true);
        Assert.Equal(17400892, trimmed.Length);
        Assert.Equal("DADA82C767761183B12F658EDEB8BABAEA0211DA6E7C8FE36DCFA11423FF2B6E",
            Convert.ToHexString(SHA256.HashData(trimmed.ToArray())));
        trimmed.Position = 0;
        using NdsImage small = await NdsImage.OpenAsync(trimmed, leaveOpen: true, cancellationToken: token).ConfigureAwait(true);
        AssertBoundaries(small);
        using var expanded = new MemoryStream();
        await NdsImageResizer.WriteAsync(small, expanded, new() { Mode = NdsImageResizeMode.PadToDeviceCapacity }, token).ConfigureAwait(true);
        expanded.Position = 0;
        Assert.Equal(Identity, Convert.ToHexString(await SHA256.HashDataAsync(expanded, token).ConfigureAwait(true)));
        await PrivateCorpusWorkspaceTests.VerifyWorkspaceAsync(CorpusExpectations.Resolve(entry), Identity).ConfigureAwait(true);
    }

    private static void AssertBoundaries(NdsImage image)
    {
        Assert.Equal((ushort)848, image.Header.NandRomEndUnits);
        Assert.Equal((ushort)848, image.Header.NandWritableStartUnits);
        Assert.Equal(111149056, image.Header.NandRomEndOffset);
        Assert.Equal(111149056, image.Header.NandWritableStartOffset);
        Assert.Equal(134217728, image.Header.DeviceCapacityBytes);
        Assert.DoesNotContain(image.Validate().Diagnostics, item => item.Code.StartsWith("NDS159", StringComparison.Ordinal));
    }
}
