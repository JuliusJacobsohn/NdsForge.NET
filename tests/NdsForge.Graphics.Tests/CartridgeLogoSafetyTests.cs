using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Images;

namespace NdsForge.Graphics.Tests;

public sealed class CartridgeLogoSafetyTests
{
    [Fact]
    public void EveryShortFieldAndExcessLengthIsRejected()
    {
        byte[] encoded = CartridgeLogo.FromPixels(new byte[1664]).WritePreserved();
        for (int length = 0; length < 156; length++) { Assert.Throws<FormatException>(() => CartridgeLogo.Parse(encoded.AsSpan(0, length))); }
        Assert.Throws<FormatException>(() => CartridgeLogo.Parse(new byte[157]));
        Assert.Throws<FormatException>(() => CartridgeLogo.Parse(new byte[1024]));
    }

    [Fact]
    public void EveryFilterPrefixMutationAndExhaustedStreamIsRejected()
    {
        byte[] encoded = CartridgeLogo.FromPixels(new byte[1664]).WritePreserved();
        for (int bit = 0; bit < 20; bit++)
        {
            byte[] modified = (byte[])encoded.Clone();
            modified[3 - (bit / 8)] ^= (byte)(0x80 >> (bit % 8));
            Assert.Throws<FormatException>(() => CartridgeLogo.Parse(modified));
        }
        Assert.Throws<FormatException>(() => CartridgeLogo.Parse(new byte[156]));
        byte[] exhausted = new byte[156];
        BinaryPrimitives.WriteUInt32LittleEndian(exhausted, BinaryPrimitives.ReadUInt32LittleEndian(encoded) & 0xFFFFF000);
        Assert.Throws<FormatException>(() => CartridgeLogo.Parse(exhausted));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1663)]
    [InlineData(1665)]
    public void InputPlanesMustHaveExactFixedDimensions(int count)
    {
        Assert.Throws<ArgumentException>(() => CartridgeLogo.FromPixels(new byte[count]));
        Assert.Throws<ArgumentException>(() => CartridgeLogo.MeasureEncodedBitLength(new byte[count]));
        Assert.Throws<ArgumentException>(() => CartridgeLogo.FromRgba32(new RgbaColor32[count], new(0, 0, 0), new(255, 255, 255)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(255)]
    public void InvalidMonochromeValuesAreNotClamped(byte invalid)
    {
        byte[] pixels = new byte[1664];
        pixels[1663] = invalid;
        Assert.Throws<ArgumentException>(() => CartridgeLogo.FromPixels(pixels));
        Assert.Throws<ArgumentException>(() => CartridgeLogo.MeasureEncodedBitLength(pixels));
        Assert.Equal(invalid, pixels[1663]);
    }

    [Fact]
    public void AmbiguousColorsAndUnselectedAlphaAreNotImplicitlyConverted()
    {
        RgbaColor32[] pixels = new RgbaColor32[1664];
        Assert.Throws<ArgumentException>(() => CartridgeLogo.FromRgba32(pixels, default, default));
        RgbaColor32 opaqueBlack = new(0, 0, 0);
        RgbaColor32 opaqueWhite = new(255, 255, 255);
        Assert.Throws<ArgumentException>(() => CartridgeLogo.FromRgba32(pixels, opaqueBlack, opaqueWhite));
        Array.Fill(pixels, opaqueWhite);
        pixels[1663] = new(127, 127, 127);
        Assert.Throws<ArgumentException>(() => CartridgeLogo.FromRgba32(pixels, opaqueBlack, opaqueWhite));
    }

    [Theory]
    [InlineData(1230, "953C5BA137F9AB2863AE38B25507A2582F1B878FEE2DD94F578D964E0967A6EA")]
    [InlineData(1240, "AC29618246146218F9A6032594DABB07D0A485849D056D8D87D34F1CC08C3635")]
    [InlineData(1247, "0C83FB024646BFA703F6BE2526F9719C2509332EE23AF5315AC514B378C0D08A")]
    [InlineData(1248, "00DDBB5DBB2908E37CE76E6EE002E930CE92973F1A663C1DC95D7E6EEDF6545C")]
    public void ExactBoundaryEncodingsMatchCompleteIdentities(int bits, string expected)
    {
        byte[] pixels = CapacityPlane(bits);
        Assert.Equal(bits, CartridgeLogo.MeasureEncodedBitLength(pixels));
        CartridgeLogo logo = CartridgeLogo.FromPixels(pixels);
        Assert.Equal(bits, logo.EncodedBitLength);
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(logo.RawData.Span)));
        Assert.Equal(pixels, CartridgeLogo.Parse(logo.RawData.Span).Pixels.ToArray());
    }

    [Fact]
    public void EveryAdjacentCapacityBoundaryIsExactAndOversizedInputsAreNotTruncated()
    {
        for (int bits = 1230; bits <= 1266; bits++)
        {
            byte[] pixels = CapacityPlane(bits);
            byte[] original = (byte[])pixels.Clone();
            Assert.Equal(bits, CartridgeLogo.MeasureEncodedBitLength(pixels));
            if (bits <= 1248) { Assert.Equal(bits, CartridgeLogo.Parse(CartridgeLogo.FromPixels(pixels).RawData.Span).EncodedBitLength); }
            else
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => CartridgeLogo.FromPixels(pixels));
                Assert.Contains(bits.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
                RgbaColor32[] colors = pixels.Select(value => value == 0 ? new RgbaColor32(255, 255, 255) : new(0, 0, 0)).ToArray();
                Assert.Throws<InvalidOperationException>(() => CartridgeLogo.FromRgba32(colors, new(0, 0, 0), new(255, 255, 255)));
            }
            Assert.Equal(original, pixels);
        }
    }

    [Fact]
    public void SystematicPayloadMutationsEitherDecodeBoundedlyOrFailWithFormatException()
    {
        byte[] source = CartridgeLogo.FromPixels(CapacityPlane(1248)).WritePreserved();
        for (int i = 0; i < 1248; i++)
        {
            byte[] data = (byte[])source.Clone();
            data[i / 8] ^= (byte)(1 << (i % 8));
            try
            {
                CartridgeLogo logo = CartridgeLogo.Parse(data);
                Assert.Equal(1664, logo.Pixels.Length);
                Assert.InRange(logo.EncodedBitLength, 436, 1248);
                Assert.Equal(data, logo.WritePreserved());
                Assert.Equal(logo.Pixels.ToArray(), CartridgeLogo.Parse(logo.WriteCanonical()).Pixels.ToArray());
            }
            catch (FormatException) { }
        }
    }

    private static byte[] CapacityPlane(int bits)
    {
        byte[] nibbles = new byte[416];
        int remaining = bits - 436;
        int cursor = 0;
        while (remaining > 0)
        {
            int cost = Math.Min(5, remaining);
            if (remaining - cost is 1 or 2) { cost = remaining - 3; }
            nibbles[cursor++] = cost switch { 5 => 5, 4 => 2, _ => 1 };
            remaining -= cost;
        }
        byte[] tiles = new byte[208];
        ushort value = 0;
        for (int i = 0; i < 104; i++)
        {
            value = unchecked((ushort)(value + nibbles[i * 4] + (nibbles[(i * 4) + 1] << 4) + (nibbles[(i * 4) + 2] << 8) + (nibbles[(i * 4) + 3] << 12)));
            BinaryPrimitives.WriteUInt16LittleEndian(tiles.AsSpan(i * 2), value);
        }
        byte[] pixels = new byte[1664];
        for (int tile = 0; tile < 26; tile++)
        {
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int destination = (((tile / 13) * 8 + y) * 104) + ((tile % 13) * 8) + x;
                    pixels[destination] = (byte)((tiles[(tile * 8) + y] >> x) & 1);
                }
            }
        }
        return pixels;
    }
}
