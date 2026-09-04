using System.Buffers.Binary;
using NdsForge.Nitro.Audio;

namespace NdsForge.Nitro.Tests;

public sealed class SwavFileTests
{
    [Fact]
    public void StandardEnvelopeHasExactFieldsAndIndependentBuilder()
    {
        NitroWave wave = NitroWave.CreateEncoded([0, 1, 2, 3], NitroWaveEncoding.Pcm8, 22050, 759);
        SwavFile file = SwavFile.Create(wave);
        Assert.Equal(Convert.FromHexString("53574156FFFE00012800000010000100444154411800000000002256F70200000100000000010203"), file.WritePreserved());
        Assert.Equal(16, file.HeaderLength);
        Assert.Equal(40, file.DeclaredLength);
        Assert.Equal(0xFEFF, file.ByteOrderMarker);
        SwavFileBuilder builder = file.CreateBuilder();
        Assert.Equal(file.WritePreserved(), builder.Build());
        builder.Wave = NitroWave.Create(new short[16], NitroWaveEncoding.Pcm16, 16000);
        SwavFile changed = SwavFile.Parse(builder.Build());
        Assert.Equal(16000, changed.Wave.SampleRate);
        Assert.Equal(16, changed.Wave.SampleCount);
        Assert.Equal(4, file.Wave.SampleCount);
        Assert.Equal(68, changed.DeclaredLength);
    }

    [Fact]
    public void ExtendedEnvelopeAndBothPaddingRegionsArePreservedOrExplicitlyCanonicalized()
    {
        byte[] extended = ExtendedFixture();
        SwavFile file = SwavFile.Parse(extended);
        Assert.Equal(20, file.HeaderLength);
        Assert.Equal(47, file.DeclaredLength);
        Assert.Equal(0xFFFE, file.ByteOrderMarker);
        Assert.Equal(extended, file.WritePreserved());
        Assert.Equal(extended, file.CreateBuilder().Build());
        byte[] canonical = file.CreateBuilder().Build(new() { PreserveSourceLayout = false });
        Assert.Equal(40, canonical.Length);
        Assert.Equal(16, SwavFile.Parse(canonical).HeaderLength);
        Assert.Equal(file.Wave.EncodedData.ToArray(), SwavFile.Parse(canonical).Wave.EncodedData.ToArray());
        SwavFileBuilder builder = file.CreateBuilder();
        builder.Wave = NitroWave.Create(new short[20], NitroWaveEncoding.Pcm8, 8000);
        byte[] replaced = builder.Build();
        Assert.Equal(new byte[] { 0xA1, 0xA2, 0xA3, 0xA4 }, replaced[16..20]);
        Assert.Equal(new byte[] { 0xD1, 0xD2 }, replaced[^2..]);
        Assert.Equal(20, SwavFile.Parse(replaced).Wave.SampleCount);
        extended[16] = 0;
        Assert.Equal(0xA1, file.WritePreserved()[16]);
    }

    internal static byte[] ExtendedFixture()
    {
        byte[] ordinary = SwavFile.Create(NitroWave.CreateEncoded([0, 1, 2, 3], NitroWaveEncoding.Pcm8, 22050, 759)).WritePreserved();
        byte[] extended = [.. ordinary[..16], 0xA1, 0xA2, 0xA3, 0xA4, .. ordinary[16..], 0xB1, 0xB2, 0xB3, 0xD1, 0xD2];
        BinaryPrimitives.WriteUInt16LittleEndian(extended.AsSpan(4), 0xFFFE);
        BinaryPrimitives.WriteInt32LittleEndian(extended.AsSpan(8), 47);
        BinaryPrimitives.WriteUInt16LittleEndian(extended.AsSpan(12), 20);
        BinaryPrimitives.WriteInt32LittleEndian(extended.AsSpan(24), 27);
        return extended;
    }
}
