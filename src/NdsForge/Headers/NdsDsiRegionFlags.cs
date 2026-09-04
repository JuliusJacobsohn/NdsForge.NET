namespace NdsForge;

/// <summary>Identifies defined territory-permission bits in the DSi extended header.</summary>
[Flags]
public enum NdsDsiRegionPermissions
{
    /// <summary>No defined territory is enabled.</summary>
    None = 0,

    /// <summary>Permits launch under the Japanese DSi territory setting.</summary>
    Japan = 1 << 0,

    /// <summary>United States and Canada.</summary>
    NorthAmerica = 1 << 1,

    /// <summary>Permits launch under European DSi territory settings.</summary>
    Europe = 1 << 2,

    /// <summary>Australia and New Zealand.</summary>
    Australia = 1 << 3,

    /// <summary>Permits launch under the mainland Chinese DSi territory setting.</summary>
    China = 1 << 4,

    /// <summary>Permits launch under the South Korean DSi territory setting.</summary>
    Korea = 1 << 5,
}
