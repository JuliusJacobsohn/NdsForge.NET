namespace NdsForge;

/// <summary>
/// Couples a serialized NitroFS name table with the payload and directory-ID assignments it encodes.
/// </summary>
internal sealed record NdsFileSystemBuildSnapshot
{
    /// <summary>Creates one internally consistent set of inputs for the later image-layout phase.</summary>
    /// <param name="fileNameTable">Complete serialized FNT bytes.</param>
    /// <param name="filesInIdOrder">Payloads ordered by the file IDs encoded in the FNT.</param>
    /// <param name="directoryIds">Canonical paths paired with the IDs encoded in child entries.</param>
    public NdsFileSystemBuildSnapshot(
        byte[] fileNameTable,
        IReadOnlyList<NdsBuildFile> filesInIdOrder,
        IReadOnlyDictionary<string, ushort> directoryIds)
    {
        FileNameTable = fileNameTable;
        FilesInIdOrder = filesInIdOrder;
        DirectoryIds = directoryIds;
    }

    /// <summary>
    /// Contains the complete main table and directory subtables ready for the ROM header's FNT region.
    /// </summary>
    public byte[] FileNameTable { get; }

    /// <summary>
    /// Couples each zero-based list position to the FAT file ID used by directory records.
    /// </summary>
    public IReadOnlyList<NdsBuildFile> FilesInIdOrder { get; }

    /// <summary>
    /// Records the stable NitroFS directory IDs, beginning with root at <c>0xF000</c>.
    /// </summary>
    public IReadOnlyDictionary<string, ushort> DirectoryIds { get; }
}
