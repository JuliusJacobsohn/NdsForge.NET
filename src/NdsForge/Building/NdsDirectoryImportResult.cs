namespace NdsForge;

/// <summary>Reports the exact staged host content applied to a NitroFS builder by one transactional import.</summary>
/// <param name="FilesImported">Payloads newly added or explicitly replaced.</param>
/// <param name="DirectoriesImported">Previously absent directory paths created, including empty directories.</param>
/// <param name="BytesImported">Total bytes copied for applied payloads rather than merely scanned files.</param>
/// <param name="EntriesSkipped">Existing files retained and host links omitted under explicit policies.</param>
public sealed record NdsDirectoryImportResult(
    int FilesImported,
    int DirectoriesImported,
    long BytesImported,
    int EntriesSkipped);
