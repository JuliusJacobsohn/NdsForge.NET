namespace NdsForge;

/// <summary>Identifies which DSi modcrypt interval and HMAC-derived initial counter a transformation uses.</summary>
public enum NdsModcryptArea
{
    /// <summary>Selects the first interval and the first sixteen bytes of the ARM9 HMAC.</summary>
    First = 0,

    /// <summary>Selects the second interval and the first sixteen bytes of the ARM7 HMAC.</summary>
    Second = 1,
}
