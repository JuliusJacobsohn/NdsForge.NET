using System.Buffers.Binary;

namespace NdsForge.Audio.Wav.Tests;

public sealed class WavSamplerTests
{
    [Fact]
    public void InclusiveStoredEndsAndOpaqueSamplerFieldsRoundTrip()
    {
        var metadata = new WavSamplerMetadata
        {
            Manufacturer = 0x01000013,
            Product = 19,
            SamplePeriodNanoseconds = 45351,
            MidiUnityNote = 72,
            MidiPitchFraction = 0x80000000,
            SmpteFormat = 29,
            SmpteOffset = 0x01020304
        };
        WavLoop[] loops = [new(1, 0, 0, 1), new(2, 1, 2, 8, 123, 3), new(3, 55, 1, 7)];
        WavSampler sampler = WavSampler.Create(loops, metadata, [9, 8, 7]);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(sampler.RawData.Span[48..]));
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(sampler.RawData.Span[72..]));
        byte[] extended = [.. sampler.RawData.Span, 0xF3, 0xAA];
        byte[] input = WavFixture.Envelope(("fmt ", WavFixture.Format()), ("data", new byte[16]), ("smpl", extended));
        WavFile file = WavFile.Parse(input);
        Assert.Equal(loops, file.Sampler!.Loops); Assert.Equal(metadata, file.Sampler.Metadata);
        Assert.Equal(new byte[] { 9, 8, 7 }, file.Sampler.SamplerData.ToArray()); Assert.Equal(extended, file.Sampler.RawData.ToArray());
        WavFile rewritten = WavFile.Create(file.Decode(), 1, file.SampleRate, new() { Sampler = file.Sampler });
        Assert.Equal(extended, rewritten.Sampler!.RawData.ToArray());
        Assert.Equal((byte)0, rewritten.Chunks[2].PaddingByte);
        Assert.Equal(60u, WavSampler.Create([]).Metadata.MidiUnityNote);
    }

    [Fact]
    public void SamplerLimitsAndInvalidRangesAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => WavSampler.Create(null!));
        foreach (WavLoop loop in new WavLoop[] { new(0, 0, -1, 1), new(0, 0, 1, 1), new(0, 0, 3, 2) })
        {
            Assert.Throws<InvalidDataException>(() => WavSampler.Create([loop]));
        }
        Assert.Throws<InvalidDataException>(() => WavSampler.Create([new(0, 0, 0, 1)], options: new() { MaximumLoops = 0 }));
        Assert.Throws<InvalidDataException>(() => WavSampler.Create([], options: new() { MaximumInputBytes = 35 }));
        Assert.Throws<InvalidDataException>(() => WavFile.Create([0], 1, 22050, new() { Sampler = WavSampler.Create([new(0, 0, 0, 2)]) }));
        byte[] good = WavSampler.Create([new(0, 0, 0, 8)]).RawData.ToArray();
        foreach (int field in new[] { 28, 32, 44, 48 })
        {
            byte[] invalid = (byte[])good.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(field), uint.MaxValue);
            Assert.Throws<InvalidDataException>(() => WavFile.Parse(WavFixture.Envelope(("fmt ", WavFixture.Format()), ("data", new byte[16]), ("smpl", invalid))));
        }
        Assert.Throws<InvalidDataException>(() => WavFile.Parse(WavFixture.Envelope(("fmt ", WavFixture.Format()), ("data", []), ("smpl", new byte[35]))));
    }
}
