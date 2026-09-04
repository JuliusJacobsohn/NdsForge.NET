namespace NdsForge.Audio.Wav;

/// <summary>Retains sampler identification, tuning, and synchronization fields without interpreting device-specific values.</summary>
public sealed record WavSamplerMetadata
{
    /// <summary>Gets the raw manufacturer identifier.</summary>
    public uint Manufacturer { get; init; }
    /// <summary>Gets the raw product identifier.</summary>
    public uint Product { get; init; }
    /// <summary>Gets the stored sample period in nanoseconds, independently from the PCM sample rate.</summary>
    public uint SamplePeriodNanoseconds { get; init; }
    /// <summary>Gets the stored MIDI unity note; defaults to middle C, sixty.</summary>
    public uint MidiUnityNote { get; init; } = 60;
    /// <summary>Gets the raw MIDI pitch-fraction field.</summary>
    public uint MidiPitchFraction { get; init; }
    /// <summary>Gets the uninterpreted SMPTE format declaration.</summary>
    public uint SmpteFormat { get; init; }
    /// <summary>Gets the uninterpreted SMPTE offset declaration.</summary>
    public uint SmpteOffset { get; init; }
}
