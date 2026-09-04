namespace NdsForge.Audio.Wav;

/// <summary>Controls deterministic PCM WAV creation without resampling or implicit loop changes.</summary>
public sealed record WavWriteOptions
{
    /// <summary>Gets the output PCM representation; defaults to signed sixteen-bit samples.</summary>
    public WavPcmEncoding Encoding { get; init; } = WavPcmEncoding.Signed16;
    /// <summary>Gets whether to emit the extensible PCM format header; defaults to false.</summary>
    public bool UseExtensibleFormat { get; init; }
    /// <summary>Gets an explicit extensible channel mask, or null for front-center mono or front-left/right stereo.</summary>
    public uint? ChannelMask { get; init; }
    /// <summary>Gets optional sampler metadata, retained exactly after validation against the output frame count.</summary>
    public WavSampler? Sampler { get; init; }
    /// <summary>Gets bounds for the complete result, input sample values, and metadata.</summary>
    public WavReadOptions Limits { get; init; } = new();

    /// <summary>Checks caller options independently from sample input.</summary>
    internal void Validate()
    {
        if (!Enum.IsDefined(Encoding)) { throw new ArgumentOutOfRangeException(nameof(Encoding)); }
        ArgumentNullException.ThrowIfNull(Limits);
        Limits.Validate();
        if (!UseExtensibleFormat && ChannelMask.HasValue) { throw new ArgumentException("An explicit channel mask requires the extensible format header."); }
    }
}
