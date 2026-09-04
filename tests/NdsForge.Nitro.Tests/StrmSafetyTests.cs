using NdsForge.Nitro.Audio;

namespace NdsForge.Nitro.Tests;

public sealed class StrmSafetyTests
{
    [Fact]
    public void HeaderMutationsEitherRemainBoundedAndPreservedOrReportInvalidData()
    {
        byte[] source = StrmFile.Create(new short[38], 2, NitroWaveEncoding.ImaAdpcm, 8000, new() { BlockByteLength = 8 }).WritePreserved();
        for (int offset = 0; offset < 104; offset++)
        {
            for (int value = 0; value < 256; value += 17)
            {
                byte[] bytes = (byte[])source.Clone(); bytes[offset] = (byte)value;
                try
                {
                    StrmFile file = StrmFile.Parse(bytes);
                    Assert.Equal(bytes, file.WritePreserved());
                    Assert.Equal(file.SampleValueCount, file.Decode().Length);
                }
                catch (InvalidDataException) { }
            }
        }
    }

    [Fact]
    public void TruncationBadEnvelopesAndUnboundedDeclarationsFailBeforeDecoding()
    {
        byte[] valid = StrmFile.Create(new short[14], 2, NitroWaveEncoding.ImaAdpcm, 8000, new() { BlockByteLength = 6 }).WritePreserved();
        for (int length = 0; length < valid.Length; length++)
        {
            byte[] prefix = valid[..length];
            Assert.Throws<InvalidDataException>(() => StrmFile.Parse(prefix));
        }
        foreach (int offset in new[] { 0, 4, 6, 8, 12, 14, 16, 20, 24, 26, 36, 40, 44, 48, 52, 56, 60, 96, 100, 106, 112, 118, 124 })
        {
            byte[] bad = (byte[])valid.Clone(); bad[offset] ^= 0xFF;
            Assert.Throws<InvalidDataException>(() => StrmFile.Parse(bad));
        }
        foreach (int offset in new[] { 8, 20, 36, 40, 44, 48, 52, 56, 60, 100 })
        {
            byte[] bad = (byte[])valid.Clone(); StrmFileTests.U32(bad, offset, uint.MaxValue);
            Assert.Throws<InvalidDataException>(() => StrmFile.Parse(bad, new() { MaximumBlocksPerChannel = int.MaxValue }));
        }
    }

    [Fact]
    public void InvalidRatesLoopsCountsAndTruncatedStatesFail()
    {
        byte[] valid = StrmFileTests.Fixture(NitroWaveEncoding.Pcm8, 1, 4, 4, [[1, 2, 3, 4]]);
        byte[] bad = (byte[])valid.Clone(); StrmFileTests.U16(bad, 28, 0);
        Assert.Throws<InvalidDataException>(() => StrmFile.Parse(bad));
        bad = (byte[])valid.Clone(); bad[25] = 1; StrmFileTests.U32(bad, 32, 4);
        Assert.Throws<InvalidDataException>(() => StrmFile.Parse(bad));
        bad = (byte[])valid.Clone(); StrmFileTests.U32(bad, 44, 0);
        Assert.Throws<InvalidDataException>(() => StrmFile.Parse(bad));
        bad = StrmFileTests.Fixture(NitroWaveEncoding.Pcm8, 1, 0, 0, [[1], [1]]);
        Assert.Throws<InvalidDataException>(() => StrmFile.Parse(bad));
        foreach (NitroWaveEncoding encoding in Enum.GetValues<NitroWaveEncoding>())
        {
            bad = StrmFileTests.Fixture(encoding, 1, 8, 8, [[0, 0, 0]]);
            Assert.Throws<InvalidDataException>(() => StrmFile.Parse(bad));
        }
        bad = StrmFileTests.Fixture(NitroWaveEncoding.ImaAdpcm, 1, 8, 8, [[0, 0, 0, 0]]);
        StrmFileTests.U32(bad, 56, 4);
        Assert.Throws<InvalidDataException>(() => StrmFile.Parse(bad));
    }

