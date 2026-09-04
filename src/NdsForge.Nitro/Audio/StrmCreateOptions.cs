namespace NdsForge.Nitro.Audio;

/// <summary>Controls native stream block sizing, loops, timing, encoding, and resource limits.</summary>
public sealed record StrmCreateOptions
{
    /// <summary>Gets each full channel block's byte length, including ADPCM state; defaults to 512.</summary>
    public int BlockByteLength { get; init; } = 512;
    /// <summary>Gets the optional loop's first sample frame; the exclusive end is the input frame count.</summary>
    public int? LoopStartSample { get; init; }
    /// <summary>Gets an explicit stream timer, or null to derive floor(16756991 / rate / 32).</summary>
    public ushort? Timer { get; init; }
    /// <summary>Gets sample encoding choices applied independently to each channel block.</summary>
    public NitroWaveEncodeOptions SampleEncoding { get; init; } = new();
    /// <summary>Gets bounds for the complete output, total input values, and per-channel blocks.</summary>
    public StrmReadOptions Limits { get; init; } = new();

    /// <summary>Checks option bounds independently from the selected format and source samples.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BlockByteLength);
        ArgumentNullException.ThrowIfNull(SampleEncoding);
        ArgumentNullException.ThrowIfNull(Limits);
        SampleEncoding.Validate();
        Limits.Validate();
    }
}
