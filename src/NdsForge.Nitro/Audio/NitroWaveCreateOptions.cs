namespace NdsForge.Nitro.Audio;

/// <summary>Controls sample-based native wave creation without silently extending duration or rounding loop positions.</summary>
public sealed record NitroWaveCreateOptions
{
    /// <summary>Gets the active loop start in samples; null disables looping.</summary>
    public int? LoopStartSample { get; init; }

    /// <summary>Gets whether incomplete final words repeat the last input sample before encoding; defaults to rejection.</summary>
    public bool PadFinalWord { get; init; }

    /// <summary>Gets an explicit timer value; null uses integer division of 16756991 by the sample rate.</summary>
    public ushort? Timer { get; init; }

    /// <summary>Gets the raw encoding policy, including ADPCM state and encoded-byte limit.</summary>
    public NitroWaveEncodeOptions EncodingOptions { get; init; } = new();

    /// <summary>Gets the maximum padded input sample count; defaults to sixteen mebisamples.</summary>
    public int MaximumSamples { get; init; } = 16 * 1024 * 1024;
}
