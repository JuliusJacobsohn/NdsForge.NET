using System.Buffers.Binary;
using NdsForge.Audio.Wav;
using NdsForge.Nitro.Audio;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Checks WAV interchange against the sample bytes included in the neutral native-audio corpus digests.</summary>
internal static class CorpusWavChecks
{
    internal static void Check(StrmFile stream, byte[] expectedPcm)
    {
        WavFile wav = NitroWavAdapter.FromStrm(stream, new() { AdpcmClipping = NitroAdpcmClipping.Signed16 });
        Check(wav, expectedPcm, stream.SampleRate, stream.ChannelCount, stream.SampleCount, stream.LoopStartSample);
        StrmFile imported = NitroWavAdapter.ToStrm(wav, NitroWaveEncoding.Pcm16, new() { Timer = stream.Timer });
        Assert.True(imported.Decode().AsSpan().SequenceEqual(wav.Decode()));
        Assert.Equal(stream.LoopStartSample, imported.LoopStartSample);
        WavFile eight = NitroWavAdapter.FromStrm(stream, new()
        {
            Encoding = WavPcmEncoding.Unsigned8,
            UseExtensibleFormat = true,
            AdpcmClipping = NitroAdpcmClipping.Signed16
        });
        CheckEight(eight, expectedPcm);
    }

    internal static void Check(SwavFile wave, byte[] expectedPcm)
    {
        WavFile wav = NitroWavAdapter.FromSwav(wave, new() { AdpcmClipping = NitroAdpcmClipping.Signed16 });
        Check(wav, expectedPcm, wave.Wave.SampleRate, 1, wave.Wave.SampleCount, wave.Wave.LoopStartSample);
        SwavFile imported = NitroWavAdapter.ToSwav(wav, NitroWaveEncoding.Pcm16, new() { Timer = wave.Wave.Timer });
        Assert.True(imported.Wave.Decode().AsSpan().SequenceEqual(wav.Decode()));
        Assert.Equal(wave.Wave.LoopStartSample, imported.Wave.LoopStartSample);
        WavFile eight = NitroWavAdapter.FromSwav(wave, new()
        {
            Encoding = WavPcmEncoding.Unsigned8,
            UseExtensibleFormat = true,
            AdpcmClipping = NitroAdpcmClipping.Signed16
        });
        CheckEight(eight, expectedPcm);
    }

    private static void Check(WavFile wav, byte[] expectedPcm, uint rate, int channels, int frames, int? loop)
    {
        Assert.True(wav.EncodedSamples.Span.SequenceEqual(expectedPcm));
        Assert.Equal(rate, wav.SampleRate); Assert.Equal(channels, wav.ChannelCount); Assert.Equal(frames, wav.FrameCount);
        Assert.Equal(loop, wav.Sampler?.Loops[0].StartFrame);
        if (loop.HasValue) { Assert.Equal(frames, wav.Sampler!.Loops[0].EndFrameExclusive); }
        byte[] bytes = wav.WritePreserved();
        WavFile parsed = WavFile.Parse(bytes, new() { AllowMissingFinalPadding = false });
        Assert.Equal(bytes, parsed.WritePreserved());
        short[] decoded = parsed.Decode();
        for (int i = 0; i < decoded.Length; i++) { Assert.Equal(BinaryPrimitives.ReadInt16LittleEndian(expectedPcm.AsSpan(i * 2)), decoded[i]); }
    }

    private static void CheckEight(WavFile wav, byte[] expectedPcm)
    {
        Assert.True(wav.IsExtensible);
        Assert.Equal(expectedPcm.Length / 2, wav.SampleValueCount);
        ReadOnlySpan<byte> data = wav.EncodedSamples.Span;
        for (int i = 0; i < data.Length; i++)
        {
            short value = BinaryPrimitives.ReadInt16LittleEndian(expectedPcm.AsSpan(i * 2));
            Assert.Equal((byte)((value >> 8) + 128), data[i]);
        }
    }
}
