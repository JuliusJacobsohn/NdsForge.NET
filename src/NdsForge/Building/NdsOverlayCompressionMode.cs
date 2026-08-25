namespace NdsForge;

/// <summary>Selects how replacement overlay bytes become the stored FAT payload and table metadata.</summary>
public enum NdsOverlayCompressionMode
{
    /// <summary>Treats input as already stored and retains the existing compression flag.</summary>
    PreserveStorage,

    /// <summary>Stores input verbatim, clears BLZ state, and uses its length as initialized RAM size.</summary>
    Uncompressed,

    /// <summary>Encodes input with deterministic bottom-up LZ and records the resulting stored size.</summary>
    Blz,
}
