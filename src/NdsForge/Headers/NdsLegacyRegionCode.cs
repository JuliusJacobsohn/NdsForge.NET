namespace NdsForge;

/// <summary>Projects an original-DS territory byte without rejecting unassigned values.</summary>
/// <param name="RawValue">Complete stored byte, including values not assigned by the public format.</param>
/// <returns>A lossless original-DS territory projection.</returns>
public readonly record struct NdsLegacyRegion(byte RawValue)
{
    /// <summary>Identifies ordinary region-independent original-DS software.</summary>
    public static NdsLegacyRegion Worldwide { get; } = new(0);

    /// <summary>Identifies software using the Korean original-DS territory value.</summary>
    public static NdsLegacyRegion Korea { get; } = new(0x40);

    /// <summary>Identifies software using the mainland Chinese original-DS territory value.</summary>
    public static NdsLegacyRegion China { get; } = new(0x80);
}
