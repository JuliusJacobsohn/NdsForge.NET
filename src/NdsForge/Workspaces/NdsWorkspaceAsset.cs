using System.Text.Json.Serialization;

namespace NdsForge;

/// <summary>Locates an independently named workspace input and records its original image identity.</summary>
public sealed record NdsWorkspaceAsset
{
    /// <summary>Identifies which native component consumes these bytes, independently from host path names.</summary>
    [JsonRequired]
    public NdsWorkspaceAssetKind Kind { get; init; }

    /// <summary>Identifies an allocation's original FAT record; non-allocation components leave this absent.</summary>
    public int? FileId { get; init; }

    /// <summary>Names a canonical portable path relative to the workspace directory, never an external file.</summary>
    [JsonRequired]
    public string Path { get; init; } = string.Empty;

    /// <summary>Records the original absolute start within the source image, not a host-file seek position.</summary>
    [JsonRequired]
    public long OriginalOffset { get; init; }

    /// <summary>Records stored source bytes before any explicitly requested structural edit.</summary>
    [JsonRequired]
    public long OriginalLength { get; init; }

    /// <summary>Records the lowercase SHA-256 identity of the originally exported component bytes.</summary>
    [JsonRequired]
    public string OriginalSha256 { get; init; } = string.Empty;
}
