using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Nitro.Audio;

namespace NdsForge.Nitro.Tests;

public sealed class NitroWaveCodecTests
{
    [Theory]
    [InlineData(0, 0, "4D22EC40C4BE09BAF4C33689142A9C110CBB5F9F7FA3FC7C6CDAB4DAEC7FC4EF")]
    [InlineData(0, 20, "3F44ACD2DB0F4C161F469A7BD0ADB7ABF6C67B1D8E697FE5E72DDE042588812D")]
    [InlineData(0, 88, "BFF7C771F427174E9FE9B262F8BF7375F5DAC7CE545DC77048ACE8FDCFED4AF8")]
    [InlineData(1000, 0, "B2E4E2365DB354E410FDED34FA12F50B70806CAB0D62B93FB44ED061509E3D4A")]
    [InlineData(1000, 20, "E2A434F88849E5864529F20FD6A59E603A94B6FB97A7D6DD6D5EC4A57E9FF91D")]
    [InlineData(1000, 88, "03D93FF57B63E6B3CD1F2B1F2E370BAE5227C81CC78B2BC8862B4B43BB0431F1")]
    [InlineData(-32768, 0, "5DD415FE74D26415A0F36DE22B0B702587EAB4F4E8B9FEF6BF24952D37565B19")]
    [InlineData(-32768, 20, "39C85E3DCBCF1F1DC032DDEDD0B20C025F4C91CBC1DFACF52E46A93A4435CC93")]
    [InlineData(-32768, 88, "A147CA3A1697C290DC5219560E850DEC83AC5A46D5EA3FAEBB341C47B6424C69")]
    [InlineData(32767, 0, "0C2E07AC4A9BBB3B3609E889573CBA897ADBFA21C619C8EAE79C65A3EC07BDE4")]
    [InlineData(32767, 20, "6B04F0100B3A2816F5D95C1083BB2D52BDD616D41780533D239D9274004A34CA")]
    [InlineData(32767, 88, "5C3B1A60A33A5399B59E6CC79F7F29D1EBF0A55BDEDEE992DAAC30D44299DCFC")]
    public void ExplicitInitialStateAndSignedClippingMatchSampleVectors(short predictor, byte index, string expected)
    {
        byte[] data = Convert.FromHexString("00000000001123456789ABCDEF77FF88");
        BinaryPrimitives.WriteInt16LittleEndian(data, predictor);
        data[2] = index;
        short[] samples = NitroWaveCodec.Decode(data, NitroWaveEncoding.ImaAdpcm, options: new() { AdpcmClipping = NitroAdpcmClipping.Signed16 });
        Assert.Equal(24, samples.Length);
        Assert.Equal(expected, Digest(samples));
    }

    [Fact]
    public void DsClippingIsDirectionalAndInitialPredictorIsNotAnOutputSample()
    {
        short[] ds = NitroWaveCodec.Decode([0, 0x80, 0, 0, 0x80], NitroWaveEncoding.ImaAdpcm);
        short[] generic = NitroWaveCodec.Decode([0, 0x80, 0, 0, 0x80], NitroWaveEncoding.ImaAdpcm,
            options: new() { AdpcmClipping = NitroAdpcmClipping.Signed16 });
        Assert.Equal(new short[] { -32768, -32767 }, ds);
        Assert.Equal(new short[] { -32768, -32768 }, generic);
        Assert.Equal(new short[] { 1006, 1011 }, NitroWaveCodec.Decode([0xE8, 3, 20, 0, 0], NitroWaveEncoding.ImaAdpcm));
    }

    [Fact]
    public void DsAdpcmWaveVectorMatchesFullSampleDigest()
    {
        byte[] data = Convert.FromHexString("E8031400FFFFFFAF0000000001111012212232333433443224333443333443333433F4FFBF800080000000100010011111122223433243333443333443334324F3FFBF80");
        Assert.Equal("DA5284FADB8E0843B812F2E7B664224C466894EEA556ACC9A71FAEEC0DC8D39C", Digest(NitroWaveCodec.Decode(data, NitroWaveEncoding.ImaAdpcm)));
    }

    [Fact]
    public void PcmCodecsMatchSignedValuesAndDiscardOnlyLowPcm8Bits()
    {
        short[] samples = Enumerable.Range(0, 128).Select(i => (short)(((i * 997) % 60001) - 30000)).ToArray();
        byte[] pcm16 = NitroWaveCodec.Encode(samples, NitroWaveEncoding.Pcm16);
        Assert.Equal("02F31A43207339102EA6CC24BC1EC5181E00FF4149C7B6B14B4FE027DFF183B2", Convert.ToHexString(SHA256.HashData(pcm16)));
        Assert.Equal(samples, NitroWaveCodec.Decode(pcm16, NitroWaveEncoding.Pcm16));
        short[] decoded8 = NitroWaveCodec.Decode(NitroWaveCodec.Encode(samples, NitroWaveEncoding.Pcm8), NitroWaveEncoding.Pcm8);
        Assert.Equal(new short[] { -32768, -256, 0, 32512 }, NitroWaveCodec.Decode([128, 255, 0, 127], NitroWaveEncoding.Pcm8));
        for (int i = 0; i < samples.Length; i++) { Assert.InRange(samples[i] - decoded8[i], 0, 255); }
    }

