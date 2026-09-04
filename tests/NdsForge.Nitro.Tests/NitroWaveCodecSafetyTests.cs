using NdsForge.Nitro.Audio;

namespace NdsForge.Nitro.Tests;

public sealed class NitroWaveCodecSafetyTests
{
    [Fact]
    public void RejectsTruncatedSamplesHeadersAndInvalidIndicesBeforeAllocation()
    {
        Assert.Throws<InvalidDataException>(() => NitroWaveCodec.Decode([1], NitroWaveEncoding.Pcm16));
        for (int size = 0; size < 4; size++) { Assert.Throws<InvalidDataException>(() => NitroWaveCodec.Decode(new byte[size], NitroWaveEncoding.ImaAdpcm)); }
        foreach (byte index in new byte[] { 89, 127, 255 })
        {
            Assert.Throws<InvalidDataException>(() => NitroWaveCodec.Decode([0, 0, index, 0], NitroWaveEncoding.ImaAdpcm, 0));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWaveCodec.Decode([0], NitroWaveEncoding.Pcm8, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWaveCodec.Decode([0], NitroWaveEncoding.Pcm8, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWaveCodec.Decode([0], (NitroWaveEncoding)3));
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWaveCodec.Encode([0], (NitroWaveEncoding)3));
    }

    [Fact]
    public void LimitsApplyToRequestedOutputAndAllowEmptyPcmOrHeaderOnlyAdpcm()
    {
        Assert.Empty(NitroWaveCodec.Decode([], NitroWaveEncoding.Pcm8, options: new() { MaximumSamples = 0 }));
        Assert.Empty(NitroWaveCodec.Decode([], NitroWaveEncoding.Pcm16));
        Assert.Empty(NitroWaveCodec.Decode([0, 0, 0, 0], NitroWaveEncoding.ImaAdpcm));
        Assert.Throws<InvalidDataException>(() => NitroWaveCodec.Decode([0, 0, 0, 0, 0], NitroWaveEncoding.ImaAdpcm, options: new() { MaximumSamples = 1 }));
        Assert.Single(NitroWaveCodec.Decode([0, 0, 0, 0, 0], NitroWaveEncoding.ImaAdpcm, 1, new() { MaximumSamples = 1 }));
        Assert.Throws<InvalidDataException>(() => NitroWaveCodec.Encode([0], NitroWaveEncoding.Pcm16, new() { MaximumOutputBytes = 1 }));
        Assert.Throws<InvalidDataException>(() => NitroWaveCodec.Encode([], NitroWaveEncoding.ImaAdpcm, new() { MaximumOutputBytes = 3 }));
        Assert.Equal(new byte[4], NitroWaveCodec.Encode([], NitroWaveEncoding.ImaAdpcm, new() { MaximumOutputBytes = 4 }));
        Assert.Empty(NitroWaveCodec.Encode([], NitroWaveEncoding.Pcm8, new() { MaximumOutputBytes = 0 }));
    }

    [Fact]
    public void RejectsUnsupportedOptions()
    {
        foreach (int limit in new[] { -1, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NitroWaveCodec.Decode([], NitroWaveEncoding.Pcm8, options: new() { MaximumSamples = limit }));
            Assert.Throws<ArgumentOutOfRangeException>(() => NitroWaveCodec.Encode([], NitroWaveEncoding.Pcm8, new() { MaximumOutputBytes = limit }));
        }
        foreach (int index in new[] { -1, 89 }) { Assert.Throws<ArgumentOutOfRangeException>(() => NitroWaveCodec.Encode([], NitroWaveEncoding.ImaAdpcm, new() { InitialStepIndex = index })); }
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWaveCodec.Decode([], NitroWaveEncoding.Pcm8, options: new() { AdpcmClipping = (NitroAdpcmClipping)2 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWaveCodec.Encode([], NitroWaveEncoding.Pcm8, new() { AdpcmClipping = (NitroAdpcmClipping)2 }));
    }
}
