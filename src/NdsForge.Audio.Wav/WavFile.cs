using System.Buffers.Binary;

namespace NdsForge.Audio.Wav;

/// <summary>Provides bounded PCM WAV interchange while preserving unknown chunks, sampler metadata, and trailing bytes.</summary>
public sealed class WavFile
{
    private readonly byte[] _source;
    private readonly WavFormat _format;

    /// <summary>Retains validated chunk slices and sample metadata in detached storage.</summary>
    private WavFile(byte[] source, int declaredLength, WavFormat format, List<(int Offset, int Length, byte? Pad)> layouts,
        int dataIndex, WavSampler? sampler)
    {
        _source = source; _format = format; DeclaredLength = declaredLength; Sampler = sampler;
        Chunks = Array.AsReadOnly(layouts.Select(item => new WavChunk(BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(item.Offset)),
            item.Offset, source.AsMemory(item.Offset + 8, item.Length), item.Pad)).ToArray());
        EncodedSamples = Chunks[dataIndex].Data;
        HasOmittedFinalPadding = layouts.Count > 0 && layouts[^1].Length % 2 != 0 && !layouts[^1].Pad.HasValue;
    }

    /// <summary>Gets the complete RIFF-declared file length, excluding any outer trailing bytes.</summary>
    public int DeclaredLength { get; }
    /// <summary>Gets all direct RIFF chunks in stored order, including unknown chunks and their alignment bytes.</summary>
    public IReadOnlyList<WavChunk> Chunks { get; }
    /// <summary>Gets the optional sampler metadata and every stored loop.</summary>
    public WavSampler? Sampler { get; }
    /// <summary>Gets the input PCM bytes in frame-major order, excluding chunk padding.</summary>
    public ReadOnlyMemory<byte> EncodedSamples { get; }
    /// <summary>Gets the raw format tag, one for standard PCM or 65534 for extensible PCM.</summary>
    public ushort FormatTag => _format.Tag;
    /// <summary>Gets whether the format uses the extensible PCM header.</summary>
    public bool IsExtensible => _format.Tag == 0xFFFE;
    /// <summary>Gets the PCM representation stored in the data chunk.</summary>
    public WavPcmEncoding Encoding => _format.Encoding;
    /// <summary>Gets the stored valid-bit count, equal to the supported PCM container width.</summary>
    public ushort ValidBitsPerSample => _format.ValidBits;
    /// <summary>Gets the extensible speaker mask, or zero when unspecified or using standard PCM.</summary>
    public uint ChannelMask => _format.ChannelMask;
    /// <summary>Gets one for mono or two for stereo.</summary>
    public int ChannelCount => _format.Channels;
    /// <summary>Gets the nonzero sample-frame rate in hertz, without imposing native DS rate limits.</summary>
    public uint SampleRate => _format.Rate;
    /// <summary>Gets the validated average PCM byte rate.</summary>
    public uint AverageBytesPerSecond => _format.ByteRate;
    /// <summary>Gets the complete sample-frame width in bytes.</summary>
    public ushort BlockAlignment => _format.Alignment;
    /// <summary>Gets the number of complete sample frames per channel.</summary>
    public int FrameCount => EncodedSamples.Length / BlockAlignment;
    /// <summary>Gets the number of sample values across all channels.</summary>
    public int SampleValueCount => FrameCount * ChannelCount;
    /// <summary>Gets whether compatibility parsing accepted an omitted final chunk-alignment byte.</summary>
    public bool HasOmittedFinalPadding { get; }

    /// <summary>Parses and copies a PCM RIFF/WAVE with bounded chunk traversal, arbitrary chunk order, and exact preservation.</summary>
    /// <param name="data">Complete WAV bytes, optionally followed by outer allocation data.</param>
    /// <param name="options">Input, sample, chunk, loop, and final-padding limits.</param>
    /// <returns>A detached WAV with complete PCM frames and validated sampler loop ranges.</returns>
    public static WavFile Parse(ReadOnlySpan<byte> data, WavReadOptions? options = null)
    {
        options ??= new(); options.Validate();
        if (data.Length > options.MaximumInputBytes) { throw new InvalidDataException("The WAV input byte limit was exceeded."); }
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("A little-endian RIFF/WAVE header is required.");
        }
        long declared = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) + 8L;
        if (declared < 12 || declared > data.Length) { throw new InvalidDataException("The RIFF size is truncated or invalid."); }
        var layouts = new List<(int Offset, int Length, byte? Pad)>();
        int position = 12, formatIndex = -1, dataIndex = -1, samplerIndex = -1;
        while (position < declared)
        {
            if (layouts.Count >= options.MaximumChunks || declared - position < 8) { throw new InvalidDataException("The WAV chunk count limit was exceeded or a chunk header is truncated."); }
            long length = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 4)..]);
            long end = position + 8L + length;
            if (end > declared) { throw new InvalidDataException("A WAV chunk exceeds the declared RIFF range."); }
            byte? pad = null;
            if (length % 2 != 0)
            {
                if (end == declared)
                {
                    if (!options.AllowMissingFinalPadding) { throw new InvalidDataException("The final odd-sized WAV chunk lacks WORD padding."); }
                }
                else { pad = data[(int)end]; end++; }
            }
            ReadOnlySpan<byte> id = data.Slice(position, 4);
            if (id.SequenceEqual("fmt "u8)) { SetUnique(ref formatIndex, layouts.Count); }
            else if (id.SequenceEqual("data"u8)) { SetUnique(ref dataIndex, layouts.Count); }
            else if (id.SequenceEqual("smpl"u8)) { SetUnique(ref samplerIndex, layouts.Count); }
            layouts.Add((position, (int)length, pad));
            position = (int)end;
        }
        if (formatIndex < 0 || dataIndex < 0) { throw new InvalidDataException("A WAV requires exactly one format and one data chunk."); }
        var formatLayout = layouts[formatIndex]; var sampleLayout = layouts[dataIndex];
        WavFormat format = WavFormat.Parse(data.Slice(formatLayout.Offset + 8, formatLayout.Length));
        if (sampleLayout.Length % format.Alignment != 0) { throw new InvalidDataException("The WAV data ends in a partial PCM frame."); }
        int frames = sampleLayout.Length / format.Alignment;
        if (frames * (long)format.Channels > options.MaximumSampleValues) { throw new InvalidDataException("The WAV sample-value limit was exceeded."); }
        WavSampler? sampler = null;
        if (samplerIndex >= 0)
        {
            var layout = layouts[samplerIndex];
            sampler = WavSampler.Parse(data.Slice(layout.Offset + 8, layout.Length), options, frames);
        }
        return new(data.ToArray(), (int)declared, format, layouts, dataIndex, sampler);
    }

    /// <summary>Creates a deterministic standard or extensible PCM WAV, with optional sampler metadata and canonical WORD padding.</summary>
    /// <param name="samples">Signed sixteen-bit frame-major values; unsigned-eight output explicitly discards the low byte.</param>
    /// <param name="channelCount">One for mono or two for stereo.</param>
    /// <param name="sampleRate">Nonzero sample-frame rate in hertz.</param>
    /// <param name="options">Output encoding, optional sampler metadata, extensible layout, and allocation limits.</param>
    /// <returns>A detached validated WAV whose frame count matches the complete input.</returns>
    public static WavFile Create(ReadOnlySpan<short> samples, int channelCount, uint sampleRate, WavWriteOptions? options = null)
        => WavComposer.Create(samples, channelCount, sampleRate, options ?? new());

    /// <summary>Decodes unsigned-eight or signed-sixteen PCM into signed-sixteen frame-major values without resampling.</summary>
    /// <param name="maximumSampleValues">Output allocation ceiling; defaults to thirty-two mebisamples.</param>
    /// <returns>Exactly the declared sample-value count, with no padding or repeated loops.</returns>
    public short[] Decode(int maximumSampleValues = 32 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSampleValues);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumSampleValues, Array.MaxLength / 2);
        if (SampleValueCount > maximumSampleValues) { throw new InvalidDataException("The WAV decoded sample-value limit was exceeded."); }
        short[] result = new short[SampleValueCount];
        ReadOnlySpan<byte> bytes = EncodedSamples.Span;
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Encoding == WavPcmEncoding.Unsigned8 ? (short)((bytes[i] - 128) * 256) : BinaryPrimitives.ReadInt16LittleEndian(bytes[(i * 2)..]);
        }
        return result;
    }

    /// <summary>Returns every original byte, including unknown chunks, pad values, and outer trailing bytes.</summary>
    /// <returns>A detached exact copy of the parsed input or created WAV.</returns>
    public byte[] WritePreserved() => (byte[])_source.Clone();

    /// <summary>Rejects ambiguous repeated semantic chunks while retaining unrelated repeated identifiers.</summary>
    private static void SetUnique(ref int slot, int index)
    {
        if (slot >= 0) { throw new InvalidDataException("Duplicate format, data, or sampler chunks are ambiguous."); }
        slot = index;
    }
}
