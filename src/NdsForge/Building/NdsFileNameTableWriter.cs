using System.Buffers.Binary;
using System.Text;

namespace NdsForge;

/// <summary>
/// Translates the path-oriented build model into the compact linked-directory representation used on cartridge.
/// </summary>
internal static class NdsFileNameTableWriter
{
    /// <summary>
    /// Marks the beginning of the 12-bit directory index range defined by the NitroFS FNT encoding.
    /// </summary>
    private const ushort RootDirectoryId = 0xF000;

    /// <summary>
    /// Assigns deterministic file/directory IDs and serializes the FNT main table and name subtables.
    /// </summary>
    /// <param name="directories">Canonical directories in their desired numeric-ID order, with root first.</param>
    /// <param name="files">Validated files whose parent paths occur in <paramref name="directories"/>.</param>
    /// <param name="firstFileId">FAT ID assigned to the first visible payload; lower IDs may belong to hidden Overlays.</param>
    /// <returns>A synchronized table, payload order, and directory-ID map for subsequent ROM layout.</returns>
    public static NdsFileSystemBuildSnapshot Write(
        IReadOnlyList<string> directories,
        IReadOnlyCollection<NdsBuildFile> files,
        int firstFileId)
    {
        var directoryIds = directories
            .Select((path, index) => (path, id: checked((ushort)(RootDirectoryId + index))))
            .ToDictionary(static value => value.path, static value => value.id, StringComparer.Ordinal);
        var filesByDirectory = files
            .GroupBy(static file => GetParent(file.Path), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static file => file.Path, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var childrenByDirectory = directories
            .Where(static path => path != "/")
            .GroupBy(static path => GetParent(path), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var orderedFiles = new List<NdsBuildFile>();
        var subtables = new byte[directories.Count][];
        var firstFileIds = new ushort[directories.Count];
        for (int index = 0; index < directories.Count; index++)
        {
            string directory = directories[index];
            firstFileIds[index] = checked((ushort)(firstFileId + orderedFiles.Count));
            NdsBuildFile[] directoryFiles = filesByDirectory.GetValueOrDefault(directory) ?? [];
            string[] childDirectories = childrenByDirectory.GetValueOrDefault(directory) ?? [];
            orderedFiles.AddRange(directoryFiles);
            subtables[index] = WriteSubtable(directoryFiles, childDirectories, directoryIds);
        }

        int mainTableLength = checked(directories.Count * 8);
        int totalLength = checked(mainTableLength + subtables.Sum(static data => data.Length));
        byte[] fnt = new byte[totalLength];
        int subtableOffset = mainTableLength;
        for (int index = 0; index < directories.Count; index++)
        {
            string directory = directories[index];
            int recordOffset = index * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(fnt.AsSpan(recordOffset), checked((uint)subtableOffset));
            BinaryPrimitives.WriteUInt16LittleEndian(fnt.AsSpan(recordOffset + 4), firstFileIds[index]);
            ushort parent = directory == "/"
                ? checked((ushort)directories.Count)
                : directoryIds[GetParent(directory)];
            BinaryPrimitives.WriteUInt16LittleEndian(fnt.AsSpan(recordOffset + 6), parent);
            subtables[index].CopyTo(fnt, subtableOffset);
            subtableOffset += subtables[index].Length;
        }

        return new(fnt, orderedFiles, directoryIds, firstFileId);
    }

    /// <summary>
    /// Encodes one directory's file names followed by child-directory names and their 16-bit IDs.
    /// </summary>
    /// <param name="files">Files in the order implied by the directory record's first-file ID.</param>
    /// <param name="childDirectories">Immediate children only, already in deterministic name order.</param>
    /// <param name="directoryIds">Concrete numeric IDs referenced after directory-name entries.</param>
    /// <returns>A zero-terminated NitroFS directory subtable.</returns>
    private static byte[] WriteSubtable(
        IReadOnlyList<NdsBuildFile> files,
        IReadOnlyList<string> childDirectories,
        Dictionary<string, ushort> directoryIds)
    {
        using var stream = new MemoryStream();
        foreach (NdsBuildFile file in files)
        {
            WriteName(stream, GetName(file.Path), isDirectory: false);
        }

        foreach (string directory in childDirectories)
        {
            WriteName(stream, GetName(directory), isDirectory: true);
            ushort id = directoryIds[directory];
            stream.WriteByte((byte)id);
            stream.WriteByte((byte)(id >> 8));
        }

        stream.WriteByte(0);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes the length/type prefix and one-to-one eight-bit name bytes shared by file and directory entries.
    /// </summary>
    /// <param name="stream">The in-memory subtable receiving bytes at its current position.</param>
    /// <param name="name">A previously validated single NitroFS path segment.</param>
    /// <param name="isDirectory">Selects bit 7 of the prefix; directory IDs are written by the caller.</param>
    private static void WriteName(Stream stream, string name, bool isDirectory)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(name);
        stream.WriteByte((byte)(bytes.Length | (isDirectory ? 0x80 : 0)));
        stream.Write(bytes);
    }

    /// <summary>Separates a canonical image path using NitroFS rather than host-filesystem rules.</summary>
    /// <param name="path">A validated absolute NitroFS path.</param>
    /// <returns>The canonical parent path, including <c>/</c> for root children.</returns>
    private static string GetParent(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator == 0 ? "/" : path[..separator];
    }

    /// <summary>Extracts the one-byte name projection represented by the final path segment.</summary>
    /// <param name="path">A validated absolute NitroFS path.</param>
    /// <returns>The segment serialized into a directory subtable.</returns>
    private static string GetName(string path) => path[(path.LastIndexOf('/') + 1)..];
}
