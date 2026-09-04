namespace NdsForge.Nitro.Audio;

/// <summary>Describes one channel's stored block and its meaningful decoded duration.</summary>
public readonly record struct StrmBlock
{
    /// <summary>Retains a validated slice of the immutable stream input.</summary>
    internal StrmBlock(int blockIndex, int channelIndex, int offset, int sampleCount, ReadOnlyMemory<byte> data)
    {
        BlockIndex = blockIndex;
        ChannelIndex = channelIndex;
        Offset = offset;
        SampleCount = sampleCount;
        EncodedData = data;
    }

    /// <summary>Gets the zero-based position within this channel.</summary>
    public int BlockIndex { get; }
    /// <summary>Gets the zero-based channel, with left before right for stereo.</summary>
    public int ChannelIndex { get; }
    /// <summary>Gets the absolute byte offset in the stored STRM input.</summary>
    public int Offset { get; }
    /// <summary>Gets the meaningful sample count, excluding any encoded padding.</summary>
    public int SampleCount { get; }
    /// <summary>Gets complete encoded bytes, including the independent ADPCM state header when present.</summary>
    public ReadOnlyMemory<byte> EncodedData { get; }
}