    [Fact]
    public void ParsingDecodingAndBlockAccessEnforceIndependentLimits()
    {
        StrmFile file = StrmFile.Create(new short[14], 2, NitroWaveEncoding.Pcm8, 8000, new() { BlockByteLength = 4 });
        byte[] bytes = file.WritePreserved();
        Assert.Throws<InvalidDataException>(() => StrmFile.Parse(bytes, new() { MaximumInputBytes = bytes.Length - 1 }));
        Assert.Throws<InvalidDataException>(() => StrmFile.Parse(bytes, new() { MaximumSampleValues = 13 }));
        Assert.Throws<InvalidDataException>(() => StrmFile.Parse(bytes, new() { MaximumBlocksPerChannel = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrmFile.Parse(bytes, new() { MaximumInputBytes = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrmFile.Parse(bytes, new() { MaximumInputBytes = int.MaxValue }));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrmFile.Parse(bytes, new() { MaximumSampleValues = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrmFile.Parse(bytes, new() { MaximumSampleValues = int.MaxValue }));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrmFile.Parse(bytes, new() { MaximumBlocksPerChannel = -1 }));
        Assert.Throws<InvalidDataException>(() => file.Decode(new() { MaximumSamples = 13 }));
        Assert.Throws<InvalidDataException>(() => file.DecodeChannel(0, new() { MaximumSamples = 6 }));
        Assert.Equal(7, file.DecodeChannel(0, new() { MaximumSamples = 7 }).Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => file.Decode(new() { AdpcmClipping = (NitroAdpcmClipping)5 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.DecodeChannel(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.DecodeChannel(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.GetBlock(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.GetBlock(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.GetBlock(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.GetBlock(0, 2));
    }

    [Fact]
    public void CreationRejectsInvalidGeometryTimingLoopsStateAndResourceRequests()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StrmFile.Create([], 0, NitroWaveEncoding.Pcm8, 8000));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrmFile.Create([], 1, (NitroWaveEncoding)9, 8000));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrmFile.Create([], 1, NitroWaveEncoding.Pcm8, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrmFile.Create([], 1, NitroWaveEncoding.Pcm8, 1));
        Assert.Throws<ArgumentException>(() => StrmFile.Create([1], 2, NitroWaveEncoding.Pcm8, 8000));
        foreach (StrmCreateOptions option in new StrmCreateOptions[]
        {
            new() { BlockByteLength = 0 }, new() { BlockByteLength = 3 }, new() { BlockByteLength = int.MaxValue },
            new() { LoopStartSample = -1 }, new() { LoopStartSample = 8 }, new() { SampleEncoding = new() { InitialStepIndex = 89 } },
        })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StrmFile.Create(new short[8], 1, NitroWaveEncoding.ImaAdpcm, 8000, option));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => StrmFile.Create([], 1, NitroWaveEncoding.Pcm16, 8000, new() { BlockByteLength = 3 }));
        foreach (StrmCreateOptions option in new StrmCreateOptions[]
        {
            new() { Limits = new() { MaximumSampleValues = 7 } }, new() { Limits = new() { MaximumInputBytes = 104 } },
            new() { BlockByteLength = 5, Limits = new() { MaximumBlocksPerChannel = 1 } }, new() { SampleEncoding = new() { MaximumOutputBytes = 1 } },
        })
        {
            Assert.Throws<InvalidDataException>(() => StrmFile.Create(new short[8], 1, NitroWaveEncoding.ImaAdpcm, 8000, option));
        }
        Assert.Throws<ArgumentNullException>(() => StrmFile.Create([], 1, NitroWaveEncoding.Pcm8, 8000, new() { Limits = null! }));
        Assert.Throws<ArgumentNullException>(() => StrmFile.Create([], 1, NitroWaveEncoding.Pcm8, 8000, new() { SampleEncoding = null! }));
    }

    [Fact]
    public void BuilderRejectsInvalidSlotsStateLoopsRatesAndOutputBounds()
    {
        StrmFile file = StrmFile.Create(new short[8], 1, NitroWaveEncoding.ImaAdpcm, 8000);
        StrmFileBuilder builder = file.CreateBuilder();
        Assert.Throws<ArgumentException>(() => builder.ReplaceBlock(0, 0, [0]));
        Assert.Throws<InvalidDataException>(() => builder.ReplaceBlock(0, 0, [0, 0, 89, 0, 0, 0, 0, 0]));
        Assert.Throws<InvalidDataException>(() => builder.Build(new() { MaximumOutputBytes = 0 }));
        Assert.Throws<InvalidDataException>(() => builder.Build(new() { MaximumOutputBytes = 0, PreserveSourceLayout = false }));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(new() { MaximumOutputBytes = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(new() { MaximumOutputBytes = int.MaxValue }));
        builder.SampleRate = 0;
        Assert.Throws<InvalidDataException>(() => builder.Build());
        builder.SampleRate = 8000; builder.RawLoopFlag = 1; builder.RawLoopStartSample = 8;
        Assert.Throws<InvalidDataException>(() => builder.Build());
        Assert.Equal(file.WritePreserved(), file.CreateBuilder().Build());
    }
}
