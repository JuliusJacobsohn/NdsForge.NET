using System.Buffers.Binary;

namespace NdsForge.Audio.Wav.Tests;

public sealed class WavSafetyTests
{
    [Fact]
    public void TruncationsImpossibleLengthsAndAmbiguousChunksFail()
    {
        byte[] good = WavFile.Create([1, 2, 3, 4], 2, 22050).WritePreserved();
        for (int length = 0; length < good.Length; length++)
        {
            Assert.Throws<InvalidDataException>(() => WavFile.Parse(good.AsSpan(0, length)));
        }
        foreach (int offset in new[] { 4, 16, 40 })
        {
            byte[] invalid = (byte[])good.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(offset), uint.MaxValue);
            Assert.Throws<InvalidDataException>(() => WavFile.Parse(invalid));
        }
        byte[] emptyRiff = WavFixture.Envelope(); Assert.Throws<InvalidDataException>(() => WavFile.Parse(emptyRiff));
        byte[] tail = [.. good, 1]; BinaryPrimitives.WriteInt32LittleEndian(tail.AsSpan(4), tail.Length - 8);
        Assert.Throws<InvalidDataException>(() => WavFile.Parse(tail));
        Assert.Throws<InvalidDataException>(() => WavFile.Parse(WavFixture.Envelope(("fmt ", WavFixture.Format()), ("data", [1]))));
        foreach (string duplicate in new[] { "fmt ", "data", "smpl" })
        {
            byte[] sampler = WavSampler.Create([]).RawData.ToArray();
            byte[] payload = duplicate switch { "fmt " => WavFixture.Format(), "smpl" => sampler, _ => [] };
            byte[] invalid = WavFixture.Envelope(("fmt ", WavFixture.Format()), ("data", []), ("smpl", sampler), (duplicate, payload));
            Assert.Throws<InvalidDataException>(() => WavFile.Parse(invalid));
        }
    }

    [Theory]
    [InlineData(0, 3, true)]
    [InlineData(2, 0, true)]
    [InlineData(2, 3, true)]
    [InlineData(4, 0, false)]
    [InlineData(8, 0, false)]
    [InlineData(12, 1, false)]
    [InlineData(14, 24, true)]
    public void InvalidOrUnsupportedFormatFieldsFail(int offset, ushort value, bool unsupported)
    {
        byte[] format = WavFixture.Format(); BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(offset), value);
        byte[] input = WavFixture.Envelope(("fmt ", format), ("data", []));
        if (unsupported) { Assert.Throws<NotSupportedException>(() => WavFile.Parse(input)); }
        else { Assert.Throws<InvalidDataException>(() => WavFile.Parse(input)); }
    }

    [Fact]
    public void FormatExtensionsAndSpeakerAssignmentsAreValidated()
    {
        byte[] standard = WavFixture.Format();
        foreach (int length in new[] { 0, 15, 17 })
        {
            Assert.Throws<InvalidDataException>(() => WavFile.Parse(WavFixture.Envelope(("fmt ", new byte[length]), ("data", []))));
        }
        Assert.Equal(0, WavFile.Parse(WavFixture.Envelope(("data", []), ("fmt ", [.. standard, 255, 255]))).FrameCount);
        byte[] extended = WavFile.Create([], 2, 22050, new() { UseExtensibleFormat = true }).Chunks[0].Data.ToArray();
        foreach (int length in new[] { 16, 18, 39 })
        {
            Assert.Throws<InvalidDataException>(() => WavFile.Parse(WavFixture.Envelope(("fmt ", extended[..length]), ("data", []))));
        }
        foreach (var (offset, value, unsupported) in new[] { (16, 21, false), (16, 23, false), (18, 0, false), (18, 17, false), (18, 15, true), (20, 4, false), (24, 3, true) })
        {
            byte[] format = (byte[])extended.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(offset), (ushort)value);
            byte[] input = WavFixture.Envelope(("fmt ", format), ("data", []));
            if (unsupported) { Assert.Throws<NotSupportedException>(() => WavFile.Parse(input)); }
            else { Assert.Throws<InvalidDataException>(() => WavFile.Parse(input)); }
        }
        byte[] extension = [.. extended, 5, 6]; BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(16), 24);
        Assert.Equal(42, WavFile.Parse(WavFixture.Envelope(("fmt ", extension), ("data", []))).Chunks[0].Data.Length);
    }

    [Fact]
    public void AllocationPoliciesRejectBeforeCopyingOrDecoding()
    {
        WavFile file = WavFile.Create([0, 1, 2, 3], 2, 22050);
        byte[] bytes = file.WritePreserved();
        Assert.Throws<InvalidDataException>(() => WavFile.Parse(bytes, new() { MaximumInputBytes = bytes.Length - 1 }));
        Assert.Throws<InvalidDataException>(() => WavFile.Parse(bytes, new() { MaximumSampleValues = 3 }));
        Assert.Throws<InvalidDataException>(() => WavFile.Parse(bytes, new() { MaximumChunks = 1 }));
        Assert.Throws<InvalidDataException>(() => file.Decode(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.Decode(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.Decode(int.MaxValue));
        foreach (WavReadOptions limits in new WavReadOptions[] { new() { MaximumInputBytes = -1 }, new() { MaximumInputBytes = int.MaxValue },
            new() { MaximumSampleValues = -1 }, new() { MaximumSampleValues = int.MaxValue }, new() { MaximumLoops = -1 }, new() { MaximumChunks = -1 } })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WavFile.Parse(bytes, limits));
        }
        Assert.Equal(4, WavFile.Parse(bytes, new() { MaximumInputBytes = bytes.Length, MaximumSampleValues = 4, MaximumChunks = 2 }).Decode(4).Length);
    }

    [Fact]
    public void WriterRejectsInconsistentOptionsAndOutputLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WavFile.Create([], 0, 22050));
        Assert.Throws<ArgumentOutOfRangeException>(() => WavFile.Create([], 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WavFile.Create([], 2, uint.MaxValue));
        Assert.Throws<ArgumentException>(() => WavFile.Create([1], 2, 22050));
        Assert.Throws<ArgumentOutOfRangeException>(() => WavFile.Create([], 1, 22050, new() { Encoding = (WavPcmEncoding)9 }));
        Assert.Throws<ArgumentNullException>(() => WavFile.Create([], 1, 22050, new() { Limits = null! }));
        Assert.Throws<ArgumentException>(() => WavFile.Create([], 1, 22050, new() { ChannelMask = 4 }));
        Assert.Throws<InvalidDataException>(() => WavFile.Create([], 2, 22050, new() { UseExtensibleFormat = true, ChannelMask = 4 }));
        Assert.Throws<InvalidDataException>(() => WavFile.Create([], 1, 22050, new() { Limits = new() { MaximumInputBytes = 43 } }));
        Assert.Throws<InvalidDataException>(() => WavFile.Create([0], 1, 22050, new() { Limits = new() { MaximumSampleValues = 0 } }));
        Assert.Throws<InvalidDataException>(() => WavFile.Create([], 1, 22050, new() { Limits = new() { MaximumChunks = 1 } }));
        WavSampler sampler = WavSampler.Create([new(0, 0, 0, 1)]);
        Assert.Throws<InvalidDataException>(() => WavFile.Create([0], 1, 22050, new() { Sampler = sampler, Limits = new() { MaximumLoops = 0 } }));
        Assert.Throws<InvalidDataException>(() => WavFile.Parse(WavFile.Create([0], 1, 22050, new() { Sampler = sampler }).WritePreserved(), new() { MaximumLoops = 0 }));
    }

    [Fact]
    public void HeaderMutationsNeverEscapeBoundsOrProducePartialFrames()
    {
        byte[] source = WavFile.Create([1, 2, 3, 4], 2, 22050, new() { UseExtensibleFormat = true }).WritePreserved();
        for (int offset = 0; offset < 68; offset++)
        {
            foreach (byte value in new byte[] { 0, 1, 0x7F, 0xFF })
            {
                byte[] input = (byte[])source.Clone(); input[offset] = value;
                try
                {
                    WavFile file = WavFile.Parse(input);
                    Assert.Equal(input, file.WritePreserved()); Assert.Equal(file.FrameCount * file.ChannelCount, file.Decode().Length);
                }
                catch (InvalidDataException) { }
                catch (NotSupportedException) { }
            }
        }
    }
}
