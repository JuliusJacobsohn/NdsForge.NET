using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NdsForge.Audio.Wav.Tests;

public sealed class WavFileTests
{
    [Fact]
    public void CanonicalPcmMatrixMatchesWholeFileAndSampleIdentities()
    {
        using var framing = new MemoryStream();
        using var writer = new BinaryWriter(framing);
        foreach (int bits in new[] { 8, 16 })
        {
            foreach (int channels in new[] { 1, 2 })
            {
                foreach (int frames in new[] { 1, 8, 128, 131 })
                {
                    short[] samples = WavFixture.Samples(bits, channels, frames);
                    WavFile file = WavFile.Create(samples, channels, 22050,
                        new() { Encoding = bits == 8 ? WavPcmEncoding.Unsigned8 : WavPcmEncoding.Signed16 });
                    Assert.Equal(samples, file.Decode());
                    Assert.Equal(frames, file.FrameCount); Assert.Equal(channels * frames, file.SampleValueCount);
                    Assert.Equal((uint)(22050 * channels * bits / 8), file.AverageBytesPerSecond);
                    Assert.False(file.IsExtensible); Assert.False(file.HasOmittedFinalPadding);
                    Assert.Equal((ushort)1, file.FormatTag); Assert.Equal((ushort)bits, file.ValidBitsPerSample);
                    writer.Write((ushort)bits); writer.Write((ushort)channels); writer.Write(frames);
                    writer.Write(SHA256.HashData(file.WritePreserved()));
                    writer.Write(SHA256.HashData(WavFixture.PcmBytes(file.Decode())));
                }
            }
        }
        Assert.Equal(1152, framing.Length);
        Assert.Equal("56F498ED217DC475568EDB051250889274AEE512FF071F3CBBF04DAF1BB567FF", Convert.ToHexString(SHA256.HashData(framing.ToArray())));
    }

    [Theory]
    [InlineData(8, 1)]
    [InlineData(8, 2)]
    [InlineData(16, 1)]
    [InlineData(16, 2)]
    public void ExtensiblePcmRetainsCompletePrecisionAndSpeakerMask(int bits, int channels)
    {
        foreach (int frames in new[] { 1, 8, 128, 131 })
        {
            foreach (uint mask in new[] { 0u, channels == 1 ? 4u : 3u })
            {
                short[] samples = WavFixture.Samples(bits, channels, frames);
                WavFile file = WavFile.Create(samples, channels, 22050, new()
                {
                    Encoding = bits == 8 ? WavPcmEncoding.Unsigned8 : WavPcmEncoding.Signed16,
                    UseExtensibleFormat = true,
                    ChannelMask = mask
                });
                Assert.True(file.IsExtensible); Assert.Equal((ushort)0xFFFE, file.FormatTag);
                Assert.Equal(mask, file.ChannelMask); Assert.Equal((ushort)bits, file.ValidBitsPerSample);
                Assert.Equal(40, file.Chunks[0].Data.Length); Assert.Equal(samples, file.Decode());
                Assert.Equal(file.WritePreserved(), WavFile.Parse(file.WritePreserved()).WritePreserved());
            }
        }
    }

    [Fact]
    public void UnknownChunksOrderPaddingAndOuterBytesArePreserved()
    {
        short[] samples = WavFixture.Samples(16, 2, 8);
        WavSampler sampler = WavSampler.Create([new(17, 0, 3, 8)], samplerData: [9, 8, 7]);
        byte[] input = WavFixture.Envelope(("JUNK", [1, 2, 3]), ("data", WavFixture.PcmBytes(samples)),
            ("smpl", sampler.RawData.ToArray()), ("fmt ", WavFixture.Format(2)), ("JUNK", [4]), ("\u00FFabc", []));
        byte[] outer = [.. input, 0x55, 0x66];
        WavFile file = WavFile.Parse(outer); outer[0] = 0;
        Assert.Equal<string>(["JUNK", "data", "smpl", "fmt ", "JUNK", "\u00FFabc"], file.Chunks.Select(chunk => chunk.Name));
        Assert.Equal((byte)0xA7, file.Chunks[0].PaddingByte); Assert.Null(file.Chunks[1].PaddingByte);
        Assert.Equal(12, file.Chunks[0].Offset); Assert.Equal(0x4B4E554Au, file.Chunks[0].Identifier);
        Assert.Equal(input.Length, file.DeclaredLength); Assert.Equal(samples, file.Decode());
        Assert.Equal(new byte[] { 9, 8, 7 }, file.Sampler!.SamplerData.ToArray());
        Assert.Equal((byte)'R', file.WritePreserved()[0]);
        byte[] copy = file.WritePreserved(); copy[12] = 0;
        Assert.Equal((byte)'J', file.WritePreserved()[12]);
        Assert.Equal(new byte[] { 0x55, 0x66 }, file.WritePreserved()[input.Length..]);
    }

    [Fact]
    public void MissingFinalPadRequiresCompatibilityPolicyAndRemainsLossless()
    {
        byte[] canonical = WavFile.Create([short.MinValue], 1, 22050, new() { Encoding = WavPcmEncoding.Unsigned8 }).WritePreserved();
        byte[] unpadded = canonical[..^1]; BinaryPrimitives.WriteInt32LittleEndian(unpadded.AsSpan(4), unpadded.Length - 8);
        WavFile file = WavFile.Parse(unpadded);
        Assert.True(file.HasOmittedFinalPadding); Assert.Null(file.Chunks[1].PaddingByte);
        Assert.Equal(unpadded, file.WritePreserved()); Assert.Equal(new[] { short.MinValue }, file.Decode());
        Assert.Throws<InvalidDataException>(() => WavFile.Parse(unpadded, new() { AllowMissingFinalPadding = false }));
        Assert.Equal((byte)0, WavFile.Parse(canonical).Chunks[1].PaddingByte);
    }

    [Fact]
    public void Pcm8QuantizationAndEmptyWavAreExplicit()
    {
        short[] samples = [short.MinValue, -32767, -257, -256, -1, 0, 255, 256, short.MaxValue];
        WavFile file = WavFile.Create(samples, 1, 1, new() { Encoding = WavPcmEncoding.Unsigned8 });
        Assert.Equal(new byte[] { 0, 0, 126, 127, 127, 128, 128, 129, 255 }, file.EncodedSamples.ToArray());
        Assert.Equal(samples.Select(value => (short)((value >> 8) * 256)), file.Decode());
        WavFile empty = WavFile.Create([], 2, 48000);
        Assert.Empty(empty.Decode()); Assert.Equal(44, empty.DeclaredLength); Assert.Null(empty.Sampler);
    }
}
