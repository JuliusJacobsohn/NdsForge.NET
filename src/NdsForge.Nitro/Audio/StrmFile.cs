using System.Buffers.Binary;

namespace NdsForge.Nitro.Audio;

/// <summary>Provides bounded native stream metadata, per-channel blocks, sample decoding, and exact preservation.</summary>
public sealed class StrmFile
{
    private readonly byte[] _source;

    /// <summary>Retains validated bytes after every allocation and sample boundary has been checked.</summary>
    private StrmFile(byte[] source, bool headerExcluded)
    {
        _source = source;
        HeaderLength = Read16(source, 12);
        DeclaredLength = (int)Read32(source, 8);
        ByteOrderMarker = Read16(source, 4);
        InformationBlockLength = (int)Read32(source, HeaderLength + 4);
        DataBlockOffset = HeaderLength + InformationBlockLength;
        DataBlockLength = (int)Read32(source, DataBlockOffset + 4);
        Encoding = (NitroWaveEncoding)source[HeaderLength + 8];
        RawLoopFlag = source[HeaderLength + 9];
        ChannelCount = source[HeaderLength + 10];
        RawFlags = source[HeaderLength + 11];
        SampleRate = Read16(source, HeaderLength + 12);
        Timer = Read16(source, HeaderLength + 14);
        RawLoopStartSample = Read32(source, HeaderLength + 16);
        SampleCount = (int)Read32(source, HeaderLength + 20);
        DataOffset = (int)Read32(source, HeaderLength + 24);
        BlocksPerChannel = (int)Read32(source, HeaderLength + 28);
        NormalBlockByteLength = Read32(source, HeaderLength + 32);
        NormalBlockSampleCount = Read32(source, HeaderLength + 36);
        FinalBlockByteLength = (int)Read32(source, HeaderLength + 40);
        FinalBlockSampleCount = (int)Read32(source, HeaderLength + 44);
        ExcludesAdpcmStateHeaderFromLength = headerExcluded;
    }

    /// <summary>Gets the raw standard-file marker; native metadata and sample fields remain little-endian.</summary>
    public ushort ByteOrderMarker { get; }
    /// <summary>Gets the standard-file header size, including preserved extension bytes.</summary>
    public int HeaderLength { get; }
    /// <summary>Gets the complete declared file size, excluding allocation padding.</summary>
    public int DeclaredLength { get; }
    /// <summary>Gets the HEAD block size, including reserved and extension bytes.</summary>
    public int InformationBlockLength { get; }
    /// <summary>Gets the DATA block's absolute header offset.</summary>
    public int DataBlockOffset { get; }
    /// <summary>Gets the DATA block's size, including its header and any padding.</summary>
    public int DataBlockLength { get; }
    /// <summary>Gets the first channel block's absolute offset.</summary>
    public int DataOffset { get; }
    /// <summary>Gets the stored native sample encoding shared by all channels.</summary>
    public NitroWaveEncoding Encoding { get; }
    /// <summary>Gets the preserved loop flag; any nonzero value enables looping.</summary>
    public byte RawLoopFlag { get; }
    /// <summary>Gets whether the declared loop is active.</summary>
    public bool IsLooping => RawLoopFlag != 0;
    /// <summary>Gets the preserved uninterpreted stream flags.</summary>
    public byte RawFlags { get; }
    /// <summary>Gets the channel count, either one for mono or two for stereo.</summary>
    public int ChannelCount { get; }
    /// <summary>Gets the nonzero sample rate, in frames per second.</summary>
    public ushort SampleRate { get; }
    /// <summary>Gets the independently stored stream timer; it need not be derived from the sample rate.</summary>
    public ushort Timer { get; }
    /// <summary>Gets the raw loop position, including inactive values that are preserved without interpretation.</summary>
    public uint RawLoopStartSample { get; }
    /// <summary>Gets the active loop's first sample frame, or null when looping is disabled.</summary>
    public int? LoopStartSample => IsLooping ? (int)RawLoopStartSample : null;
    /// <summary>Gets the exclusive loop end in sample frames, or null when looping is disabled.</summary>
    public int? LoopEndSample => IsLooping ? SampleCount : null;
    /// <summary>Gets the meaningful sample count per channel, not the interleaved array length.</summary>
    public int SampleCount { get; }
    /// <summary>Gets the meaningful number of decoded values across all channels.</summary>
    public int SampleValueCount => SampleCount * ChannelCount;
    /// <summary>Gets the number of stored blocks in each channel, including a possible empty final block.</summary>
    public int BlocksPerChannel { get; }
    /// <summary>Gets the raw byte count per nonfinal channel block; unused declarations remain lossless.</summary>
    public uint NormalBlockByteLength { get; }
    /// <summary>Gets the raw sample count per nonfinal channel block; unused declarations remain lossless.</summary>
    public uint NormalBlockSampleCount { get; }
    /// <summary>Gets the raw final byte count per channel; inspect the explicit state-header convention before using it as a slice length.</summary>
    public int FinalBlockByteLength { get; }
    /// <summary>Gets the meaningful sample count in each channel's final block.</summary>
    public int FinalBlockSampleCount { get; }
    /// <summary>Gets whether this single-block ADPCM file's length omits its four-byte state header.</summary>
    public bool ExcludesAdpcmStateHeaderFromLength { get; }
    /// <summary>Gets preserved HEAD bytes following the defined sample and block fields, including its standard reserved area.</summary>
    public ReadOnlyMemory<byte> ReservedMetadata => _source.AsMemory(HeaderLength + 48, InformationBlockLength - 48);

