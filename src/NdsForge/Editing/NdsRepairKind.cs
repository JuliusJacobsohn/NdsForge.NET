namespace NdsForge;

/// <summary>Names independently requested checksum repairs so an edit plan never hides a broad implicit fix operation.</summary>
[Flags]
public enum NdsRepairKind
{
    /// <summary>No source defect has been selected for repair.</summary>
    None = 0,

    /// <summary>Recalculates the common header CRC over bytes <c>0x000</c>-<c>0x15D</c>.</summary>
    HeaderCrc = 1 << 0,

    /// <summary>Recalculates the dedicated Nintendo-logo checksum stored at header offset <c>0x15C</c>.</summary>
    NintendoLogoCrc = 1 << 1,

    /// <summary>Recalculates every cumulative checksum field defined by the current banner version.</summary>
    BannerCrcs = 1 << 2,

    /// <summary>Recalculates the encrypted-representation secure-area checksum stored at header offset <c>0x6C</c>.</summary>
    SecureAreaCrc = 1 << 3,
}
