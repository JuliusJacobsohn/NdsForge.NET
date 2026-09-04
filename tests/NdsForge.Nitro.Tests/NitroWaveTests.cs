using System.Buffers.Binary;
using NdsForge.Nitro.Audio;

namespace NdsForge.Nitro.Tests;

public sealed class NitroWaveTests
{
    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8, 4, 0)]
    [InlineData(NitroWaveEncoding.Pcm16, 2, 0)]
    [InlineData(NitroWaveEncoding.ImaAdpcm, 8, 1)]
    public void WordLengthsAndLoopsExcludeAdpcmState(NitroWaveEncoding encoding, int quantum, int headerWords)
    {
        NitroWave wave = NitroWave.Create(new short[quantum * 3], encoding, 22050, new() { LoopStartSample = quantum });
        Assert.Equal(encoding, wave.Encoding);
        Assert.Equal(quantum * 3, wave.SampleCount);
        Assert.Equal(quantum, wave.LoopStartSample);
        Assert.Equal(wave.SampleCount, wave.LoopEndSample);
        Assert.Equal(1, wave.RawLoopFlag);
        Assert.True(wave.IsLooping);
        Assert.Equal(1 + headerWords, wave.LoopStartWords);
        Assert.Equal(2u, wave.RemainingWords);
        Assert.Equal(22050, wave.SampleRate);
        Assert.Equal(759, wave.Timer);
        Assert.Equal(new short[quantum * 3], wave.Decode());
        Assert.Equal(wave.WriteSampleBlock(), NitroWave.ParseSampleBlock(wave.WriteSampleBlock()).WriteSampleBlock());
    }

    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8, 4)]
    [InlineData(NitroWaveEncoding.Pcm16, 2)]
    [InlineData(NitroWaveEncoding.ImaAdpcm, 8)]
    public void FinalWordPaddingRequiresOptInAndRepeatsLastInput(NitroWaveEncoding encoding, int quantum)
    {
        Assert.Throws<ArgumentException>(() => NitroWave.Create([1024], encoding, 8000));
        NitroWave padded = NitroWave.Create([1024], encoding, 8000, new() { PadFinalWord = true });
        Assert.Equal(quantum, padded.SampleCount);
        Assert.Equal(Enumerable.Repeat((short)1024, quantum), padded.Decode());
        Assert.False(padded.IsLooping);
        Assert.Null(padded.LoopStartSample);
        Assert.Null(padded.LoopEndSample);
    }

    [Fact]
    public void EncodedCreationPreservesIndependentMetadataAndReservedState()
    {
        byte[] raw = [0, 0x80, 0x94, 0xFF, 0x21, 0x43, 0x65, 0x87];
        NitroWave wave = NitroWave.CreateEncoded(raw, NitroWaveEncoding.ImaAdpcm, 22050, 17, 1, 0x80);
        Assert.Equal(17, wave.Timer);
        Assert.Equal(0x80, wave.RawLoopFlag);
        Assert.Equal(0, wave.LoopStartSample);
        Assert.Equal(8, wave.SampleCount);
        Assert.Equal(raw, wave.EncodedData.ToArray());
        raw[0] = 12;
        Assert.Equal(0, wave.EncodedData.Span[0]);
        byte[] stored = wave.WriteSampleBlock();
        stored[12] = 99;
        Assert.Equal(0, wave.EncodedData.Span[0]);
    }

    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8)]
    [InlineData(NitroWaveEncoding.Pcm16)]
    [InlineData(NitroWaveEncoding.ImaAdpcm)]
    public void EmptyNonLoopingWaveHasNoDecodedSamples(NitroWaveEncoding encoding)
    {
        NitroWave wave = NitroWave.Create([], encoding, 1, new() { Timer = 0, MaximumSamples = 0 });
        Assert.Empty(wave.Decode(new() { MaximumSamples = 0 }));
        Assert.Equal(encoding == NitroWaveEncoding.ImaAdpcm ? 4 : 0, wave.EncodedData.Length);
        Assert.Equal(0, wave.Timer);
    }

    [Fact]
    public void InactiveLoopOffsetAndSamplePaddingRemainLossless()
    {
        NitroWave initial = NitroWave.CreateEncoded([1, 2, 3, 4, 5, 6, 7, 8], NitroWaveEncoding.Pcm8, 8000, 0, 2);
        byte[] stored = [.. initial.WriteSampleBlock(), 0xAA, 0xBB, 0xCC];
        NitroWave parsed = NitroWave.ParseSampleBlock(stored);
        Assert.Equal(stored, parsed.WriteSampleBlock());
        Assert.Equal(initial.WriteSampleBlock(), parsed.WriteSampleBlock(false));
        Assert.Equal(0u, parsed.RemainingWords);
        Assert.Null(parsed.LoopStartSample);
        Assert.Equal(8, parsed.Decode().Length);
        BinaryPrimitives.WriteUInt16LittleEndian(stored.AsSpan(2), 9000);
        Assert.Equal(8000, parsed.SampleRate);
    }
}
