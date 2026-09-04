namespace NdsForge;

/// <summary>Identifies the authority slot occupied by one DSi parental-control byte.</summary>
public enum NdsDsiAgeRatingAuthority
{
    /// <summary>Selects the slot interpreted by the Japanese CERO classification system.</summary>
    Cero = 0,
    /// <summary>North American ESRB slot.</summary>
    Esrb = 1,
    /// <summary>Unassigned authority slot 2.</summary>
    Unassigned2 = 2,
    /// <summary>Selects the slot interpreted by the German USK classification system.</summary>
    Usk = 3,
    /// <summary>Selects the slot interpreted by the pan-European PEGI classification system.</summary>
    Pegi = 4,
    /// <summary>Unassigned authority slot 5.</summary>
    Unassigned5 = 5,
    /// <summary>Selects the slot interpreted by the Portuguese PEGI classification system.</summary>
    PegiPortugal = 6,
    /// <summary>United Kingdom PEGI and BBFC slot.</summary>
    UnitedKingdom = 7,
    /// <summary>Australian classification slot.</summary>
    Australia = 8,
    /// <summary>South Korean classification slot.</summary>
    Korea = 9,
    /// <summary>Unassigned authority slot 10.</summary>
    Unassigned10 = 10,
    /// <summary>Unassigned authority slot 11.</summary>
    Unassigned11 = 11,
    /// <summary>Unassigned authority slot 12.</summary>
    Unassigned12 = 12,
    /// <summary>Unassigned authority slot 13.</summary>
    Unassigned13 = 13,
    /// <summary>Unassigned authority slot 14.</summary>
    Unassigned14 = 14,
    /// <summary>Unassigned authority slot 15.</summary>
    Unassigned15 = 15,
}
