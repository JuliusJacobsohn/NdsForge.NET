namespace NdsForge;

/// <summary>Projects one parental-control byte while preserving its complete stored value.</summary>
/// <param name="Authority">Authority or reserved slot selected by byte position.</param>
/// <param name="RawValue">Exact stored rating and flag byte.</param>
/// <returns>A lossless authority-specific rating projection.</returns>
public readonly record struct NdsDsiAgeRating(NdsDsiAgeRatingAuthority Authority, byte RawValue)
{
    /// <summary>Decodes the low five bits as the authority-specific minimum age.</summary>
    public byte MinimumAge => (byte)(RawValue & 0x1F);

    /// <summary>Reports whether unassigned bit 5 remains set in the stored byte.</summary>
    public bool HasReservedBit => (RawValue & 0x20) != 0;

    /// <summary>Reports the authority-dependent prohibited or pending state encoded by bit 6.</summary>
    public bool IsProhibitedOrPending => (RawValue & 0x40) != 0;

    /// <summary>Gets whether this authority slot contains an applicable rating.</summary>
    public bool IsEnabled => (RawValue & 0x80) != 0;
}
