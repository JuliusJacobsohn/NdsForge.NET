namespace NdsForge.Tests;

public sealed class NdsChecksumsTests
{
    [Fact]
    public void ComputeCrc16MatchesPublishedCheckValue()
    {
        byte[] data = "123456789"u8.ToArray();

        ushort crc = NdsChecksums.ComputeCrc16(data);

        Assert.Equal(0x4B37, crc);
    }
}

