namespace NdsForge.Nitro.Audio;

/// <summary>Identifies the encoded sample representation used by native DS waves and streams.</summary>
public enum NitroWaveEncoding
{
    /// <summary>Signed eight-bit PCM, expanded by multiplying by 256.</summary>
    Pcm8,

    /// <summary>Signed sixteen-bit little-endian PCM.</summary>
    Pcm16,

    /// <summary>DS IMA-ADPCM with a four-byte state header and low-nibble-first codes.</summary>
    ImaAdpcm,
}

/// <summary>Selects the lower saturation behavior for IMA-ADPCM subtraction.</summary>
public enum NitroAdpcmClipping
{
    /// <summary>Clips subtraction to -32767, following the DS sound-unit convention.</summary>
    NintendoDs,

    /// <summary>Clips subtraction to -32768, as used by general signed-sixteen-bit IMA decoders.</summary>
    Signed16,
}