    /// <summary>Validates complete headers, block layout, loops, ADPCM states, and allocation limits before copying the source.</summary>
    /// <param name="data">Complete stream bytes, optionally including allocation padding.</param>
    /// <param name="options">Stored-byte, total sample-value, and per-channel block limits.</param>
    /// <returns>An immutable stream whose blocks are safe to inspect and decode.</returns>
    public static StrmFile Parse(ReadOnlySpan<byte> data, StrmReadOptions? options = null)
    {
        options ??= new();
        options.Validate();
        bool headerExcluded = StrmValidation.Validate(data, options);
        return new(data.ToArray(), headerExcluded);
    }

    /// <summary>Creates a canonical stream from interleaved signed sixteen-bit input without resampling or repeating loops.</summary>
    /// <param name="samples">Frame-major sample values, left then right for stereo.</param>
    /// <param name="channelCount">One for mono or two for stereo.</param>
    /// <param name="encoding">Native representation used independently by each channel block.</param>
    /// <param name="sampleRate">Nonzero input and output sample rate.</param>
    /// <param name="options">Block sizing, timing, loop, encoding, and allocation choices.</param>
    /// <returns>A validated stream retaining exactly the input frame count, including odd ADPCM durations.</returns>
    public static StrmFile Create(ReadOnlySpan<short> samples, int channelCount, NitroWaveEncoding encoding, ushort sampleRate,
        StrmCreateOptions? options = null) => StrmComposer.Create(samples, channelCount, encoding, sampleRate, options ?? new());

    /// <summary>Creates an isolated metadata and fixed-slot block replacement plan.</summary>
    /// <returns>A builder initially preserving all source bytes.</returns>
    public StrmFileBuilder CreateBuilder() => new(this);

    /// <summary>Gets a channel block without copying or decoding its stored sample data.</summary>
    /// <param name="blockIndex">Zero-based block index, less than the per-channel block count.</param>
    /// <param name="channelIndex">Zero-based channel index.</param>
    /// <returns>A validated complete block; ADPCM state headers are included even for legacy length declarations.</returns>
    public StrmBlock GetBlock(int blockIndex, int channelIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(blockIndex, BlocksPerChannel);
        ArgumentOutOfRangeException.ThrowIfNegative(channelIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channelIndex, ChannelCount);
        bool final = blockIndex == BlocksPerChannel - 1;
        int length = final ? FinalBlockByteLength + (ExcludesAdpcmStateHeaderFromLength ? 4 : 0) : (int)NormalBlockByteLength;
        int count = final ? FinalBlockSampleCount : (int)NormalBlockSampleCount;
        int offset = (int)(DataOffset + blockIndex * (long)NormalBlockByteLength * ChannelCount + channelIndex * (long)length);
        return new(blockIndex, channelIndex, offset, count, _source.AsMemory(offset, length));
    }

    /// <summary>Decodes one pass into interleaved signed sixteen-bit values; loops are described rather than repeated.</summary>
    /// <param name="options">Total output-value limit and clipping policy; the stream default is thirty-two mebisamples.</param>
    /// <returns>Sample-frame-major values, left then right for stereo, with encoded padding omitted.</returns>
    public short[] Decode(NitroWaveDecodeOptions? options = null) => DecodeSamples(null, options);

    /// <summary>Decodes only one channel, resetting ADPCM state independently at every block.</summary>
    /// <param name="channelIndex">Zero-based channel index.</param>
    /// <param name="options">Selected-channel output limit and clipping policy; the stream default is thirty-two mebisamples.</param>
    /// <returns>Exactly the declared per-channel sample count.</returns>
    public short[] DecodeChannel(int channelIndex, NitroWaveDecodeOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(channelIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channelIndex, ChannelCount);
        return DecodeSamples(channelIndex, options);
    }

    /// <summary>Returns every input byte, including uninterpreted header fields and all padding.</summary>
    /// <returns>A detached byte-exact copy.</returns>
    public byte[] WritePreserved() => (byte[])_source.Clone();

    /// <summary>Exposes the original layout to its paired writer without a public writable buffer.</summary>
    internal ReadOnlyMemory<byte> Source => _source;

    /// <summary>Preflights the complete result before decoding and interleaving bounded channel blocks.</summary>
    private short[] DecodeSamples(int? selectedChannel, NitroWaveDecodeOptions? options)
    {
        options ??= new() { MaximumSamples = 32 * 1024 * 1024 };
        options.Validate();
        int channels = selectedChannel.HasValue ? 1 : ChannelCount;
        int count = SampleCount * channels;
        if (count > options.MaximumSamples) { throw new InvalidDataException("The decoded stream sample-value limit was exceeded."); }
        short[] result = new short[count];
        int frame = 0;
        for (int blockIndex = 0; blockIndex < BlocksPerChannel; blockIndex++)
        {
            int blockSamples = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                StrmBlock block = GetBlock(blockIndex, selectedChannel ?? channel);
                blockSamples = block.SampleCount;
                if (blockSamples == 0) { continue; }
                short[] decoded = NitroWaveCodec.Decode(block.EncodedData.Span, Encoding, blockSamples, options);
                for (int i = 0; i < decoded.Length; i++) { result[(frame + i) * channels + channel] = decoded[i]; }
            }
            frame += blockSamples;
        }
        return result;
    }

    /// <summary>Reads a field only after validation has established its range.</summary>
    private static ushort Read16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
    /// <summary>Reads a field only after validation has established its range.</summary>
    private static uint Read32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
}
