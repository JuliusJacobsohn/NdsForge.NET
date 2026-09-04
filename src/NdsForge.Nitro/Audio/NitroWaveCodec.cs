using System.Buffers.Binary;

namespace NdsForge.Nitro.Audio;

/// <summary>Converts native mono sample blocks without SWAV, STRM, WAV, resampling, or playback dependencies.</summary>
public static class NitroWaveCodec
{
    /// <summary>Decodes signed PCM or one header-prefixed DS ADPCM block into signed sixteen-bit sample values.</summary>
    /// <param name="data">A complete raw block, including the four-byte state header for ADPCM.</param>
    /// <param name="encoding">Native sample representation.</param>
    /// <param name="sampleCount">Optional meaningful prefix length, excluding encoded padding and the ADPCM state header.</param>
    /// <param name="options">Output sample limit and ADPCM clipping policy.</param>
    /// <returns>Exactly the requested sample count, or every encoded sample when no count is specified.</returns>
    public static short[] Decode(ReadOnlySpan<byte> data, NitroWaveEncoding encoding, int? sampleCount = null,
        NitroWaveDecodeOptions? options = null)
    {
        options ??= new();
        options.Validate();
        if (!Enum.IsDefined(encoding)) { throw new ArgumentOutOfRangeException(nameof(encoding)); }
        if (encoding == NitroWaveEncoding.Pcm16 && data.Length % 2 != 0) { throw new InvalidDataException("PCM16 requires complete two-byte samples."); }
        if (encoding == NitroWaveEncoding.ImaAdpcm && data.Length < 4) { throw new InvalidDataException("An ADPCM block requires a four-byte state header."); }
        long available = encoding switch
        {
            NitroWaveEncoding.Pcm8 => data.Length,
            NitroWaveEncoding.Pcm16 => data.Length / 2,
            _ => (data.Length - 4L) * 2,
        };
        long count = sampleCount ?? available;
        if (count < 0 || count > available) { throw new ArgumentOutOfRangeException(nameof(sampleCount), "The requested samples exceed the complete encoded block."); }
        if (count > options.MaximumSamples) { throw new InvalidDataException("The decoded sample limit was exceeded."); }
        int predictor = encoding == NitroWaveEncoding.ImaAdpcm ? BinaryPrimitives.ReadInt16LittleEndian(data) : 0;
        int index = encoding == NitroWaveEncoding.ImaAdpcm ? data[2] & 127 : 0;
        if (index > 88) { throw new InvalidDataException("The ADPCM initial index exceeds eighty-eight."); }
        short[] result = new short[(int)count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = encoding switch
            {
                NitroWaveEncoding.Pcm8 => (short)(unchecked((sbyte)data[i]) * 256),
                NitroWaveEncoding.Pcm16 => BinaryPrimitives.ReadInt16LittleEndian(data.Slice(i * 2, 2)),
                _ => DecodeNibble(data[4 + (i / 2)], i, ref predictor, ref index, options.AdpcmClipping),
            };
        }
        return result;
    }

    /// <summary>Encodes raw mono samples deterministically; PCM8 discards the low byte, while ADPCM minimizes each next-sample error.</summary>
    /// <param name="samples">Signed sixteen-bit source values in playback order.</param>
    /// <param name="encoding">Native output representation.</param>
    /// <param name="options">Output bounds and optional ADPCM state; state properties do not affect PCM.</param>
    /// <returns>Raw encoded bytes, with an ADPCM state header and zero unused high nibble for odd sample counts.</returns>
    public static byte[] Encode(ReadOnlySpan<short> samples, NitroWaveEncoding encoding, NitroWaveEncodeOptions? options = null)
    {
        options ??= new();
        options.Validate();
        if (!Enum.IsDefined(encoding)) { throw new ArgumentOutOfRangeException(nameof(encoding)); }
        long length = encoding switch
        {
            NitroWaveEncoding.Pcm8 => samples.Length,
            NitroWaveEncoding.Pcm16 => samples.Length * 2L,
            _ => 4 + ((samples.Length + 1L) / 2),
        };
        if (length > options.MaximumOutputBytes) { throw new InvalidDataException("The encoded byte limit was exceeded."); }
        byte[] output = new byte[(int)length];
        int predictor = options.InitialPredictor ?? (samples.IsEmpty ? 0 : samples[0]);
        int index = options.InitialStepIndex;
        if (encoding == NitroWaveEncoding.ImaAdpcm)
        {
            BinaryPrimitives.WriteInt16LittleEndian(output, (short)predictor);
            output[2] = (byte)index;
        }
        for (int i = 0; i < samples.Length; i++)
        {
            if (encoding == NitroWaveEncoding.Pcm8) { output[i] = unchecked((byte)(samples[i] >> 8)); }
            else if (encoding == NitroWaveEncoding.Pcm16) { BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(i * 2), samples[i]); }
            else
            {
                int code = NitroAdpcmMath.ChooseCode(samples[i], predictor, index, options.AdpcmClipping);
                output[4 + (i / 2)] |= (byte)(code << ((i & 1) * 4));
                predictor = NitroAdpcmMath.Advance(code, predictor, ref index, options.AdpcmClipping);
            }
        }
        return output;
    }

    /// <summary>Decodes the low nibble before the high nibble without emitting the initial predictor as a sample.</summary>
    private static short DecodeNibble(byte pair, int sample, ref int predictor, ref int index, NitroAdpcmClipping clipping)
    {
        int code = (pair >> ((sample & 1) * 4)) & 15;
        predictor = NitroAdpcmMath.Advance(code, predictor, ref index, clipping);
        return (short)predictor;
    }
}
