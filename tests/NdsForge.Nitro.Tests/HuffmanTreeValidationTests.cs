using NdsForge.Nitro.Compression;

namespace NdsForge.Nitro.Tests;

public sealed class HuffmanTreeValidationTests
{
    [Fact]
    public void RejectsOutOfRangeBranchEvenWhenTheBitstreamNeverSelectsIt()
    {
        byte[] encoded = [0x28, 1, 0, 0, 1, 0x80, 0x41, 0, 0, 0, 0, 0];

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => HuffmanCodec.Decompress(encoded));

        Assert.Contains("child offset", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsOversizedUnselectedFourBitLeaf()
    {
        byte[] encoded = [0x24, 1, 0, 0, 1, 0xC0, 1, 0x20, 0, 0, 0, 0];

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => HuffmanCodec.Decompress(encoded));

        Assert.Contains("nibble", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsSharedForwardSubtreesWithoutExpandingThemRecursively()
    {
        byte[] encoded = [0x28, 4, 0, 0, 3, 0, 0, 0, 0xC0, 0xC0, 0x41, 0x42, 0, 0, 0x70, 0x07];

        byte[] decoded = HuffmanCodec.Decompress(encoded);

        Assert.Equal("ABAB"u8.ToArray(), decoded);
    }
}
