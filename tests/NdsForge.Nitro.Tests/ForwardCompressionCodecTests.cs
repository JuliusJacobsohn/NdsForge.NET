using NdsForge.Nitro.Compression;

namespace NdsForge.Nitro.Tests;

public sealed class ForwardCompressionCodecTests
{
    [Fact]
    public void DecodesKnownLz10TokenLayout()
    {
        byte[] encoded = [0x10, 9, 0, 0, 0x10, (byte)'A', (byte)'B', (byte)'C', 0x30, 0x02];

        byte[] decoded = Lz10Codec.Decompress(encoded);

        Assert.Equal("ABCABCABC"u8.ToArray(), decoded);
    }

    [Fact]
    public void DecodesKnownLz11ShortTokenLayout()
    {
        byte[] encoded = [0x11, 9, 0, 0, 0x10, (byte)'A', (byte)'B', (byte)'C', 0x50, 0x02];

        byte[] decoded = Lz11Codec.Decompress(encoded);

        Assert.Equal("ABCABCABC"u8.ToArray(), decoded);
    }

    [Fact]
    public void DecodesKnownRunLengthBlocks()
    {
        byte[] encoded = [0x30, 9, 0, 0, 0x80, (byte)'A', 0x01, (byte)'B', (byte)'C', 0x81, (byte)'D'];

        byte[] decoded = RleCodec.Decompress(encoded);

        Assert.Equal("AAABCDDDD"u8.ToArray(), decoded);
    }

    [Fact]
    public void DecodesKnownEightBitHuffmanTree()
    {
        byte[] encoded = [0x28, 4, 0, 0, 1, 0xC0, (byte)'A', (byte)'B', 0, 0, 0, 0x50];

        byte[] decoded = HuffmanCodec.Decompress(encoded);

        Assert.Equal("ABAB"u8.ToArray(), decoded);
        Assert.Equal(decoded, NitroCompression.Decompress(encoded));
    }

    [Fact]
    public void DecodesKnownFourBitHuffmanTreeHighNibbleFirst()
    {
        byte[] encoded = [0x24, 2, 0, 0, 1, 0xC0, 1, 2, 0, 0, 0, 0x50];

        byte[] decoded = HuffmanCodec.Decompress(encoded);

        Assert.Equal([0x12, 0x12], decoded);
    }

    [Fact]
    public void RoundTripsDeterministicMixedInputThroughAllForwardEncoders()
    {
        byte[] source = CreateMixedInput(8192);

        byte[] lz10 = Lz10Codec.Compress(source);
        byte[] lz11 = Lz11Codec.Compress(source);
        byte[] rle = RleCodec.Compress(source);

        Assert.Equal(source, Lz10Codec.Decompress(lz10));
        Assert.Equal(source, Lz11Codec.Decompress(lz11));
        Assert.Equal(source, RleCodec.Decompress(rle));
        Assert.Equal(source, NitroCompression.Decompress(lz10));
        Assert.Equal(source, NitroCompression.Decompress(lz11));
        Assert.Equal(source, NitroCompression.Decompress(rle));
    }

    [Fact]
    public void Lz11UsesLongTokensForLargeRuns()
    {
        byte[] source = Enumerable.Repeat((byte)0x5A, 70_000).ToArray();

        byte[] encoded = Lz11Codec.Compress(source);

        Assert.True(encoded.Length < 32);
        Assert.Equal(source, Lz11Codec.Decompress(encoded));
    }

    [Fact]
    public void CommonInspectionRecognizesEveryForwardTypeAndExtendedSize()
    {
        byte[] header = [0x11, 0, 0, 0, 1, 0, 0, 1];

        Assert.True(NitroCompression.TryInspect(header, out NitroCompressionInfo info));
        Assert.Equal(NitroCompressionType.Lz11, info.Type);
        Assert.Equal(0x01000001, info.DecodedLength);
        Assert.Equal(8, info.HeaderLength);
        Assert.False(NitroCompression.TryInspect([0x12, 1, 0, 0], out _));
    }

    [Fact]
    public void RejectsWrongTypesTruncationLookbehindAndOutputBombs()
    {
        Assert.Throws<InvalidDataException>(() => Lz10Codec.Decompress([0x11, 1, 0, 0, 0, 0]));
        Assert.Throws<InvalidDataException>(() => Lz10Codec.Decompress([0x10, 1, 0, 0, 0x80, 0, 0]));
        Assert.Throws<InvalidDataException>(() => Lz11Codec.Decompress([0x11, 1, 0, 0, 0x80, 0]));
        Assert.Throws<InvalidDataException>(() => RleCodec.Decompress([0x30, 3, 0, 0, 0x80]));
        Assert.Throws<InvalidDataException>(() => HuffmanCodec.Decompress([0x28, 1, 0, 0, 1, 0xC0, 0, 1]));
        Assert.Throws<InvalidDataException>(() => HuffmanCodec.Decompress([0x24, 1, 0, 0, 1, 0xC0, 0x10, 1, 0, 0, 0, 0]));
        Assert.Throws<InvalidDataException>(() => NitroCompression.Decompress([0x10, 2, 0, 0, 0, 1], 1));
    }

    [Fact]
    public void EncodersRejectEmptyInputBecauseTheZeroSizeHeaderIsExtended()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Lz10Codec.Compress([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Lz11Codec.Compress([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => RleCodec.Compress([]));
    }

    private static byte[] CreateMixedInput(int length)
    {
        byte[] data = new byte[length];
        for (int index = 0; index < data.Length; index++)
        {
            data[index] = index % 97 < 40
                ? (byte)(index % 7)
                : (byte)((index * 73) ^ (index >> 3));
        }

        return data;
    }
}
