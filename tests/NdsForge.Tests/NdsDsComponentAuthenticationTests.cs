using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsDsComponentAuthenticationTests
{
    [Theory]
    [InlineData(-1, -1, "08FBC08DAFC493C52654441DC463FE2030C741CD")]
    [InlineData(0x15F, -1, "CE0345035F41B20ED56742D1F599D8F26D62B051")]
    [InlineData(0x160, -1, "08FBC08DAFC493C52654441DC463FE2030C741CD")]
    [InlineData(0x4000, -1, "08FBC08DAFC493C52654441DC463FE2030C741CD")]
    [InlineData(0xA000, -1, "B05132DE78A7508FEBCD4E3F358C07D759065804")]
    [InlineData(0xA2FF, -1, "1BC778D6C7CD7475622A10ABE289BBB13DA01FC2")]
    [InlineData(0xA300, -1, "08FBC08DAFC493C52654441DC463FE2030C741CD")]
    [InlineData(-1, 0, "FA02329A050FD38CAE6D2594799EDCA8E308D6BC")]
    [InlineData(-1, 0x4000, "FBE976D8FD8D49730B7B8BED74878E5EDED6BF61")]
    public void ProgramDigestUsesExactHeaderAndExplicitProgramRepresentations(int imageOffset, int arm9Offset, string expected)
    {
        byte[] image = Pattern(0xB000, 19);
        byte[] arm9 = Pattern(0x4800, 47);
        if (imageOffset >= 0)
        {
            image[imageOffset] ^= 0x55;
        }

        if (arm9Offset >= 0)
        {
            arm9[arm9Offset] ^= 0x55;
        }

        byte[] actual = NdsDsAuthentication.ComputeProgramsHmac(
            image.AsSpan(0, 0x160), arm9, image.AsSpan(0xA000, 0x300), ProgramKey());
        Assert.Equal(expected, Convert.ToHexString(actual));
    }

    [Theory]
    [InlineData(1, 0x840, "A252229537C48AD7044B88121F688592284C6E9B", "EE842DDE61C6FF3F46FDABD202BEC097172585EA")]
    [InlineData(2, 0x940, "FBC215B4F7B3A36D99EEEDB8898824BBAB7411BA", "148427C691FE2053487F0313C21CE61357520E25")]
    [InlineData(3, 0xA40, "B555810C75B0E3618AD0C86E117EDDF4D0D507A8", "4E8D826493C509792A0BAFD10FF13EFA97262DB3")]
    [InlineData(0x103, 0x23C0, "ADCFF261F98B7DDD7492FDF0476378AC689EFF36", "54FE0F33BB13CEB8A7BD76F98C82983EDC9E8ABD")]
    public void BannerDigestCoversEveryVersionDefinedByteButNotExternalPadding(
        ushort version, int size, string expected, string changedLastByte)
    {
        byte[] data = Pattern(0x4000 + size + 512, 13);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x4000), version);
        byte[] key = Enumerable.Range(0, 64).Select(static index => (byte)(255 - index)).ToArray();
        NdsBanner banner = NdsBanner.Parse(data.AsMemory(0x4000, size));
        Assert.Equal(expected, Convert.ToHexString(NdsDsAuthentication.ComputeBannerHmac(banner, key)));
        data[0x4000 + size] ^= 0x55;
        banner = NdsBanner.Parse(data.AsMemory(0x4000, size));
        Assert.Equal(expected, Convert.ToHexString(NdsDsAuthentication.ComputeBannerHmac(banner, key)));
        data[0x4000 + size - 1] ^= 0x55;
        banner = NdsBanner.Parse(data.AsMemory(0x4000, size));
        Assert.Equal(changedLastByte, Convert.ToHexString(NdsDsAuthentication.ComputeBannerHmac(banner, key)));
    }

    [Fact]
    public void RejectsAbsentCredentialsAndIncorrectHeaderWidths()
    {
        Assert.Throws<ArgumentException>(() => NdsDsAuthentication.ComputeProgramsHmac(new byte[0x15F], [1], [2], ProgramKey()));
        Assert.Throws<ArgumentException>(() => NdsDsAuthentication.ComputeProgramsHmac(new byte[0x161], [1], [2], ProgramKey()));
        Assert.Throws<ArgumentException>(() => NdsDsAuthentication.ComputeProgramsHmac(new byte[0x160], [1], [2], []));
        Assert.Throws<ArgumentNullException>(() => NdsDsAuthentication.ComputeBannerHmac(null!, ProgramKey()));
        byte[] rawBanner = new byte[0x840];
        rawBanner[0] = 1;
        NdsBanner banner = NdsBanner.Parse(rawBanner);
        Assert.Throws<ArgumentException>(() => NdsDsAuthentication.ComputeBannerHmac(banner, []));
    }

    private static byte[] ProgramKey() => Enumerable.Range(0, 64).Select(static index => (byte)index).ToArray();

    private static byte[] Pattern(int length, int seed) => Enumerable.Range(0, length)
        .Select(index => unchecked((byte)((index * seed) ^ (index >> 7) ^ seed))).ToArray();
}
