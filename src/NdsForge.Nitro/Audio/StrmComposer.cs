using System.Buffers.Binary;

namespace NdsForge.Nitro.Audio;

/// <summary>Creates canonical streams from sample frames with independent block state and explicit final duration.</summary>
internal static class StrmComposer
{
    /// <summary>Preflights the complete stored layout before allocating and encoding channel blocks.</summary>
    internal static StrmFile Create(ReadOnlySpan<short> samples, int channels, NitroWaveEncoding encoding, ushort rate, StrmCreateOptions options)
    {
        options.Validate();
        if (channels is not (1 or 2)) { throw new ArgumentOutOfRangeException(nameof(channels)); }
        if (!Enum.IsDefined(encoding)) { throw new ArgumentOutOfRangeException(nameof(encoding)); }
        ArgumentOutOfRangeException.ThrowIfZero(rate);
        if (samples.Length % channels != 0) { throw new ArgumentException("The interleaved input ends in a partial sample frame.", nameof(samples)); }
        if (samples.Length > options.Limits.MaximumSampleValues) { throw new InvalidDataException("The stream input sample-value limit was exceeded."); }
        long fullSamples = encoding switch
        {
            NitroWaveEncoding.Pcm8 => options.BlockByteLength,
            NitroWaveEncoding.Pcm16 when options.BlockByteLength % 2 == 0 => options.BlockByteLength / 2,
            NitroWaveEncoding.ImaAdpcm when options.BlockByteLength >= 5 => (options.BlockByteLength - 4L) * 2,
            _ => throw new ArgumentOutOfRangeException(nameof(options), "The selected encoding cannot use this full block byte length."),
        };
        if (fullSamples > int.MaxValue) { throw new ArgumentOutOfRangeException(nameof(options), "The full block sample capacity is not representable."); }
        int frames = samples.Length / channels;
        if (options.LoopStartSample is int loop && (loop < 0 || loop >= frames)) { throw new ArgumentOutOfRangeException(nameof(options), "The loop must begin inside the input duration."); }
        int timer = options.Timer ?? (16756991 / rate / 32);
        if (timer > ushort.MaxValue) { throw new ArgumentOutOfRangeException(nameof(rate), "This rate requires an explicit representable stream timer."); }
        int blocks = (int)Math.Max(1, (frames + fullSamples - 1) / fullSamples);
        if (blocks > options.Limits.MaximumBlocksPerChannel) { throw new InvalidDataException("The stream block count limit was exceeded."); }
        int finalSamples = (int)(frames - (blocks - 1L) * fullSamples);
        long finalBytes = encoding switch
        {
            NitroWaveEncoding.Pcm8 => finalSamples,
            NitroWaveEncoding.Pcm16 => finalSamples * 2L,
            _ => 4 + (finalSamples + 1L) / 2,
        };
        long largestBlock = blocks > 1 ? options.BlockByteLength : finalBytes;
        if (largestBlock > options.SampleEncoding.MaximumOutputBytes) { throw new InvalidDataException("The encoded channel block limit was exceeded."); }
        long payload = ((blocks - 1L) * options.BlockByteLength + finalBytes) * channels;
        long length = (104 + payload + 3) & ~3L;
        if (length > options.Limits.MaximumInputBytes) { throw new InvalidDataException("The complete stream output limit was exceeded."); }
        byte[] bytes = new byte[(int)length];
        WriteEnvelope(bytes);
        Span<byte> head = bytes.AsSpan(16, 80);
        head[8] = (byte)encoding;
        head[9] = options.LoopStartSample.HasValue ? (byte)1 : (byte)0;
        head[10] = (byte)channels;
        BinaryPrimitives.WriteUInt16LittleEndian(head[12..], rate);
        BinaryPrimitives.WriteUInt16LittleEndian(head[14..], (ushort)timer);
        Put(head, 16, options.LoopStartSample ?? 0);
        Put(head, 20, frames);
        Put(head, 24, 104);
        Put(head, 28, blocks);
        Put(head, 32, blocks == 1 ? (int)finalBytes : options.BlockByteLength);
        Put(head, 36, blocks == 1 ? finalSamples : (int)fullSamples);
        Put(head, 40, (int)finalBytes);
        Put(head, 44, finalSamples);
        int position = 104, frame = 0;
        for (int block = 0; block < blocks; block++)
        {
            int count = block == blocks - 1 ? finalSamples : (int)fullSamples;
            for (int channel = 0; channel < channels; channel++)
            {
                short[] mono = new short[count];
                for (int i = 0; i < count; i++) { mono[i] = samples[(frame + i) * channels + channel]; }
                byte[] encoded = NitroWaveCodec.Encode(mono, encoding, options.SampleEncoding);
                encoded.CopyTo(bytes, position);
                position += encoded.Length;
            }
            frame += count;
        }
        return StrmFile.Parse(bytes, options.Limits);
    }

    /// <summary>Initializes a minimal standard envelope over an already bounded, aligned output.</summary>
    internal static void WriteEnvelope(Span<byte> bytes)
    {
        "STRM"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[4..], 0xFEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[6..], 0x0100);
        Put(bytes, 8, bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[12..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[14..], 2);
        "HEAD"u8.CopyTo(bytes[16..]);
        Put(bytes, 20, 80);
        "DATA"u8.CopyTo(bytes[96..]);
        Put(bytes, 100, bytes.Length - 96);
    }

    /// <summary>Writes a checked nonnegative signed value into a native unsigned-length field.</summary>
    private static void Put(Span<byte> bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], value);
}
