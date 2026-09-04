namespace NdsForge.Audio.Wav;

/// <summary>Represents one sampler loop in frame units with an exclusive end and otherwise lossless raw metadata.</summary>
public readonly record struct WavLoop
{
    /// <summary>Describes an ordered loop; sampler and file creation validate its frame bounds.</summary>
    /// <param name="identifier">Stored cue-point identifier.</param>
    /// <param name="type">Raw loop type: zero forward, one alternating, two backward; other values remain uninterpreted.</param>
    /// <param name="startFrame">First loop frame.</param>
    /// <param name="endFrameExclusive">First frame after the loop; serialized WAV endpoints are inclusive.</param>
    /// <param name="fraction">Stored fractional loop position.</param>
    /// <param name="playCount">Stored repetition count; zero means indefinite repetition.</param>
    public WavLoop(uint identifier, uint type, int startFrame, int endFrameExclusive, uint fraction = 0, uint playCount = 0)
    {
        Identifier = identifier; Type = type; StartFrame = startFrame; EndFrameExclusive = endFrameExclusive;
        Fraction = fraction; PlayCount = playCount;
    }

    /// <summary>Gets the stored cue-point identifier.</summary>
    public uint Identifier { get; init; }
    /// <summary>Gets the raw loop type: zero forward, one alternating, two backward, or an uninterpreted value.</summary>
    public uint Type { get; init; }
    /// <summary>Gets the first loop frame.</summary>
    public int StartFrame { get; init; }
    /// <summary>Gets the first frame after the loop; WAV serializes an inclusive endpoint instead.</summary>
    public int EndFrameExclusive { get; init; }
    /// <summary>Gets the stored fractional loop position.</summary>
    public uint Fraction { get; init; }
    /// <summary>Gets the stored repetition count; zero means indefinite repetition.</summary>
    public uint PlayCount { get; init; }
}
