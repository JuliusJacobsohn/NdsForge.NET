using System.Buffers.Binary;
using NdsForge.Nitro.Audio;

namespace NdsForge.Audio.Wav.Tests;

public sealed class NitroWavAdapterTests
{
    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8, 8)]
    [InlineData(NitroWaveEncoding.Pcm16, 4)]
    [InlineData(NitroWaveEncoding.ImaAdpcm, 8)]
    public void StandaloneExportRetainsSamplesRateAndInclusiveLoopEndpoint(NitroWaveEncoding encoding, int start)
    {
        NitroWave wave = NitroWave.Create(WavFixture.Samples(16, 1, 128), encoding, 22050, new()
        {
            LoopStartSample = start,
            Timer = 321,
            EncodingOptions = new() { InitialPredictor = 1000, InitialStepIndex = 20 }
        });
        WavFile file = NitroWavAdapter.FromSwav(SwavFile.Create(wave), new() { AdpcmClipping = NitroAdpcmClipping.Signed16 });
        Assert.Equal(wave.Decode(new() { AdpcmClipping = NitroAdpcmClipping.Signed16 }), file.Decode());
        Assert.Equal(22050u, file.SampleRate); Assert.Equal(128, file.FrameCount); Assert.Equal(1, file.ChannelCount);
        WavLoop loop = Assert.Single(file.Sampler!.Loops);
        Assert.Equal(start, loop.StartFrame); Assert.Equal(128, loop.EndFrameExclusive);
        Assert.Equal(127u, BinaryPrimitives.ReadUInt32LittleEndian(file.Sampler.RawData.Span[48..]));
        Assert.Equal(45351u, file.Sampler.Metadata.SamplePeriodNanoseconds);
        SwavFile imported = NitroWavAdapter.ToSwav(file, NitroWaveEncoding.Pcm16);
        Assert.Equal(file.Decode(), imported.Wave.Decode()); Assert.Equal(start, imported.Wave.LoopStartSample);
        Assert.Equal((ushort)759, imported.Wave.Timer);
    }

    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8, 1)]
    [InlineData(NitroWaveEncoding.Pcm8, 2)]
    [InlineData(NitroWaveEncoding.Pcm16, 1)]
    [InlineData(NitroWaveEncoding.Pcm16, 2)]
    [InlineData(NitroWaveEncoding.ImaAdpcm, 1)]
    [InlineData(NitroWaveEncoding.ImaAdpcm, 2)]
    public void StreamImportAndExportRetainOddDurationsAndChannelOrdering(NitroWaveEncoding encoding, int channels)
    {
        short[] samples = WavFixture.Samples(16, channels, 131);
        WavFile input = WavFile.Create(samples, channels, 22050,
            new() { UseExtensibleFormat = true, Sampler = WavSampler.Create([new(99, 0, 5, 131)]) });
        StrmFile native = NitroWavAdapter.ToStrm(input, encoding, new() { BlockByteLength = 32 });
        WavFile output = NitroWavAdapter.FromStrm(native, new() { UseExtensibleFormat = true });
        Assert.Equal(native.Decode(), output.Decode()); Assert.Equal(channels, output.ChannelCount);
        Assert.Equal(131, output.FrameCount); Assert.Equal(5, Assert.Single(output.Sampler!.Loops).StartFrame);
        Assert.Equal(channels == 1 ? 4u : 3u, output.ChannelMask);
        Assert.Equal(samples, NitroWavAdapter.ToStrm(input, NitroWaveEncoding.Pcm16).Decode());
        Assert.Equal(output.Decode(), NitroWavAdapter.ToStrm(output, NitroWaveEncoding.Pcm16).Decode());
    }

    [Fact]
    public void UnsignedWavBytesAreNotCopiedAsSignedNativePcm8()
    {
        WavFile file = WavFile.Create([short.MinValue, -256, 0, 32512], 1, 22050, new() { Encoding = WavPcmEncoding.Unsigned8 });
        NitroWave wave = NitroWavAdapter.ToWave(file, NitroWaveEncoding.Pcm8);
        Assert.Equal(new byte[] { 128, 255, 0, 127 }, wave.EncodedData.ToArray());
        WavFile exported = NitroWavAdapter.FromWave(wave, new() { Encoding = WavPcmEncoding.Unsigned8 });
        Assert.Equal(file.WritePreserved(), exported.WritePreserved()); Assert.Null(exported.Sampler);
        Assert.Equal(file.Decode(), NitroWavAdapter.FromWave(wave).Decode());
    }

    [Fact]
    public void NativeWordPaddingAndLoopAlignmentAreCallerChoices()
    {
        WavFile file = WavFile.Create([10, 20, 30], 1, 22050, new() { Sampler = WavSampler.Create([new(0, 0, 0, 3)]) });
        Assert.Throws<ArgumentException>(() => NitroWavAdapter.ToWave(file, NitroWaveEncoding.Pcm16));
        NitroWave padded = NitroWavAdapter.ToWave(file, NitroWaveEncoding.Pcm16, new() { PadFinalWord = true });
        Assert.Equal(new short[] { 10, 20, 30, 30 }, padded.Decode()); Assert.Equal(4, padded.LoopEndSample);
        WavFile unaligned = WavFile.Create(new short[8], 1, 22050, new() { Sampler = WavSampler.Create([new(0, 0, 1, 8)]) });
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWavAdapter.ToWave(unaligned, NitroWaveEncoding.ImaAdpcm));
        Assert.Equal(1, NitroWavAdapter.ToStrm(unaligned, NitroWaveEncoding.ImaAdpcm).LoopStartSample);
        Assert.Empty(NitroWavAdapter.ToWave(WavFile.Create([], 1, 22050), NitroWaveEncoding.Pcm16).Decode());
    }

    [Fact]
    public void AdpcmClippingChoiceIsPropagatedExplicitly()
    {
        NitroWave wave = NitroWave.CreateEncoded([0, 128, 88, 0, 0xFF, 0xFF, 0xFF, 0xFF], NitroWaveEncoding.ImaAdpcm, 22050, 759);
        WavFile ds = NitroWavAdapter.FromWave(wave);
        WavFile signed = NitroWavAdapter.FromWave(wave, new() { AdpcmClipping = NitroAdpcmClipping.Signed16 });
        Assert.All(ds.Decode(), value => Assert.Equal(-32767, value));
        Assert.All(signed.Decode(), value => Assert.Equal(short.MinValue, value));
    }
}
