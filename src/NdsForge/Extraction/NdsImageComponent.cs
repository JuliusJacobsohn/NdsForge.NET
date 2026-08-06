namespace NdsForge;

/// <summary>Identifies image components that can be exported.</summary>
[Flags]
public enum NdsImageComponent
{
    /// <summary>No components.</summary>
    None = 0,

    /// <summary>The parsed header bytes.</summary>
    Header = 1 << 0,

    /// <summary>ARM7, ARM9, ARM7i, and ARM9i programs.</summary>
    Programs = 1 << 1,

    /// <summary>The raw filename and file-allocation tables.</summary>
    FileSystemTables = 1 << 2,

    /// <summary>Named NitroFS files.</summary>
    NitroFileSystem = 1 << 3,

    /// <summary>Overlay tables and resolved payloads.</summary>
    Overlays = 1 << 4,

    /// <summary>The raw banner.</summary>
    Banner = 1 << 5,

    /// <summary>The raw Nintendo logo data from the header.</summary>
    Logo = 1 << 6,

    /// <summary>Every supported component.</summary>
    All = Header | Programs | FileSystemTables | NitroFileSystem | Overlays | Banner | Logo,
}

