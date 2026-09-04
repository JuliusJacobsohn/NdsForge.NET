using System.Buffers.Binary;
using NdsForge.Nitro.Audio;

namespace NdsForge.Nitro.Tests;

public sealed class NitroWaveEncoderTests
{
    [Theory]
    [InlineData(NitroAdpcmClipping.NintendoDs)]
    [InlineData(NitroAdpcmClipping.Signed16)]
    public void EveryStepIndexChoosesLowestErrorAndLowestCodeOnTies(NitroAdpcmClipping clipping)
    {
        short[] values = [short.MinValue, -32767, -16000, -1000, -1, 0, 1, 1000, 16000, short.MaxValue];
        byte[] block = new byte[5];
        for (int index = 0; index <= 88; index++)
        {
            foreach (short predictor in values)
            {
                BinaryPrimitives.WriteInt16LittleEndian(block, predictor);
                block[2] = (byte)index;
                short[] candidates = new short[16];
                for (byte code = 0; code < 16; code++)
                {
                    block[4] = code;
                    candidates[code] = NitroWaveCodec.Decode(block, NitroWaveEncoding.ImaAdpcm, 1,
                        new() { AdpcmClipping = clipping })[0];
                }
                foreach (short target in values)
                {
                    byte[] encoded = NitroWaveCodec.Encode([target], NitroWaveEncoding.ImaAdpcm,
                        new() { InitialPredictor = predictor, InitialStepIndex = index, AdpcmClipping = clipping });
                    int expected = Enumerable.Range(0, 16).MinBy(code => Math.Abs((int)target - candidates[code]));
                    Assert.Equal(expected, encoded[4]);
                    Assert.Equal(predictor, BinaryPrimitives.ReadInt16LittleEndian(encoded));
                    Assert.Equal(index, encoded[2]);
                    Assert.Equal(0, encoded[3]);
                }
            }
        }
    }

    [Fact]
    public void ContinuousRampTracksInputWithinItsSampleIncrement()
    {
        short[] input = Enumerable.Range(0, 1024).Select(i => (short)(-16000 + i * 31)).ToArray();
        byte[] encoded = NitroWaveCodec.Encode(input, NitroWaveEncoding.ImaAdpcm, new() { InitialStepIndex = 20 });
        short[] decoded = NitroWaveCodec.Decode(encoded, NitroWaveEncoding.ImaAdpcm);
        for (int i = 0; i < input.Length; i++) { Assert.InRange(Math.Abs((int)input[i] - decoded[i]), 0, 31); }
    }
}
