using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsDsAuthenticationTests
{
    [Theory]
    [InlineData(1, "217DA67BE10C8601046ECD67E1228F22089334A4")]
    [InlineData(512, "619FB43311487AFA45E719BD1CDD2FC80A8EECCC")]
    [InlineData(513, "9A8C7293785A9DCA690D9EAC930ED0C14A82D52A")]
    [InlineData(600000, "0BC916D8A017D1210A6102D88D690F4C1D1268C1")]
    public void MatchesSectorBoundariesAndSharedBudget(int length, string expected)
    {
        using NdsImage image = NdsImage.Load(CreateFixture([length]));
        Assert.Equal(expected, Compute(image));
        Assert.Equal(3, NdsDsAuthentication.GetOverlayHashRegions(image).Count);
    }

    [Theory]
    [InlineData(false, "102D52ECFD339DE2EADD5385FEE3D4ED3E1B19FE")]
    [InlineData(true, "B331E4D2B23041D665587103BCE16EA74C70AFFA")]
    public void PayloadCoverageFollowsFatOrderRatherThanOverlayRecordOrder(bool reverseIds, string expected)
    {
        using NdsImage image = NdsImage.Load(CreateFixture([512, 513], reverseIds ? [1, 0] : [0, 1]));
        Assert.Equal(expected, Compute(image));
        Assert.Equal(
            [new NdsRegion(0x4000, 64), new(0x5000, 16), new(0x8000, 512), new(0x8200, 1024)],
            NdsDsAuthentication.GetOverlayHashRegions(image));
    }

    [Fact]
    public void RedistributesUnusedSectorBudgetToRemainingFiles()
    {
        using NdsImage image = NdsImage.Load(CreateFixture([1, 400000, 400000]));
        Assert.Equal("9550D15258ACEDB6F006C03C1CB4742226503F4C", Compute(image));
        Assert.Equal([512L, 511L * 512, 512L * 512],
            NdsDsAuthentication.GetOverlayHashRegions(image).Skip(2).Select(static region => region.Length));
    }

    [Fact]
    public void IncludesRequiredPaddingButExcludesUnrelatedAllocationRecords()
    {
        byte[] data = CreateFixture([1]);
        data[0x8001] ^= 0x55;
        using (NdsImage paddingChanged = NdsImage.Load(data))
        {
            Assert.Equal("6B69930A4B7576FF2972ECD4BE298DE35E280FB6", Compute(paddingChanged));
        }

        data[0x8001] ^= 0x55;
        Write(data, 0x5008, 85);
        Write(data, 0x500C, 85);
        using NdsImage unrelatedChanged = NdsImage.Load(data);
        Assert.Equal("217DA67BE10C8601046ECD67E1228F22089334A4", Compute(unrelatedChanged));
    }

    [Fact]
    public void ExcludesPayloadBytesBeyondTheTotalBudget()
    {
        byte[] data = CreateFixture([600000]);
        data[0x8000 + 512 * 1024] ^= 0x55;
        using NdsImage image = NdsImage.Load(data);
        Assert.Equal("0BC916D8A017D1210A6102D88D690F4C1D1268C1", Compute(image));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    public void RejectsDuplicateOrNonPrefixAllocationSelections(int first, int second)
    {
        using NdsImage image = NdsImage.Load(CreateFixture([1, 1], [first, second]));
        Assert.Throws<InvalidDataException>(() => NdsDsAuthentication.GetOverlayHashRegions(image));
        Assert.Throws<InvalidDataException>(() => NdsDsAuthentication.ComputeOverlayHmac(image, [1]));
    }

    [Fact]
    public void RejectsMissingPhysicalPaddingBeforeReadingAnyDigestInput()
    {
        using NdsImage image = NdsImage.Load(CreateFixture([1]).AsMemory(0, 0x8001));
        Assert.Throws<InvalidDataException>(() => NdsDsAuthentication.GetOverlayHashRegions(image));
    }

    [Fact]
    public void EmptyInputIsNotConfusedWithAnAbsentStoredField()
    {
        using NdsImage image = NdsImage.Load(CreateFixture([]));
        Assert.Empty(NdsDsAuthentication.GetOverlayHashRegions(image));
        Assert.Equal("60BF8C95C85CFA61279A2B9B079AA19D7FA5F31A", Compute(image));
        Assert.Equal(new byte[20], image.Header.DsExtended!.Arm9OverlaysHmac.ToArray());
        Assert.Throws<ArgumentException>(() => NdsDsAuthentication.ComputeOverlayHmac(image, []));
        Assert.Throws<ArgumentNullException>(() => NdsDsAuthentication.GetOverlayHashRegions(null!));
    }

    private static string Compute(NdsImage image) => Convert.ToHexString(NdsDsAuthentication.ComputeOverlayHmac(
        image, Enumerable.Range(0, 64).Select(static index => (byte)index).ToArray()));

    private static byte[] CreateFixture(int[] lengths, int[]? ids = null)
    {
        int length = 0x8200 + lengths.Sum(static value => (value + 511) / 512 * 512);
        byte[] data = new byte[length];
        for (int index = 0x8000; index < data.Length; index++)
        {
            data[index] = unchecked((byte)((index * 73) ^ (index >> 9) ^ 0x39));
        }

        Write(data, 0x40, 0x6000);
        Write(data, 0x44, 9);
        Write(data, 0x6000, 8);
        data[0x6006] = 1;
        Write(data, 0x48, 0x5000);
        Write(data, 0x4C, (lengths.Length + 1) * 8);
        Write(data, 0x50, 0x4000);
        Write(data, 0x54, lengths.Length * 32);
        data[0x1BF] = 0x40;
        int offset = 0x8000;
        for (int index = 0; index < lengths.Length; index++)
        {
            Write(data, 0x4000 + index * 32, index);
            Write(data, 0x4004 + index * 32, 0x02000000 + index * 0x100000);
            Write(data, 0x4008 + index * 32, lengths[index]);
            Write(data, 0x4018 + index * 32, ids?[index] ?? index);
            Write(data, 0x5000 + index * 8, offset);
            Write(data, 0x5004 + index * 8, offset + lengths[index]);
            offset += (lengths[index] + 511) / 512 * 512;
        }

        return data;
    }

    private static void Write(Span<byte> data, int offset, int value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], checked((uint)value));
}
