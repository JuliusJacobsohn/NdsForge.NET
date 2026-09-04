using NdsForge.Nitro.Compression;

namespace NdsForge.Nitro.Tests;

public sealed class LzOutputBoundaryTests
{
    [Fact]
    public void Lz10ClipsFinalBackReferenceToDeclaredOutputLength()
    {
        byte[] encoded = [0x10, 2, 0, 0, 0x40, 0x41, 0, 0];

        Assert.Equal("AA"u8.ToArray(), Lz10Codec.Decompress(encoded));
    }

    [Fact]
    public void Lz11ClipsFinalBackReferenceToDeclaredOutputLength()
    {
        byte[] encoded = [0x11, 2, 0, 0, 0x40, 0x41, 0x20, 0];

        Assert.Equal("AA"u8.ToArray(), Lz11Codec.Decompress(encoded));
    }
}
