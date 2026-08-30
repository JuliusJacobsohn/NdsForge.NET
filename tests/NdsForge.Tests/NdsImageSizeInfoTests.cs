using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsImageSizeInfoTests
{
    [Fact]
    public async Task ManifestRetainsAnUnrepresentableCapacityWithoutInventingAByteLength()
    {
        byte[] bytes = SyntheticImage.CreateHeaderOnly();
        bytes[0x14] = 255;
        using NdsImage image = NdsImage.Load(bytes);
        NdsImageManifest manifest = await image.CreateManifestAsync(TestContext.Current.CancellationToken);
        Assert.Equal(255, manifest.Header.DeviceCapacityExponent);
        Assert.Equal(0, manifest.Header.DeviceCapacityBytes);
    }

    [Fact]
    public void SizeDeclarationsRemainDistinctAndTrailingBytesAreNotAssumedDisposable()
    {
        byte[] bytes = SyntheticImage.CreateHeaderOnly();
        bytes[0x3FFF] = 37;
        using NdsImage image = NdsImage.Load(bytes);
        NdsImageSizeInfo sizes = image.SizeInfo;
        Assert.Equal(0x4000, sizes.PhysicalSize);
        Assert.Equal(0x22Du, sizes.CommonUsedSize);
        Assert.Null(sizes.DsiUsedSize);
        Assert.Equal(0, sizes.DeviceCapacityExponent);
        Assert.Equal(0x20000, sizes.DeviceCapacityBytes);
        Assert.Equal(0x22D, sizes.DeclaredContentEnd);
        Assert.Equal(new NdsRegion(0x22D, 0x4000 - 0x22D), sizes.PostUsedData);
        Assert.Equal(sizes.PostUsedData, sizes.TrailingData);
        Assert.Empty(sizes.Diagnostics);
        image.Dispose();
        Assert.Equal(0x4000, sizes.PhysicalSize);
    }

    [Fact]
    public void EveryCapacityByteHasCheckedInterpretationWithoutShiftWrapping()
    {
        byte[] bytes = SyntheticImage.CreateHeaderOnly();
        for (int exponent = 0; exponent <= byte.MaxValue; exponent++)
        {
            bytes[0x14] = (byte)exponent;
            using NdsImage image = NdsImage.Load(bytes);
            Assert.Equal(exponent, image.SizeInfo.DeviceCapacityExponent);
            if (exponent <= 45)
            {
                Assert.Equal(0x20000L << exponent, image.SizeInfo.DeviceCapacityBytes);
                Assert.Equal(image.SizeInfo.DeviceCapacityBytes, image.Header.DeviceCapacityBytes);
            }
            else
            {
                Assert.Null(image.SizeInfo.DeviceCapacityBytes);
                Assert.Throws<InvalidOperationException>(() => image.Header.DeviceCapacityBytes);
                Assert.Contains(image.Validate().Diagnostics, static item => item.Code == "NDS1570");
            }
        }
    }

    [Fact]
    public void CompleteAndTruncatedTrailersRemainSeparateFromPadding()
    {
        byte[] bytes = SyntheticImage.CreateHeaderOnly();
        new byte[] { 0x61, 0x63, 1, 0 }.CopyTo(bytes, 0x22D);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.Equal(0x22D + 136, image.SizeInfo.DeclaredContentEnd);
        Assert.Equal(0x22D, image.SizeInfo.PostUsedData!.Value.Offset);
        Assert.Equal(0x22D + 136, image.SizeInfo.TrailingData!.Value.Offset);
        using NdsImage truncated = NdsImage.Load(bytes.AsMemory(0, 0x231));
        Assert.Contains(truncated.SizeInfo.Diagnostics, static item => item.Code == "NDS1572");
    }

    [Theory]
    [InlineData(0x84, 0x5000)]
    [InlineData(0x80, 0x5000)]
    [InlineData(0x2C, 0x5000)]
    public void MissingDeclaredContentIsReportedWithoutAllocatingItsClaimedSize(int field, uint size)
    {
        byte[] bytes = SyntheticImage.CreateHeaderOnly();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(field), size);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.True(image.SizeInfo.DeclaredContentEnd > image.Length);
        Assert.Null(image.SizeInfo.TrailingData);
        Assert.Contains(image.SizeInfo.Diagnostics, static item => item.Code == "NDS1571");
        if (field == 0x80) { Assert.Null(image.SizeInfo.PostUsedData); }
    }

    [Fact]
    public void AllFfPayloadAndEmptyFatEndpointAreIncludedRegardlessOfUsedField()
    {
        byte[] bytes = SyntheticImage.CreateHeaderOnly();
        bytes.AsSpan(0x228, 5).Fill(0xFF);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80), 0x200);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.Equal(0x22D, image.SizeInfo.DeclaredContentEnd);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x220), 0x3000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x224), 0x3000);
        using NdsImage empty = NdsImage.Load(bytes);
        Assert.Equal(0x3000, empty.SizeInfo.DeclaredContentEnd);
    }

    [Fact]
    public async Task DsiExtentIncludesProgramsAndProtocolWindowEvenWithoutTotalDeclaration()
    {
        var builder = new NdsImageBuilder
        {
            Kind = NdsImageKind.NintendoDsiEnhanced,
            Arm9 = new(NdsProcessor.Arm9, [1], 0x02000000, 0x02000000),
            Arm7 = new(NdsProcessor.Arm7, [2], 0x02380000, 0x02380000),
            Arm9i = new(NdsProcessor.Arm9i, [3], 0x02400000, 0x02400000),
            Arm7i = new(NdsProcessor.Arm7i, [4], 0x02E80000, 0x02E80000),
            DsiMetadata = new(),
        };
        byte[] bytes = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(bytes);
        Assert.Equal(bytes.Length, image.SizeInfo.DeclaredContentEnd);
        Assert.Null(image.SizeInfo.TrailingData);
        Assert.True(image.SizeInfo.PostUsedData!.Value.Length > 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x210), 0);
        using NdsImage unspecified = NdsImage.Load(bytes);
        Assert.Equal(0u, unspecified.SizeInfo.DsiUsedSize);
        Assert.Equal(0x87001, unspecified.SizeInfo.DeclaredContentEnd);
        Assert.Equal(new NdsRegion(0x87001, 511), unspecified.SizeInfo.TrailingData);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x1E8), 0x83000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x1EC), 0x4200);
        using NdsImage covered = NdsImage.Load(bytes);
        Assert.Equal(bytes.Length, covered.SizeInfo.DeclaredContentEnd);
        Assert.Null(covered.SizeInfo.TrailingData);
    }

    [Fact]
    public void LateDsPhysicalAuthenticationCoverageIsNotMistakenForPadding()
    {
        byte[] bytes = SyntheticImage.CreateWithOverlayAuthentication();
        using NdsImage image = NdsImage.Load(bytes);
        long coverageEnd = NdsDsAuthentication.GetOverlayHashRegions(image).Max(static region => region.End);
        Assert.True(coverageEnd > image.Header.UsedImageSize);
        Assert.Equal(coverageEnd, image.SizeInfo.DeclaredContentEnd);
        using NdsImage truncated = NdsImage.Load(bytes.AsMemory(0, checked((int)image.Header.UsedImageSize)));
        Assert.Contains(truncated.SizeInfo.Diagnostics, static item => item.Code == "NDS1573");
        Assert.Null(truncated.SizeInfo.TrailingData);
    }
}
