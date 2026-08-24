using NdsForge.Graphics.Colors;

namespace NdsForge.Graphics.Tests;

public sealed class NitroColor555Tests
{
    [Fact]
    public void ExpandsPrimaryChannelsAndPreservesStoredHighBit()
    {
        Assert.Equal(new RgbaColor32(255, 0, 0), new NitroColor555(0x001F).ToRgba32());
        Assert.Equal(new RgbaColor32(0, 255, 0), new NitroColor555(0x03E0).ToRgba32());
        Assert.Equal(new RgbaColor32(0, 0, 255), new NitroColor555(0x7C00).ToRgba32());
        Assert.Equal(new RgbaColor32(255, 197, 115), new NitroColor555(0x3B1F).ToRgba32());
        Assert.Equal(new RgbaColor32(255, 247, 239), new NitroColor555(0x77DF).ToRgba32());
        Assert.Equal((ushort)0xFFFF, NitroColor555.FromRgba32(new(255, 255, 255), highBit: true).PackedValue);
    }

    [Fact]
    public void EveryFiveBitColorRoundTripsThroughEightBitExpansion()
    {
        for (int packed = 0; packed <= 0x7FFF; packed++)
        {
            var source = new NitroColor555((ushort)packed);
            NitroColor555 rebuilt = NitroColor555.FromRgba32(source.ToRgba32());
            Assert.Equal(source, rebuilt);
        }
    }
}
