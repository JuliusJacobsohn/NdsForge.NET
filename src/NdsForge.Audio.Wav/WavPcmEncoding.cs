namespace NdsForge.Audio.Wav;

/// <summary>Identifies the supported WAV PCM storage representations, distinct from native DS signed PCM8.</summary>
public enum WavPcmEncoding
{
    /// <summary>Unsigned eight-bit samples, with silence at 128.</summary>
    Unsigned8 = 0,
    /// <summary>Signed sixteen-bit little-endian samples, with silence at zero.</summary>
    Signed16 = 1,
}
