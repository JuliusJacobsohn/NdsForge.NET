using NdsForge.Nitro.Audio;

namespace NdsForge.Audio.Wav;

/// <summary>Controls WAV sample representation and allocation while retaining native duration and active loop semantics.</summary>
public sealed record NitroWavExportOptions
{
    /// <summary>Gets the WAV storage representation; defaults to signed sixteen-bit samples.</summary>
    public WavPcmEncoding Encoding { get; init; } = WavPcmEncoding.Signed16;
    /// <summary>Gets whether to write extensible PCM with standard mono or stereo speaker positions.</summary>
    public bool UseExtensibleFormat { get; init; }
    /// <summary>Gets the native ADPCM subtraction convention; defaults to Nintendo DS behavior.</summary>
    public NitroAdpcmClipping AdpcmClipping { get; init; }
    /// <summary>Gets complete WAV output, total sample-value, and sampler metadata limits.</summary>
    public WavReadOptions Limits { get; init; } = new();
}
