namespace NdsForge.Nitro.Containers;

/// <summary>Identifies the integer byte order used by a Nintendo DS resource.</summary>
public enum NitroByteOrder
{
    /// <summary>Multi-byte integers store their least-significant byte first.</summary>
    LittleEndian,

    /// <summary>Multi-byte integers store their most-significant byte first.</summary>
    BigEndian,
}
