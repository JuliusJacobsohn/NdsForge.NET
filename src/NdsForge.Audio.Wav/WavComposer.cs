using System.Buffers.Binary;

namespace NdsForge.Audio.Wav;

/// <summary>Writes canonical bounded WAV envelopes without using a host audio codec library.</summary>
internal static class WavComposer
{
    /// <summary>Checks complete output size and metadata before allocating or quantizing input samples.</summary>
    internal static WavFile Create(ReadOnlySpan<short> samples, int channels, uint rate, WavWriteOptions options)
    {
        options.Validate();
        if (channels is not (1 or 2)) { throw new ArgumentOutOfRangeException(nameof(channels)); }
        ArgumentOutOfRangeException.ThrowIfZero(rate);
        if (samples.Length % channels != 0) { throw new ArgumentException("The WAV input ends in a partial sample frame.", nameof(samples)); }
        int width = options.Encoding == WavPcmEncoding.Unsigned8 ? 1 : 2;
        int alignment = channels * width;
        if (rate * (long)alignment > uint.MaxValue) { throw new ArgumentOutOfRangeException(nameof(rate), "The PCM byte rate exceeds its field."); }
        uint mask = options.ChannelMask ?? (channels == 1 ? 4u : 3u);
        if (options.UseExtensibleFormat) { WavFormat.ValidateMask(mask, channels); }
        int frames = samples.Length / channels;
        if (samples.Length > options.Limits.MaximumSampleValues) { throw new InvalidDataException("The WAV input sample-value limit was exceeded."); }
        WavSampler? sampler = options.Sampler;
        if (sampler is not null)
        {
            if (sampler.Loops.Count > options.Limits.MaximumLoops) { throw new InvalidDataException("The WAV sampler loop limit was exceeded."); }
            foreach (WavLoop loop in sampler.Loops) { WavSampler.ValidateLoop(loop, frames); }
        }
        if (options.Limits.MaximumChunks < (sampler is null ? 2 : 3)) { throw new InvalidDataException("The WAV chunk limit was exceeded."); }
        int formatLength = options.UseExtensibleFormat ? 40 : 16;
        long dataLength = samples.Length * (long)width;
        long length = 12 + 8 + formatLength + 8 + dataLength + dataLength % 2;
        if (sampler is not null) { length += 8L + sampler.RawData.Length + sampler.RawData.Length % 2; }
        if (length > options.Limits.MaximumInputBytes) { throw new InvalidDataException("The complete WAV output byte limit was exceeded."); }
        byte[] bytes = new byte[(int)length];
        "RIFF"u8.CopyTo(bytes); U32(bytes, 4, (uint)length - 8); "WAVE"u8.CopyTo(bytes.AsSpan(8));
        "fmt "u8.CopyTo(bytes.AsSpan(12)); U32(bytes, 16, (uint)formatLength);
        U16(bytes, 20, options.UseExtensibleFormat ? (ushort)0xFFFE : (ushort)1);
        U16(bytes, 22, (ushort)channels); U32(bytes, 24, rate); U32(bytes, 28, (uint)(rate * alignment));
        U16(bytes, 32, (ushort)alignment); U16(bytes, 34, (ushort)(width * 8));
        if (options.UseExtensibleFormat)
        {
            U16(bytes, 36, 22); U16(bytes, 38, (ushort)(width * 8)); U32(bytes, 40, mask);
            _ = WavFormat.PcmSubformat.TryWriteBytes(bytes.AsSpan(44, 16));
        }
        int position = 20 + formatLength;
        "data"u8.CopyTo(bytes.AsSpan(position)); U32(bytes, position + 4, (uint)dataLength);
        position += 8;
        for (int i = 0; i < samples.Length; i++)
        {
            if (width == 1) { bytes[position + i] = (byte)((samples[i] >> 8) + 128); }
            else { BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(position + i * 2), samples[i]); }
        }
        position += (int)(dataLength + dataLength % 2);
        if (sampler is not null)
        {
            "smpl"u8.CopyTo(bytes.AsSpan(position)); U32(bytes, position + 4, (uint)sampler.RawData.Length);
            sampler.RawData.Span.CopyTo(bytes.AsSpan(position + 8));
        }
        return WavFile.Parse(bytes, options.Limits);
    }

    /// <summary>Writes a checked field into the bounded output envelope.</summary>
    private static void U16(Span<byte> bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes[offset..], value);
    /// <summary>Writes a checked field into the bounded output envelope.</summary>
    private static void U32(Span<byte> bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
}
