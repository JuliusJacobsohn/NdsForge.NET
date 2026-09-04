using NdsForge.Nitro.Audio;

namespace NdsForge.Audio.Wav.Tests;

public sealed class NitroWavSafetyTests
{
    [Theory]
    [InlineData(1u, 0u, 0u, 8)]
    [InlineData(2u, 0u, 0u, 8)]
    [InlineData(0u, 1u, 0u, 8)]
    [InlineData(0u, 0u, 1u, 8)]
    [InlineData(0u, 0u, 0u, 7)]
    public void UnrepresentableLoopsRequireExplicitDisregard(uint type, uint fraction, uint count, int end)
    {
        WavFile file = WavFile.Create(new short[8], 1, 22050,
            new() { Sampler = WavSampler.Create([new(0, type, 0, end, fraction, count)]) });
        Assert.Throws<NotSupportedException>(() => NitroWavAdapter.ToStrm(file, NitroWaveEncoding.Pcm16));
        Assert.Throws<NotSupportedException>(() => NitroWavAdapter.ToWave(file, NitroWaveEncoding.Pcm16));
        Assert.False(NitroWavAdapter.ToStrm(file, NitroWaveEncoding.Pcm16, loopPolicy: WavLoopImportPolicy.Ignore).IsLooping);
        Assert.Equal(2, NitroWavAdapter.ToWave(file, NitroWaveEncoding.Pcm16, new() { LoopStartSample = 2 }, WavLoopImportPolicy.Ignore).LoopStartSample);
    }

    [Fact]
    public void MultipleConflictingAndExplicitLoopsHaveDistinctPolicies()
    {
        WavFile multiple = WavFile.Create(new short[8], 1, 22050, new() { Sampler = WavSampler.Create([new(0, 0, 0, 8), new(1, 0, 2, 8)]) });
        Assert.Throws<NotSupportedException>(() => NitroWavAdapter.ToWave(multiple, NitroWaveEncoding.Pcm8));
        WavFile single = WavFile.Create(new short[8], 1, 22050, new() { Sampler = WavSampler.Create([new(0, 0, 2, 8)]) });
        Assert.Throws<ArgumentException>(() => NitroWavAdapter.ToStrm(single, NitroWaveEncoding.Pcm16, new() { LoopStartSample = 3 }));
        Assert.Equal(2, NitroWavAdapter.ToWave(single, NitroWaveEncoding.Pcm16, new() { LoopStartSample = 2 }).LoopStartSample);
        WavFile empty = WavFile.Create(new short[8], 1, 22050, new() { Sampler = WavSampler.Create([]) });
        Assert.Equal(4, NitroWavAdapter.ToStrm(empty, NitroWaveEncoding.Pcm8, new() { LoopStartSample = 4 }).LoopStartSample);
    }

    [Fact]
    public void ImportRejectsImplicitResamplingMixingAndSpeakerReassignment()
    {
        WavFile stereo = WavFile.Create([], 2, 22050);
        Assert.Throws<NotSupportedException>(() => NitroWavAdapter.ToWave(stereo, NitroWaveEncoding.Pcm8));
        WavFile highRate = WavFile.Create([], 1, 96000);
        Assert.Throws<NotSupportedException>(() => NitroWavAdapter.ToStrm(highRate, NitroWaveEncoding.Pcm16));
        WavFile otherSpeaker = WavFile.Create([], 1, 22050, new() { UseExtensibleFormat = true, ChannelMask = 8 });
        Assert.Throws<NotSupportedException>(() => NitroWavAdapter.ToStrm(otherSpeaker, NitroWaveEncoding.Pcm16));
        WavFile unspecified = WavFile.Create([], 2, 22050, new() { UseExtensibleFormat = true, ChannelMask = 0 });
        Assert.Equal(2, NitroWavAdapter.ToStrm(unspecified, NitroWaveEncoding.Pcm16).ChannelCount);
    }

    [Fact]
    public void AdapterValidatesNullEnumsAndAllocationBounds()
    {
        WavFile file = WavFile.Create(new short[8], 1, 22050);
        NitroWave wave = NitroWave.Create(new short[8], NitroWaveEncoding.Pcm16, 22050, new() { LoopStartSample = 0 });
        Assert.Throws<ArgumentNullException>(() => NitroWavAdapter.FromWave(null!));
        Assert.Throws<ArgumentNullException>(() => NitroWavAdapter.FromSwav(null!));
        Assert.Throws<ArgumentNullException>(() => NitroWavAdapter.FromStrm(null!));
        Assert.Throws<ArgumentNullException>(() => NitroWavAdapter.ToWave(null!, NitroWaveEncoding.Pcm8));
        Assert.Throws<ArgumentNullException>(() => NitroWavAdapter.FromWave(wave, new() { Limits = null! }));
        Assert.Throws<ArgumentNullException>(() => NitroWavAdapter.ToStrm(file, NitroWaveEncoding.Pcm8, new() { Limits = null! }));
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWavAdapter.ToWave(file, (NitroWaveEncoding)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWavAdapter.ToStrm(file, NitroWaveEncoding.Pcm16, loopPolicy: (WavLoopImportPolicy)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWavAdapter.FromWave(wave, new() { Encoding = (WavPcmEncoding)99 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWavAdapter.FromWave(wave, new() { AdpcmClipping = (NitroAdpcmClipping)99 }));
        foreach (WavReadOptions limits in new WavReadOptions[] { new() { MaximumInputBytes = 127 }, new() { MaximumSampleValues = 7 },
            new() { MaximumChunks = 2 }, new() { MaximumLoops = 0 } })
        {
            Assert.Throws<InvalidDataException>(() => NitroWavAdapter.FromWave(wave, new() { Limits = limits }));
        }
        Assert.Equal(128, NitroWavAdapter.FromWave(wave, new() { Limits = new() { MaximumInputBytes = 128, MaximumSampleValues = 8, MaximumChunks = 3, MaximumLoops = 1 } }).DeclaredLength);
        Assert.Throws<InvalidDataException>(() => NitroWavAdapter.ToWave(file, NitroWaveEncoding.Pcm16, new() { MaximumSamples = 7 }));
        Assert.Throws<InvalidDataException>(() => NitroWavAdapter.ToStrm(file, NitroWaveEncoding.Pcm16, new() { Limits = new() { MaximumSampleValues = 7 } }));
    }
}
