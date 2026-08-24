namespace NdsForge.Graphics.Animations;

/// <summary>Models one NANR animation sequence.</summary>
public sealed class NanrSequence
{
    internal NanrSequence(ushort dataType, ushort playbackMode, ushort loopStartFrame,
        ushort sequenceFlags, uint frameOffset, IReadOnlyList<NanrFrame> frames)
    {
        DataType = dataType;
        PlaybackMode = playbackMode;
        LoopStartFrame = loopStartFrame;
        SequenceFlags = sequenceFlags;
        FrameOffset = frameOffset;
        Frames = Array.AsReadOnly(frames.ToArray());
    }

    /// <summary>Gets the frame-data variant, from zero through two.</summary>
    public ushort DataType { get; }

    /// <summary>Gets the exact playback-mode word.</summary>
    public ushort PlaybackMode { get; }

    /// <summary>Gets the exact loop-start word.</summary>
    public ushort LoopStartFrame { get; }

    /// <summary>Gets the exact trailing sequence-flags word.</summary>
    public ushort SequenceFlags { get; }

    /// <summary>Gets the offset into the ABNK frame-descriptor table.</summary>
    public uint FrameOffset { get; }

    /// <summary>Gets frames in playback-table order.</summary>
    public IReadOnlyList<NanrFrame> Frames { get; }
}
