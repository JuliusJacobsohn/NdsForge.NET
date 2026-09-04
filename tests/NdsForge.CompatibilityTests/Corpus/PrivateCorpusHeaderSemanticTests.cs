using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Checks typed header projections against direct native-field decoding across the complete private corpus.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusHeaderSemanticTests
{
    /// <summary>Freezes aggregate semantics without embedding image bytes or external provenance in the repository.</summary>
    [Fact]
    public async Task HeaderSemanticProjectionsMatchNativeFieldsAcrossCorpus()
    {
        using IncrementalHash nativeDigest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using IncrementalHash modelDigest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int dsiImages = 0;
        int debugPrograms = 0;
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            string path = CorpusExpectations.Resolve(entry);
            byte[] raw = new byte[0x1000];
            await using (FileStream stream = File.OpenRead(path))
            {
                await stream.ReadExactlyAsync(raw, TestContext.Current.CancellationToken).ConfigureAwait(true);
            }

            using NdsImage image = await NdsImage.OpenAsync(
                path,
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            NdsHeader header = image.Header;
            Assert.Equal(raw[0x1C], header.DsiFlags);
            Assert.Equal(raw[0x1C] & 0x0F, (int)header.DsiCryptoPolicy & 0x0F);
            Assert.Equal(raw[0x1C] & 0xF0, header.UnknownDsiFlagBits);
            Assert.Equal(raw[0x1D], header.RegionCode);
            Assert.Equal(raw[0x1D], header.LegacyRegion.RawValue);
            Assert.Equal(raw[0x1D] & 0x03, (int)header.DsiLaunchPolicy & 0x03);
            Assert.Equal(raw[0x1D] & 0xFC, header.UnknownDsiLaunchBits);
            Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x160)), header.DebugRomOffset);
            Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x164)), header.DebugRomSize);
            Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x168)), header.DebugLoadAddress);
            debugPrograms += header.DebugRomSize == 0 ? 0 : 1;

            Append(nativeDigest, raw.AsSpan(0x1C, 2));
            Append(nativeDigest, raw.AsSpan(0x160, 12));
            Append(modelDigest, [header.DsiFlags, header.RegionCode]);
            AppendUInt32(modelDigest, header.DebugRomOffset);
            AppendUInt32(modelDigest, header.DebugRomSize);
            AppendUInt32(modelDigest, header.DebugLoadAddress);

            if (header.Dsi is not NdsDsiHeader dsi)
            {
                continue;
            }

            dsiImages++;
            uint nativeRegions = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x1B0));
            uint nativeAccess = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x1B4));
            Assert.Equal(raw.AsSpan(0x180, 0x30), dsi.MemoryBankSettings.Span);
            Assert.Equal(nativeRegions, dsi.RegionFlags);
            Assert.Equal(nativeRegions & 0x3F, (uint)(int)dsi.Regions & 0x3F);
            Assert.Equal(nativeRegions & ~0x3Fu, dsi.UnknownRegionFlagBits);
            Assert.Equal(nativeAccess, dsi.AccessControl);
            Assert.Equal(nativeAccess & 0x8001FFFFu, (uint)(long)dsi.AccessControlFlags & 0x8001FFFFu);
            Assert.Equal(nativeAccess & ~0x8001FFFFu, dsi.UnknownAccessControlBits);
            Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x1B8)), dsi.ScfgExtMask);
            Assert.Equal(raw[0x1BF], dsi.ApplicationFlags);
            Assert.Equal(raw[0x1BF], (byte)dsi.ApplicationFeatures);
            Assert.Equal([raw[0x20C], raw[0x20D], raw[0x214], raw[0x215], raw[0x216], raw[0x217]],
                dsi.SharedDataFileSizes);
            Assert.Equal(raw[0x20E], dsi.EulaVersion);
            Assert.Equal(raw[0x20F], dsi.AgeRatingsUsage);
            Assert.Equal(raw.AsSpan(0x2F0, 0x10), dsi.AgeRatings.Span);
            Assert.All(dsi.Ratings, rating => Assert.Equal(raw[0x2F0 + (int)rating.Authority], rating.RawValue));

            Append(nativeDigest, raw.AsSpan(0x180, 0x40));
            Append(nativeDigest, raw.AsSpan(0x20C, 4));
            Append(nativeDigest, raw.AsSpan(0x214, 4));
            Append(nativeDigest, raw.AsSpan(0x2F0, 0x10));
            Append(modelDigest, dsi.MemoryBankSettings.Span);
            AppendUInt32(modelDigest, dsi.RegionFlags);
            AppendUInt32(modelDigest, dsi.AccessControl);
            AppendUInt32(modelDigest, dsi.ScfgExtMask);
            Append(modelDigest, [0, 0, 0, dsi.ApplicationFlags]);
            Append(modelDigest, [dsi.SharedDataFileSizes[0], dsi.SharedDataFileSizes[1], dsi.EulaVersion, dsi.AgeRatingsUsage]);
            Append(modelDigest, dsi.SharedDataFileSizes.Skip(2).ToArray());
            Append(modelDigest, dsi.AgeRatings.Span);
        }

        string nativeHash = Convert.ToHexString(nativeDigest.GetHashAndReset());
        Assert.Equal(nativeHash, Convert.ToHexString(modelDigest.GetHashAndReset()));
        Assert.Multiple(
            () => Assert.Equal(9, dsiImages),
            () => Assert.Equal(0, debugPrograms),
            () => CorpusExpectations.AssertDigest("D63387B5F1421FB61CEE2D19F0DFD755810149B672ADD049CD9369879F85E12D", nativeHash));
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> data) => hash.AppendData(data);

    private static void AppendUInt32(IncrementalHash hash, uint value)
    {
        Span<byte> data = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        hash.AppendData(data);
    }
}
