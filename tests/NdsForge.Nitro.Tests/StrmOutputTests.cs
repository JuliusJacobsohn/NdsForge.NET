using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Nitro.Audio;

namespace NdsForge.Nitro.Tests;

public sealed class StrmOutputTests
{
    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8, 0, "629148E09D4D9A8F4B3C7878BC8ACEB4155985372B46AD15F76FFA43C2017C94", "2A2E29B28C7BACBB3D1A20FEE7E6D4F6C9D6CCF52A6622B8B8785D6965931BC7")]
    [InlineData(NitroWaveEncoding.Pcm16, 0, "3AC508AF1123E401AEA417FEA9D75294F7BB5EF4B166D8820285F2806D8CA27C", "DB4DD72448626FAFABBC8310B4B992D876C31B2243D50CEA5E23FA46D8A9173F")]
    [InlineData(NitroWaveEncoding.ImaAdpcm, 20, "55C0F707277A238DB8AF6F052884CE0BA4F886C42DE7BF0B0C2258941280A35B", "9A475610F5094442B20D0FFD9481661EE43C8DAE42A014614977FF2F33D1922B")]
    public void EncodedStereoAndDecodedSamplesMatchFixedExpectations(NitroWaveEncoding encoding, int index, string storedHash, string sampleHash)
    {
        short[] input = Enumerable.Range(0, 262).Select(i => (short)(((i / 2 * 997 + i % 2 * 7919) % 60001) - 30000)).ToArray();
        StrmFile file = StrmFile.Create(input, 2, encoding, 22050,
            new() { BlockByteLength = 32, LoopStartSample = 130, SampleEncoding = new() { InitialStepIndex = index } });
        Assert.Equal(storedHash, Convert.ToHexString(SHA256.HashData(file.WritePreserved())));
        short[] decoded = file.Decode(new() { AdpcmClipping = NitroAdpcmClipping.Signed16 });
        byte[] pcm = new byte[decoded.Length * 2];
        for (int i = 0; i < decoded.Length; i++) { BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), decoded[i]); }
        Assert.Equal(sampleHash, Convert.ToHexString(SHA256.HashData(pcm)));
    }

    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8, 1)]
    [InlineData(NitroWaveEncoding.Pcm8, 2)]
    [InlineData(NitroWaveEncoding.Pcm16, 1)]
    [InlineData(NitroWaveEncoding.Pcm16, 2)]
    [InlineData(NitroWaveEncoding.ImaAdpcm, 1)]
    [InlineData(NitroWaveEncoding.ImaAdpcm, 2)]
    public void CreationRetainsExactDurationAndEncodesEachBlockIndependently(NitroWaveEncoding encoding, int channels)
    {
        foreach (int frames in new[] { 0, 1, 8, 16, 17, 128, 131 })
        {
            short[] input = Enumerable.Range(0, frames * channels).Select(i => (short)((i * 997 % 60001) - 30000)).ToArray();
            var options = new StrmCreateOptions { BlockByteLength = 16, LoopStartSample = frames > 0 ? frames - 1 : null };
            StrmFile file = StrmFile.Create(input, channels, encoding, 22050, options);
            Assert.Equal(frames, file.SampleCount);
            Assert.Equal(frames * channels, file.SampleValueCount);
            Assert.Equal(22050, file.SampleRate);
            Assert.Equal(23, file.Timer);
            Assert.Equal(options.LoopStartSample, file.LoopStartSample);
            Assert.Equal(frames > 0 ? frames : (int?)null, file.LoopEndSample);
            Assert.Equal(file.WritePreserved(), StrmFile.Create(input, channels, encoding, 22050, options).WritePreserved());
            Assert.Equal(file.WritePreserved(), file.CreateBuilder().Build(new() { PreserveSourceLayout = false }));
            Assert.Equal(input.Length, file.Decode().Length);
            if (encoding == NitroWaveEncoding.Pcm16) { Assert.Equal(input, file.Decode()); }
            if (encoding == NitroWaveEncoding.Pcm8) { Assert.Equal(input.Select(s => (short)((s >> 8) * 256)), file.Decode()); }
            int frame = 0;
            for (int b = 0; b < file.BlocksPerChannel; b++)
            {
                for (int c = 0; c < channels; c++)
                {
                    StrmBlock block = file.GetBlock(b, c);
                    short[] mono = Enumerable.Range(frame, block.SampleCount).Select(i => input[i * channels + c]).ToArray();
                    Assert.Equal(NitroWaveCodec.Encode(mono, encoding), block.EncodedData.ToArray());
                }
                frame += file.GetBlock(b, 0).SampleCount;
            }
            Assert.Equal(frames, frame);
        }
    }

    [Fact]
    public void ReplacementAndMetadataEditsAreDetachedAndDoNotChangeOtherSlots()
    {
        StrmFile original = StrmFile.Create(new short[14], 2, NitroWaveEncoding.Pcm8, 8000, new() { BlockByteLength = 4 });
        StrmFileBuilder builder = original.CreateBuilder();
        byte[] replacement = [1, 2, 3];
        builder.ReplaceBlock(1, 1, replacement);
        replacement[0] = 100;
        builder.SampleRate = 16000; builder.Timer = 765; builder.RawFlags = 0xA5; builder.RawLoopFlag = 7; builder.RawLoopStartSample = 3;
        StrmFile changed = StrmFile.Parse(builder.Build());
        Assert.Equal(16000, changed.SampleRate);
        Assert.Equal(765, changed.Timer);
        Assert.Equal(0xA5, changed.RawFlags);
        Assert.Equal(7, changed.RawLoopFlag);
        Assert.Equal(3, changed.LoopStartSample);
        Assert.Equal(7, changed.LoopEndSample);
        Assert.Equal(new short[] { 0, 0, 0, 0, 256, 512, 768 }, changed.DecodeChannel(1));
        Assert.Equal(new short[7], original.DecodeChannel(1));
        Assert.Equal(original.GetBlock(1, 0).EncodedData.ToArray(), changed.GetBlock(1, 0).EncodedData.ToArray());
    }

    [Fact]
    public void EveryExtensionAndPaddingRegionSurvivesPreservationButNotCanonicalEnvelope()
    {
        byte[] ordinary = StrmFile.Create(new short[4], 1, NitroWaveEncoding.Pcm8, 8000).WritePreserved();
        byte[] bytes = [.. ordinary[..16], 1, 2, 3, 4, .. ordinary[16..96], 5, 6, 7, 8, .. ordinary[96..104], 9, 10, 11, .. ordinary[104..], 12, 13, 14, 15, 16, 17, 18, 19, 20, 21];
        StrmFileTests.U16(bytes, 4, 0xFFFE); StrmFileTests.U16(bytes, 12, 20);
        StrmFileTests.U32(bytes, 8, (uint)bytes.Length - 2); StrmFileTests.U32(bytes, 24, 84);
        StrmFileTests.U32(bytes, 44, 115); StrmFileTests.U32(bytes, 108, (uint)(bytes.Length - 104 - 5));
        StrmFile file = StrmFile.Parse(bytes);
        Assert.Equal(20, file.HeaderLength);
        Assert.Equal(84, file.InformationBlockLength);
        Assert.Equal(36, file.ReservedMetadata.Length);
        Assert.Equal(0xFFFE, file.ByteOrderMarker);
        Assert.Equal(bytes.Length - 2, file.DeclaredLength);
        Assert.Equal(bytes, file.CreateBuilder().Build());
        Assert.Equal(ordinary, file.CreateBuilder().Build(new() { PreserveSourceLayout = false }));
        StrmFileBuilder builder = file.CreateBuilder();
        builder.ReplaceBlock(0, 0, [1, 2, 3, 4]);
        byte[] changed = builder.Build();
        Assert.Equal(bytes[..115], changed[..115]);
        Assert.Equal(bytes[119..], changed[119..]);
    }

    [Fact]
    public void LowRatesExplicitStateAndNonWordAlignedBlocksRemainRepresentable()
    {
        var options = new StrmCreateOptions { BlockByteLength = 5, Timer = 0, SampleEncoding = new() { InitialPredictor = -1234, InitialStepIndex = 88 } };
        StrmFile file = StrmFile.Create(new short[7], 1, NitroWaveEncoding.ImaAdpcm, 1, options);
        Assert.Equal(4, file.BlocksPerChannel);
        Assert.Equal(7, file.Decode().Length);
        Assert.Equal(new byte[] { 0x2E, 0xFB, 88, 0 }, file.GetBlock(3, 0).EncodedData.Span[..4].ToArray());
        Assert.Equal(0, file.Timer);
    }
}
