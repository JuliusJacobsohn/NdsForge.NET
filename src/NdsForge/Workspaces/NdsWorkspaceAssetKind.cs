namespace NdsForge;

/// <summary>Identifies a byte-bearing workspace input independently from its host filename.</summary>
public enum NdsWorkspaceAssetKind
{
    /// <summary>Preserves the complete parsed common or extended header, including unknown fields.</summary>
    Header = 0,
    /// <summary>Preserves the primary executable together with its optional adjacent SDK footer.</summary>
    Arm9 = 1,
    /// <summary>Preserves the secondary original-mode executable.</summary>
    Arm7 = 2,
    /// <summary>Preserves the primary DSi-mode executable tuple's stored bytes.</summary>
    Arm9i = 3,
    /// <summary>Preserves the secondary DSi-mode executable tuple's stored bytes.</summary>
    Arm7i = 4,
    /// <summary>Preserves directory ordering and raw NitroFS filename-table bytes.</summary>
    FileNameTable = 5,
    /// <summary>Preserves the complete File ID to physical-region mapping.</summary>
    FileAllocationTable = 6,
    /// <summary>Preserves ARM9 overlay records in their original table order.</summary>
    Arm9OverlayTable = 7,
    /// <summary>Preserves ARM7 overlay records in their original table order.</summary>
    Arm7OverlayTable = 8,
    /// <summary>Preserves one named, overlay-linked, or otherwise unreferenced FAT allocation.</summary>
    Allocation = 9,
    /// <summary>Preserves all native menu metadata and image frames.</summary>
    Banner = 10,
    /// <summary>Preserves the optional executable used by a debug-capable image.</summary>
    DebugProgram = 11,
    /// <summary>Preserves carrier-only bytes between the parsed header and ordinary program storage.</summary>
    PostHeader = 12,
    /// <summary>Preserves the cartridge-only opaque reservation before ARM9i.</summary>
    TwlReservation = 13,
    /// <summary>Preserves first-level stored DSi authentication entries.</summary>
    SectorHashTable = 14,
    /// <summary>Preserves second-level stored DSi authentication entries.</summary>
    BlockHashTable = 15,
    /// <summary>Preserves the recognized opaque signature immediately after common used content.</summary>
    DownloadPlaySignature = 16,
}
