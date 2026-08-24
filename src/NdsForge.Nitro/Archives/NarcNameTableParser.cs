using System.Buffers.Binary;
using System.Text;

namespace NdsForge.Nitro.Archives;

/// <summary>Links the standard Nitro filename table to an already bounded NARC allocation array.</summary>
internal static class NarcNameTableParser
{
    private const ushort RootDirectoryId = 0xF000;

    /// <summary>Returns a complete reachable directory graph while leaving unreferenced FAT entries unnamed.</summary>
    public static (NarcDirectory Root, IReadOnlyList<NarcDirectory> Directories) Parse(
        byte[] table,
        IReadOnlyList<NarcFile> files,
        NarcReadOptions options)
    {
        var unnamedRoot = new NarcDirectory(RootDirectoryId, string.Empty, "/", null);
        unnamedRoot.SetChildren([], []);
        if (table.Length == 0)
        {
            return (unnamedRoot, [unnamedRoot]);
        }

        if (table.Length < 8)
        {
            throw new InvalidDataException("The NARC filename table is smaller than its root record.");
        }

        int directoryCount = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(6));
        int rootSubtable = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(table));
        if (directoryCount == 1 && rootSubtable < 8)
        {
            return (unnamedRoot, [unnamedRoot]);
        }

        if (directoryCount <= 0 || directoryCount > options.MaximumDirectoryCount ||
            directoryCount > table.Length / 8)
        {
            throw new InvalidDataException($"The NARC directory count {directoryCount} is invalid.");
        }

        var directories = new Dictionary<ushort, NarcDirectory>();
        var visiting = new HashSet<ushort>();
        var namedFileIds = new HashSet<int>();
        NarcDirectory root = ReadDirectory(RootDirectoryId, string.Empty, "/", null, 0);
        if (directories.Count != directoryCount)
        {
            throw new InvalidDataException(
                $"The NARC declares {directoryCount} directories, but {directories.Count} are reachable.");
        }

        return (root, directories.Values.OrderBy(static value => value.Id).ToArray());

        NarcDirectory ReadDirectory(
            ushort directoryId,
            string name,
            string fullPath,
            NarcDirectory? parent,
            int depth)
        {
            if (depth > options.MaximumDirectoryDepth || directoryId < RootDirectoryId)
            {
                throw new InvalidDataException($"NARC directory 0x{directoryId:X4} exceeds configured limits.");
            }

            int directoryIndex = directoryId - RootDirectoryId;
            if (directoryIndex >= directoryCount || !visiting.Add(directoryId) || directories.ContainsKey(directoryId))
            {
                throw new InvalidDataException($"NARC directory 0x{directoryId:X4} is invalid, repeated, or cyclic.");
            }

            int recordOffset = directoryIndex * 8;
            int subtableOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(recordOffset)));
            int fileId = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(recordOffset + 4));
            ushort recordedParent = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(recordOffset + 6));
            ushort expectedParent = parent?.Id ?? checked((ushort)directoryCount);
            if (recordedParent != expectedParent || subtableOffset < directoryCount * 8 || subtableOffset >= table.Length)
            {
                throw new InvalidDataException($"NARC directory record 0x{directoryId:X4} is inconsistent.");
            }

            var directory = new NarcDirectory(directoryId, name, fullPath, parent);
            directories.Add(directoryId, directory);
            var childDirectories = new List<NarcDirectory>();
            var childFiles = new List<NarcFile>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            int cursor = subtableOffset;
            while (cursor < table.Length)
            {
                byte descriptor = table[cursor++];
                if (descriptor == 0)
                {
                    directory.SetChildren(childDirectories, childFiles);
                    visiting.Remove(directoryId);
                    return directory;
                }

                bool isDirectory = (descriptor & 0x80) != 0;
                int nameLength = descriptor & 0x7F;
                if (nameLength == 0 || cursor > table.Length - nameLength)
                {
                    throw new InvalidDataException($"NARC directory 0x{directoryId:X4} contains a truncated name.");
                }

                string childName = Encoding.Latin1.GetString(table.AsSpan(cursor, nameLength));
                cursor += nameLength;
                ValidateName(childName, names);
                string childPath = fullPath == "/" ? "/" + childName : fullPath + "/" + childName;
                if (isDirectory)
                {
                    if (cursor > table.Length - sizeof(ushort))
                    {
                        throw new InvalidDataException($"NARC child directory '{childPath}' has no identifier.");
                    }

                    ushort childId = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(cursor));
                    cursor += sizeof(ushort);
                    childDirectories.Add(ReadDirectory(childId, childName, childPath, directory, depth + 1));
                    continue;
                }

                if ((uint)fileId >= (uint)files.Count || !namedFileIds.Add(fileId))
                {
                    throw new InvalidDataException($"NARC file '{childPath}' has an invalid or repeated FAT ID {fileId}.");
                }

                NarcFile file = files[fileId++];
                file.SetName(childName, childPath, directory);
                childFiles.Add(file);
            }

            throw new InvalidDataException($"NARC directory 0x{directoryId:X4} has no terminator.");
        }
    }

    /// <summary>Enforces unambiguous slash-path lookup while retaining one-to-one Latin-1 names.</summary>
    private static void ValidateName(string name, HashSet<string> siblings)
    {
        if (name is "" or "." or ".." ||
            name.Contains('/', StringComparison.Ordinal) ||
            name.Contains('\\', StringComparison.Ordinal) ||
            !siblings.Add(name))
        {
            throw new InvalidDataException($"The NARC filename '{name}' is unsafe or duplicated.");
        }
    }
}