    [Fact]
    public void StoredPcm8WaveVectorMatchesFullSampleDigest()
    {
        byte[] data = Convert.FromHexString("8A8E92969A9EA2A6A9ADB1B5B9BDC1C5C9CDD0D4D8DCE0E4E8ECF0F3F7FBFF03070B0F13171A1E22262A2E32363A3D4145494D5155595D6164686C70748D9195999DA1A5A9ADB1B4B8BCC0C4C8CCD0D4D7DBDFE3E7EBEFF3F7FBFE02060A0E12161A1E2125292D3135393D4145484C5054585C6064686B6F738D9195989CA0A4");
        Assert.Equal("441FE3E1E445E34E6957AB2B5296D32F9FEC55D65294F81C3E7D0D0AB9867116", Digest(NitroWaveCodec.Decode(data, NitroWaveEncoding.Pcm8)));
    }

    [Fact]
    public void Pcm8QuantizationAndPcm16RoundTripCoverEverySignedInput()
    {
        short[] samples = Enumerable.Range(short.MinValue, 65536).Select(static i => (short)i).ToArray();
        Assert.Equal(samples, NitroWaveCodec.Decode(NitroWaveCodec.Encode(samples, NitroWaveEncoding.Pcm16), NitroWaveEncoding.Pcm16));
        byte[] encoded = NitroWaveCodec.Encode(samples, NitroWaveEncoding.Pcm8);
        short[] decoded = NitroWaveCodec.Decode(encoded, NitroWaveEncoding.Pcm8);
        for (int i = 0; i < samples.Length; i++)
        {
            Assert.Equal(unchecked((byte)(i / 256 + 128)), encoded[i]);
            Assert.Equal(i % 256, samples[i] - decoded[i]);
        }
    }

    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8)]
    [InlineData(NitroWaveEncoding.Pcm16)]
    [InlineData(NitroWaveEncoding.ImaAdpcm)]
    public void MeaningfulCountExcludesPaddingWithoutAnExtraInitialSample(NitroWaveEncoding encoding)
    {
        short[] samples = [100, 200, 300];
        byte[] encoded = NitroWaveCodec.Encode(samples, encoding);
        Assert.Equal(3, NitroWaveCodec.Decode(encoded, encoding, 3).Length);
        Assert.Equal(encoding == NitroWaveEncoding.ImaAdpcm ? 4 : 3, NitroWaveCodec.Decode(encoded, encoding).Length);
        Assert.Empty(NitroWaveCodec.Decode(encoded, encoding, 0));
        if (encoding == NitroWaveEncoding.ImaAdpcm) { Assert.Equal(0, encoded[^1] >> 4); }
    }

    [Fact]
    public void AdpcmEncoderHasStableTiesAndUsesFirstSampleAsDefaultPredictor()
    {
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0 }, NitroWaveCodec.Encode([0, 0], NitroWaveEncoding.ImaAdpcm));
        byte[] output = NitroWaveCodec.Encode([1000, 1000, 1000], NitroWaveEncoding.ImaAdpcm);
        Assert.Equal(new byte[] { 0xE8, 3, 0, 0, 0, 0 }, output);
        Assert.Equal(new short[] { 1000, 1000, 1000 }, NitroWaveCodec.Decode(output, NitroWaveEncoding.ImaAdpcm, 3));
        short[] samples = Enumerable.Range(0, 1024).Select(i => (short)(((i * 37) % 4000) - 2000)).ToArray();
        byte[] a = NitroWaveCodec.Encode(samples, NitroWaveEncoding.ImaAdpcm, new() { InitialPredictor = -2000, InitialStepIndex = 20 });
        byte[] b = NitroWaveCodec.Encode(samples, NitroWaveEncoding.ImaAdpcm, new() { InitialPredictor = -2000, InitialStepIndex = 20 });
        Assert.Equal(a, b);
        Assert.Equal(samples.Length, NitroWaveCodec.Decode(a, NitroWaveEncoding.ImaAdpcm).Length);
    }

    [Fact]
    public void HeaderReservedBitsDoNotAffectDecodedState()
    {
        Assert.Equal(NitroWaveCodec.Decode([0, 0, 20, 0, 0x21], NitroWaveEncoding.ImaAdpcm),
            NitroWaveCodec.Decode([0, 0, 148, 255, 0x21], NitroWaveEncoding.ImaAdpcm));
    }

    internal static string Digest(short[] samples)
    {
        byte[] bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++) { BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]); }
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
