using System.Buffers.Binary;

namespace NdsForge.Nitro.Audio;

/// <summary>Edits stream metadata and fixed-size channel blocks while retaining layout or writing a canonical envelope.</summary>
public sealed class StrmFileBuilder
{
    private readonly StrmFile _source;
    private readonly Dictionary<(int Block, int Channel), byte[]> _replacements = [];

    /// <summary>Copies editable fields while retaining the immutable source and its block geometry.</summary>
    internal StrmFileBuilder(StrmFile source)
    {
        _source = source;
        SampleRate = source.SampleRate;
        Timer = source.Timer;
        RawLoopFlag = source.RawLoopFlag;
        RawLoopStartSample = source.RawLoopStartSample;
        RawFlags = source.RawFlags;
    }

    /// <summary>Gets or sets the sample rate without implicitly changing the independent timer.</summary>
    public ushort SampleRate { get; set; }
    /// <summary>Gets or sets the independently stored stream timer.</summary>
    public ushort Timer { get; set; }
    /// <summary>Gets or sets the raw loop flag; any nonzero value activates the stored loop position.</summary>
    public byte RawLoopFlag { get; set; }
    /// <summary>Gets or sets the raw loop position in sample frames; inactive values are retained uninterpreted.</summary>
    public uint RawLoopStartSample { get; set; }
    /// <summary>Gets or sets uninterpreted stream flags without changing their other fields.</summary>
    public byte RawFlags { get; set; }

    /// <summary>Copies a complete replacement channel block with unchanged byte length and meaningful sample count.</summary>
    /// <param name="blockIndex">Zero-based block position within the channel.</param>
    /// <param name="channelIndex">Zero-based channel index.</param>
    /// <param name="encodedData">Native bytes including the ADPCM state header when present.</param>
    public void ReplaceBlock(int blockIndex, int channelIndex, ReadOnlySpan<byte> encodedData)
    {
        StrmBlock block = _source.GetBlock(blockIndex, channelIndex);
        if (encodedData.Length != block.EncodedData.Length) { throw new ArgumentException("A fixed-slot replacement must retain the complete block byte length.", nameof(encodedData)); }
        StrmValidation.ValidateBlock(encodedData, _source.Encoding, block.SampleCount);
        _replacements[(blockIndex, channelIndex)] = encodedData.ToArray();
    }

    /// <summary>Builds and reparses the complete result before exposing any output.</summary>
    /// <param name="options">Output bounds and whether to retain the original envelope and padding.</param>
    /// <returns>A detached validated stream; canonical output uses header-inclusive ADPCM lengths and four-byte final alignment.</returns>
    public byte[] Build(StrmWriteOptions? options = null)
    {
        options ??= new();
        options.Validate();
        if (SampleRate == 0 || (RawLoopFlag != 0 && RawLoopStartSample >= _source.SampleCount)) { throw new InvalidDataException("The edited stream sample rate or active loop is invalid."); }
        byte[] result = Compose(options);
        int header = options.PreserveSourceLayout ? _source.HeaderLength : 16;
        result[header + 9] = RawLoopFlag;
        result[header + 11] = RawFlags;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(header + 12), SampleRate);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(header + 14), Timer);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(header + 16), RawLoopStartSample);
        _ = StrmFile.Parse(result, new() { MaximumInputBytes = options.MaximumOutputBytes, MaximumSampleValues = _source.SampleValueCount, MaximumBlocksPerChannel = _source.BlocksPerChannel });
        return result;
    }

    /// <summary>Preflights the selected layout and copies only the regions belonging to that layout.</summary>
    private byte[] Compose(StrmWriteOptions options)
    {
        if (options.PreserveSourceLayout)
        {
            if (_source.Source.Length > options.MaximumOutputBytes) { throw new InvalidDataException("The stream output byte limit was exceeded."); }
            byte[] preserved = _source.WritePreserved();
            foreach (var replacement in _replacements)
            {
                replacement.Value.CopyTo(preserved, _source.GetBlock(replacement.Key.Block, replacement.Key.Channel).Offset);
            }
            return preserved;
        }
        StrmBlock last = _source.GetBlock(_source.BlocksPerChannel - 1, _source.ChannelCount - 1);
        long payload = last.Offset + (long)last.EncodedData.Length - _source.DataOffset;
        long length = (104 + payload + 3) & ~3L;
        if (length > options.MaximumOutputBytes) { throw new InvalidDataException("The canonical stream output byte limit was exceeded."); }
        byte[] result = new byte[(int)length];
        StrmComposer.WriteEnvelope(result);
        _source.Source.Span.Slice(_source.HeaderLength + 8, 72).CopyTo(result.AsSpan(24));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(40), 104);
        if (_source.BlocksPerChannel == 1)
        {
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(48), last.EncodedData.Length);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(52), last.SampleCount);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(56), last.EncodedData.Length);
        }
        int position = 104;
        for (int block = 0; block < _source.BlocksPerChannel; block++)
        {
            for (int channel = 0; channel < _source.ChannelCount; channel++)
            {
                ReadOnlySpan<byte> bytes = _replacements.TryGetValue((block, channel), out byte[]? replacement) ? replacement : _source.GetBlock(block, channel).EncodedData.Span;
                bytes.CopyTo(result.AsSpan(position));
                position += bytes.Length;
            }
        }
        return result;
    }
}
