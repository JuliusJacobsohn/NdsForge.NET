namespace NdsForge;

/// <summary>Snapshots banner format, content, localized text, and physical placement without embedding rendered pixels.</summary>
public sealed class NdsManifestBanner
{
    /// <summary>Records the first native banner byte in the image.</summary>
    public long Offset { get; init; }
    /// <summary>Records the exact version-defined native structure length.</summary>
    public long Length { get; init; }
    /// <summary>Records static revision one through three or animated DSi revision <c>0x0103</c>.</summary>
    public ushort Version { get; init; }
    /// <summary>Records whether native tiles, palettes, and a playback sequence extend the static icon.</summary>
    public bool IsAnimated { get; init; }
    /// <summary>Copies every supported localized title under a stable language-name key.</summary>
    public IReadOnlyDictionary<string, string> Titles { get; init; } = new Dictionary<string, string>();
    /// <summary>Hashes the complete native banner, including reserved and unused sequence bytes.</summary>
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>Snapshots DSi-specific title, size, security-mode, and modcrypt layout metadata.</summary>
public sealed class NdsManifestDsi
{
    /// <summary>Records the 64-bit platform title identity used by DSi services and save storage.</summary>
    public ulong TitleId { get; init; }
    /// <summary>Records the DSi metadata's total content extent separately from common used size.</summary>
    public uint TotalImageSize { get; init; }
    /// <summary>Records territory permission flags without resolving policy names.</summary>
    public uint RegionFlags { get; init; }
    /// <summary>Records DSi service and hardware access-control bits.</summary>
    public uint AccessControl { get; init; }
    /// <summary>Records the complete mask of SCFG_EXT bits the application may modify.</summary>
    public uint ScfgExtMask { get; init; }
    /// <summary>Records the complete application-policy byte.</summary>
    public byte ApplicationFlags { get; init; }
    /// <summary>Records the required EULA revision byte.</summary>
    public byte EulaVersion { get; init; }
    /// <summary>Records the exact byte controlling parental-rating evaluation.</summary>
    public byte AgeRatingsUsage { get; init; }
    /// <summary>Encodes all 48 native MBK and WRAM bytes as canonical lowercase hexadecimal text.</summary>
    public string MemoryBankSettingsHex { get; init; } = string.Empty;
    /// <summary>Encodes the six shared-data file-size unit bytes in native slot order.</summary>
    public string SharedDataFileSizesHex { get; init; } = string.Empty;
    /// <summary>Encodes all sixteen authority-specific rating bytes without interpreting reserved bits.</summary>
    public string AgeRatingsHex { get; init; } = string.Empty;
    /// <summary>Records whether either header-declared modcrypt interval contains bytes.</summary>
    public bool HasModcryptAreas { get; init; }
    /// <summary>Records whether public header bytes replace the securely scrambled normal key.</summary>
    public bool UsesInsecureModcryptKey { get; init; }
    /// <summary>Records first-area placement as a transport-friendly offset/length pair.</summary>
    public NdsManifestRegion ModcryptArea1 { get; init; } = new();
    /// <summary>Records second-area placement as a transport-friendly offset/length pair.</summary>
    public NdsManifestRegion ModcryptArea2 { get; init; } = new();
}

/// <summary>Serializes a half-open image interval without relying on value-type JSON conventions.</summary>
public sealed class NdsManifestRegion
{
    /// <summary>Records the first absolute image byte.</summary>
    public long Offset { get; init; }
    /// <summary>Records the number of bytes in the interval rather than its inclusive end.</summary>
    public long Length { get; init; }
}
