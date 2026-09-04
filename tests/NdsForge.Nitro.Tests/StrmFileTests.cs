using System.Buffers.Binary;
using NdsForge.Nitro.Audio;

namespace NdsForge.Nitro.Tests;

public sealed class StrmFileTests
{
    [Fact]
    public void StereoBlocksDecodeInFrameOrderWithIndependentStatesAndOddFinalCounts()
    {
        byte[] bytes = Fixture(NitroWaveEncoding.ImaAdpcm, 2, 4, 3,
            [[100, 0, 0, 0, 0x11, 0x11], [156, 255, 0, 0, 0x99, 0x99], [200, 0, 0, 0, 0x11, 0xF1], [56, 255, 0, 0, 0x99, 0x09]]);
        StrmFile file = StrmFile.Parse(bytes);
        Assert.Equal(new short[] { 101, -101, 102, -102, 103, -103, 104, -104, 201, -201, 202, -202, 203, -203 }, file.Decode());
        Assert.Equal(new short[] { 101, 102, 103, 104, 201, 202, 203 }, file.DecodeChannel(0));
        Assert.Equal(new short[] { -101, -102, -103, -104, -201, -202, -203 }, file.DecodeChannel(1));
        Assert.Equal(7, file.SampleCount);
        Assert.Equal(14, file.SampleValueCount);
        Assert.Equal(2, file.BlocksPerChannel);
        Assert.Equal(104, file.DataOffset);
        Assert.Equal(96, file.DataBlockOffset);
        Assert.Equal(bytes.Length - 96, file.DataBlockLength);
        Assert.Equal(32, file.ReservedMetadata.Length);
        Assert.Equal(6u, file.NormalBlockByteLength);
        Assert.Equal(4u, file.NormalBlockSampleCount);
        Assert.Equal(6, file.FinalBlockByteLength);
        Assert.Equal(3, file.FinalBlockSampleCount);
        Assert.Equal(122, file.GetBlock(1, 1).Offset);
        Assert.Equal(1, file.GetBlock(1, 1).BlockIndex);
        Assert.Equal(1, file.GetBlock(1, 1).ChannelIndex);
        Assert.Equal(3, file.GetBlock(1, 1).SampleCount);
        Assert.Equal(file.GetBlock(0, 0), file.GetBlock(0, 0));
        Assert.NotEqual(file.GetBlock(0, 0), file.GetBlock(0, 1));
        Assert.False(file.ExcludesAdpcmStateHeaderFromLength);
        Assert.Equal(bytes, file.WritePreserved());
    }

    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8)]
    [InlineData(NitroWaveEncoding.Pcm16)]
    public void UnalignedFinalStereoBlocksAreAdjacentAndSigned(NitroWaveEncoding encoding)
    {
        byte[][] blocks = encoding == NitroWaveEncoding.Pcm8 ? [[1, 2, 3], [255, 254, 253]] : [[1, 0, 2, 0, 3, 0], [255, 255, 254, 255, 253, 255]];
        StrmFile file = StrmFile.Parse(Fixture(encoding, 2, 3, 3, blocks));
        int scale = encoding == NitroWaveEncoding.Pcm8 ? 256 : 1;
        Assert.Equal(new short[] { (short)scale, (short)-scale, (short)(2 * scale), (short)(-2 * scale), (short)(3 * scale), (short)(-3 * scale) }, file.Decode());
        Assert.Equal(104 + blocks[0].Length, file.GetBlock(0, 1).Offset);
    }

    [Fact]
    public void LegacySingleBlockLengthsIncludeStateOnlyInTheExposedSlice()
    {
        byte[] bytes = Fixture(NitroWaveEncoding.ImaAdpcm, 1, 3, 3, [[100, 0, 0, 0, 0x11, 0xF1]]);
        U32(bytes, 48, 2);
        U32(bytes, 56, 2);
        StrmFile file = StrmFile.Parse(bytes);
        Assert.True(file.ExcludesAdpcmStateHeaderFromLength);
        Assert.Equal(2, file.FinalBlockByteLength);
        Assert.Equal(6, file.GetBlock(0, 0).EncodedData.Length);
        Assert.Equal(new short[] { 101, 102, 103 }, file.Decode());
        Assert.Equal(bytes, file.CreateBuilder().Build());
        StrmFile canonical = StrmFile.Parse(file.CreateBuilder().Build(new() { PreserveSourceLayout = false }));
        Assert.False(canonical.ExcludesAdpcmStateHeaderFromLength);
        Assert.Equal(6u, canonical.NormalBlockByteLength);
        Assert.Equal(3u, canonical.NormalBlockSampleCount);
        Assert.Equal(6, canonical.FinalBlockByteLength);
        Assert.Equal(file.Decode(), canonical.Decode());
    }

    [Theory]
    [InlineData(NitroWaveEncoding.Pcm8)]
    [InlineData(NitroWaveEncoding.Pcm16)]
    [InlineData(NitroWaveEncoding.ImaAdpcm)]
    public void EmptyFinalBlocksDoNotAddOrResetSamples(NitroWaveEncoding encoding)
    {
        byte[] nonempty = NitroWaveCodec.Encode(new short[] { 256, 512 }, encoding);
        StrmFile file = StrmFile.Parse(Fixture(encoding, 1, 2, 0, [nonempty, []]));
        Assert.Equal(2, file.SampleCount);
        Assert.Equal(2, file.Decode().Length);
        Assert.Empty(file.GetBlock(1, 0).EncodedData.ToArray());
    }

    [Fact]
    public void InactiveRawLoopAndUnusedBlockDeclarationsStayLossless()
    {
        byte[] bytes = Fixture(NitroWaveEncoding.Pcm8, 1, 1, 1, [[1]]);
        U32(bytes, 32, uint.MaxValue);
        U32(bytes, 48, uint.MaxValue);
        U32(bytes, 52, uint.MaxValue);
        bytes[27] = 0xEF;
        StrmFile file = StrmFile.Parse(bytes);
        Assert.False(file.IsLooping);
        Assert.Null(file.LoopStartSample);
        Assert.Null(file.LoopEndSample);
        Assert.Equal(uint.MaxValue, file.RawLoopStartSample);
        Assert.Equal(uint.MaxValue, file.NormalBlockByteLength);
        Assert.Equal(0xEF, file.RawFlags);
        Assert.Equal(new short[] { 256 }, file.Decode());
        bytes[104] = 2;
        Assert.Equal(1, file.WritePreserved()[104]);
    }

    internal static byte[] Fixture(NitroWaveEncoding encoding, int channels, int normalSamples, int finalSamples, byte[][] blocks)
    {
        int count = blocks.Length / channels;
        int payload = blocks.Sum(b => b.Length);
        byte[] bytes = new byte[(104 + payload + 3) & ~3];
        "STRM"u8.CopyTo(bytes);
        U16(bytes, 4, 0xFEFF); U16(bytes, 6, 0x0100); U32(bytes, 8, (uint)bytes.Length); U16(bytes, 12, 16); U16(bytes, 14, 2);
        "HEAD"u8.CopyTo(bytes.AsSpan(16)); U32(bytes, 20, 80);
        bytes[24] = (byte)encoding; bytes[26] = (byte)channels;
        U16(bytes, 28, 22050); U16(bytes, 30, 23);
        U32(bytes, 36, (uint)((count - 1) * normalSamples + finalSamples)); U32(bytes, 40, 104); U32(bytes, 44, (uint)count);
        U32(bytes, 48, (uint)blocks[0].Length); U32(bytes, 52, (uint)normalSamples);
        U32(bytes, 56, (uint)blocks[^1].Length); U32(bytes, 60, (uint)finalSamples);
        "DATA"u8.CopyTo(bytes.AsSpan(96)); U32(bytes, 100, (uint)(bytes.Length - 96));
        int offset = 104;
        foreach (byte[] block in blocks) { block.CopyTo(bytes, offset); offset += block.Length; }
        return bytes;
    }

    internal static void U32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    internal static void U16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
}
