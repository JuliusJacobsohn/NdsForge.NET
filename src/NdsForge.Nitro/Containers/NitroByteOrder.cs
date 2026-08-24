namespace NdsForge.Nitro.Containers;

/// <summary>Identifies the byte-order marker used by a Nitro standard-file header.</summary>
public enum NitroByteOrder
{
    /// <summary>The header stores the ordinary <c>0xFEFF</c> marker and version representation.</summary>
    LittleEndian,

    /// <summary>The header stores the swapped marker and version representation used by some NARC producers.</summary>
    BigEndian,
}
