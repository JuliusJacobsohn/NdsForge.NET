using System.Buffers.Binary;
using NdsForge.Nitro.Audio;

namespace NdsForge.Nitro.Tests;

public sealed class SwavSafetyTests
{
    [Fact]
    public void EveryTruncatedPrefixAndInvalidEnvelopeFails()
    {
        byte[] data = SwavFile.Create(NitroWave.Create(new short[8], NitroWaveEncoding.ImaAdpcm, 8000)).WritePreserved();
        for (int length = 0; length < data.Length; length++)
        {
            byte[] prefix = data[..length];
            Assert.Throws<InvalidDataException>(() => SwavFile.Parse(prefix));
        }
        foreach (int offset in new[] { 0, 4, 6, 8, 12, 14, 16, 20 })
        {
            byte[] invalid = (byte[])data.Clone();
            invalid[offset] ^= 0xFF;
            Assert.Throws<InvalidDataException>(() => SwavFile.Parse(invalid));
        }
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => SwavFile.Parse(data));
    }

    [Fact]
    public void InvalidWaveStateCountsRatesAndLoopsFail()
    {
        byte[] block = NitroWave.Create(new short[8], NitroWaveEncoding.ImaAdpcm, 8000).WriteSampleBlock();
        for (int length = 0; length < block.Length; length++)
        {
            byte[] prefix = block[..length];
            Assert.Throws<InvalidDataException>(() => NitroWave.ParseSampleBlock(prefix));
        }
        foreach (int offset in new[] { 0, 8, 14 })
        {
            byte[] invalid = (byte[])block.Clone();
            invalid[offset] = 0xFF;
            Assert.Throws<InvalidDataException>(() => NitroWave.ParseSampleBlock(invalid));
        }
        Assert.Throws<InvalidDataException>(() => NitroWave.CreateEncoded([], NitroWaveEncoding.ImaAdpcm, 8000, 0));
        Assert.Throws<InvalidDataException>(() => NitroWave.CreateEncoded([0, 0, 0, 0], NitroWaveEncoding.Pcm8, 0, 0));
        Assert.Throws<InvalidDataException>(() => NitroWave.CreateEncoded([0, 0, 0, 0], NitroWaveEncoding.ImaAdpcm, 8000, 0, 0, 1));
        Assert.Throws<InvalidDataException>(() => NitroWave.CreateEncoded([0, 0, 0, 0], NitroWaveEncoding.Pcm8, 8000, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => NitroWave.CreateEncoded([0], NitroWaveEncoding.Pcm8, 8000, 0));
        Assert.Throws<ArgumentException>(() => NitroWave.CreateEncoded([], NitroWaveEncoding.Pcm8, 8000, 0, 1));
    }

    [Fact]
    public void FactoriesRejectUnrepresentableOrUnalignedLoopsAndTimers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWave.Create([], NitroWaveEncoding.Pcm8, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWave.Create([], NitroWaveEncoding.Pcm8, 1));
        foreach (int loop in new[] { -1, 1, 8 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NitroWave.Create(new short[8], NitroWaveEncoding.ImaAdpcm, 8000, new() { LoopStartSample = loop }));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWave.Create(new short[262148], NitroWaveEncoding.Pcm8, 8000, new() { LoopStartSample = 262144 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWave.Create([], (NitroWaveEncoding)3, 8000));
        Assert.Throws<ArgumentOutOfRangeException>(() => NitroWave.CreateEncoded([], (NitroWaveEncoding)3, 8000, 0));
    }

    [Fact]
    public void LimitsAndNullArgumentsFailBeforeOutputAllocation()
    {
        byte[] data = SwavSafetyTestsFixture();
        Assert.Throws<InvalidDataException>(() => SwavFile.Parse(data, new() { MaximumInputBytes = data.Length - 1 }));
        Assert.Throws<InvalidDataException>(() => SwavFile.Parse(data, new() { MaximumSamples = 3 }));
        Assert.Throws<InvalidDataException>(() => NitroWave.ParseSampleBlock(data.AsSpan(24), new() { MaximumInputBytes = 0 }));
        Assert.Throws<InvalidDataException>(() => NitroWave.CreateEncoded([0, 0, 0, 0], NitroWaveEncoding.Pcm8, 8000, 0, options: new() { MaximumInputBytes = 15 }));
        Assert.Throws<InvalidDataException>(() => NitroWave.Create([1], NitroWaveEncoding.Pcm8, 8000, new() { PadFinalWord = true, MaximumSamples = 3 }));
        Assert.Throws<InvalidDataException>(() => NitroWave.Create([1], NitroWaveEncoding.Pcm8, 8000, new() { PadFinalWord = true, EncodingOptions = new() { MaximumOutputBytes = 3 } }));
        Assert.Throws<InvalidDataException>(() => SwavFile.Parse(data).CreateBuilder().Build(new() { MaximumOutputBytes = data.Length - 1 }));
        Assert.Throws<ArgumentNullException>(() => SwavFile.Create(null!));
        Assert.Throws<ArgumentNullException>(() => NitroWave.Create([], NitroWaveEncoding.Pcm8, 8000, new() { EncodingOptions = null! }));
        SwavFileBuilder builder = SwavFile.Parse(data).CreateBuilder();
        builder.Wave = null!;
        Assert.Throws<ArgumentNullException>(() => builder.Build());
        foreach (int limit in new[] { -1, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SwavFile.Parse(data, new() { MaximumInputBytes = limit }));
            Assert.Throws<ArgumentOutOfRangeException>(() => SwavFile.Parse(data, new() { MaximumSamples = limit }));
            Assert.Throws<ArgumentOutOfRangeException>(() => SwavFile.Parse(data).CreateBuilder().Build(new() { MaximumOutputBytes = limit }));
            Assert.Throws<ArgumentOutOfRangeException>(() => NitroWave.Create([], NitroWaveEncoding.Pcm8, 8000, new() { MaximumSamples = limit }));
        }
    }

    private static byte[] SwavSafetyTestsFixture() => SwavFile.Create(NitroWave.Create(new short[4], NitroWaveEncoding.Pcm8, 8000)).WritePreserved();
}
