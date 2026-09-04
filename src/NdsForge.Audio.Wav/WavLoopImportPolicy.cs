namespace NdsForge.Audio.Wav;

/// <summary>Selects how WAV sampler loops are mapped into a native DS format.</summary>
public enum WavLoopImportPolicy
{
    /// <summary>Preserves a single forward, integer, infinite loop ending at the file duration; rejects other active loop semantics.</summary>
    Preserve = 0,
    /// <summary>Explicitly ignores every WAV sampler loop; an independently requested native loop still applies.</summary>
    Ignore = 1,
}
