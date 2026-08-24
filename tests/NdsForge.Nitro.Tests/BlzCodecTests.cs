using NdsForge.Nitro.Compression;

namespace NdsForge.Nitro.Tests;

public sealed class BlzCodecTests
{
    [Fact]
    public void CompressesAndDecompressesRepeatedDataWithAPreservedPrefix()
    {
        byte[] source = new byte[4096];
        for (int index = 0; index < 32; index++)
        {
            source[index] = (byte)((index * 73) ^ 0xA5);
        }

        for (int index = 32; index < source.Length; index++)
        {
            source[index] = source[32 + (index % 23)];
        }

        Assert.True(BlzCodec.TryCompress(source, out byte[] encoded, 32));
        Assert.True(BlzCodec.TryInspect(encoded, out BlzInfo info));
        byte[] decoded = BlzCodec.Decompress(encoded);

        Assert.Equal(source, decoded);
        Assert.Equal(32, info.UncompressedPrefixLength);
        Assert.Equal(source.Length, info.DecodedLength);
        Assert.InRange(info.HeaderLength, (byte)8, (byte)11);
    }

    [Fact]
    public void CompressionIsDeterministic()
    {
        byte[] source = Enumerable.Repeat("NDSFORGE"u8.ToArray(), 256).SelectMany(static value => value).ToArray();

        Assert.True(BlzCodec.TryCompress(source, out byte[] first));
        Assert.True(BlzCodec.TryCompress(source, out byte[] second));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ReportsWhenEncodingWouldNotSaveSpace()
    {
        byte[] source = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();

        Assert.False(BlzCodec.TryCompress(source, out byte[] encoded));

        Assert.Empty(encoded);
    }

    [Fact]
    public void RejectsTruncatedAndOutOfRangeStreams()
    {
        Assert.False(BlzCodec.TryInspect([1, 2, 3], out _));
        Assert.Throws<InvalidDataException>(() => BlzCodec.Decompress(new byte[8]));

        byte[] encoded =
        [
            0xFF, 0x0F, 0x80, 0xFF,
            0x0C, 0x00, 0x00, 0x09,
            0x03, 0x00, 0x00, 0x00,
        ];

        Assert.Throws<InvalidDataException>(() => BlzCodec.Decompress(encoded));
    }

    [Fact]
    public void EnforcesTheCallerOutputLimitBeforeAllocating()
    {
        byte[] source = Enumerable.Repeat((byte)0x41, 512).ToArray();
        Assert.True(BlzCodec.TryCompress(source, out byte[] encoded));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => BlzCodec.Decompress(encoded, 511));

        Assert.Contains("configured limit", error.Message, StringComparison.Ordinal);
    }
}
