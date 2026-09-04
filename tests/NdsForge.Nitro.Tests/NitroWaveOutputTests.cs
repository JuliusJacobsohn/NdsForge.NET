using NdsForge.Nitro.Audio;

namespace NdsForge.Nitro.Tests;

public sealed class NitroWaveOutputTests
{
    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8, "B34555C8CA83C00D9697E7CA0D3C9EDC91C1907E239E4388B7914A1EEAECF933")]
    [InlineData(NitroWaveEncoding.Pcm16, "D757BC0587FA937E274876E3288384CA85F22F3507C711173CC686F654D96C22")]
    [InlineData(NitroWaveEncoding.ImaAdpcm, "55E01C66753A1FF5043C24D6478CC963793F743ADA4E11D01DA0FC8E9C6438C0")]
    public void EncodedNonAlignedWaveProducesExpectedMeaningfulSamples(NitroWaveEncoding encoding, string expected)
    {
        short[] samples = Enumerable.Range(0, 131).Select(i => (short)((i * 997 % 60001) - 30000)).ToArray();
        byte[] encoded = NitroWaveCodec.Encode(samples, encoding, new() { InitialStepIndex = 20 });
        Assert.Equal(expected, NitroWaveCodecTests.Digest(NitroWaveCodec.Decode(encoded, encoding, samples.Length)));
        Assert.Equal(expected, NitroWaveCodecTests.Digest(NitroWaveCodec.Decode(encoded, encoding, samples.Length,
            new() { AdpcmClipping = NitroAdpcmClipping.Signed16 })));
    }

    [Theory]
    [InlineData(NitroAdpcmClipping.NintendoDs, "CF6A764F3C4FC2B0DFC56303B673695087C8BB75F01254F3486255BC38C92C47")]
    [InlineData(NitroAdpcmClipping.Signed16, "7C300FBCEE3E9D6B938E08086D7B5A42FB792B554836AF56A76867ACF9BD5F20")]
    public void SaturatingSignalHasExplicitSampleClippingPolicy(NitroAdpcmClipping clipping, string expected)
    {
        short[] samples = Enumerable.Range(0, 128).Select(i => i % 2 == 0 ? short.MinValue : short.MaxValue).ToArray();
        byte[] encoded = NitroWaveCodec.Encode(samples, NitroWaveEncoding.ImaAdpcm,
            new() { InitialStepIndex = 88, InitialPredictor = short.MinValue, AdpcmClipping = clipping });
        Assert.Equal(expected, NitroWaveCodecTests.Digest(NitroWaveCodec.Decode(encoded, NitroWaveEncoding.ImaAdpcm,
            options: new() { AdpcmClipping = clipping })));
    }

    [Theory]
    [InlineData(NitroAdpcmClipping.NintendoDs, "59A7BB25F0A9D5E9FDE4296CDA00D43E5A52389074575F5BD0A1BFDCD0F31C87")]
    [InlineData(NitroAdpcmClipping.Signed16, "52C6772C3C46F0E00D8779FE4938904831AB0843C08F6B0644122BA95E029AB5")]
    public void SaturationPolicyAlsoControlsEncoderSelection(NitroAdpcmClipping clipping, string expected)
    {
        byte[] encoded = NitroWaveCodec.Encode([short.MinValue, short.MaxValue, short.MinValue], NitroWaveEncoding.ImaAdpcm,
            new() { InitialPredictor = short.MinValue, AdpcmClipping = clipping });
        Assert.Equal(expected, NitroWaveCodecTests.Digest(NitroWaveCodec.Decode(encoded, NitroWaveEncoding.ImaAdpcm, 3,
            new() { AdpcmClipping = clipping })));
    }
}
