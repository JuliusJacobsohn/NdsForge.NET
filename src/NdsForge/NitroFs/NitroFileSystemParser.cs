namespace NdsForge;

internal static class NitroFileSystemParser
{
    private const ushort RootDirectoryId = 0xF000;

    public static NdsFileSystem Parse(
        IImageDataSource source,
        NdsHeader header,
        NdsReadOptions options)
    {
        (byte[] fnt, byte[] fat) = ReadTables(source, header, options);
        return ParseTables(source, fnt, fat, options);
    }

    public static async ValueTask<NdsFileSystem> ParseAsync(
        IImageDataSource source,
        NdsHeader header,
        NdsReadOptions options,
        CancellationToken cancellationToken)
    {
        ValidateTableLengths(header, options);
        byte[] fnt = new byte[header.FileNameTable.Length];
        byte[] fat = new byte[header.FileAllocationTable.Length];
        await source.ReadExactlyAsync(
            header.FileNameTable.Offset,
            fnt,
            cancellationToken).ConfigureAwait(false);
        await source.ReadExactlyAsync(
            header.FileAllocationTable.Offset,
            fat,
            cancellationToken).ConfigureAwait(false);
        return ParseTables(source, fnt, fat, options);
    }

    private static (byte[] Fnt, byte[] Fat) ReadTables(
        IImageDataSource source,
        NdsHeader header,
        NdsReadOptions options)
    {
        ValidateTableLengths(header, options);
        byte[] fnt = new byte[header.FileNameTable.Length];
        byte[] fat = new byte[header.FileAllocationTable.Length];
        source.ReadExactly(header.FileNameTable.Offset, fnt);
        source.ReadExactly(header.FileAllocationTable.Offset, fat);
        return (fnt, fat);
    }

    private static NdsFileSystem ParseTables(
        IImageDataSource source,
        byte[] fnt,
        byte[] fat,
        NdsReadOptions options)
    {
        NdsFileAllocation[] allocations = ParseAllocations(source.Length, fat);
        if (fnt.Length == 0)
        {
            var emptyRoot = new NdsDirectory(RootDirectoryId, string.Empty, "/", null);
            return new(emptyRoot, [emptyRoot], [], allocations);
        }

        if (fnt.Length < 8)
        {
            throw new InvalidDataException("The NitroFS filename table is smaller than its root directory record.");
        }

        int directoryCount = NdsBinary.ReadUInt16(fnt, 6);
        if (directoryCount == 0 || directoryCount > options.MaximumDirectoryCount || directoryCount > fnt.Length / 8)
        {
            throw new InvalidDataException($"The NitroFS directory count {directoryCount} is invalid.");
        }

        var directoriesById = new Dictionary<ushort, NdsDirectory>();
        var files = new List<NdsFile>();
        var visiting = new HashSet<ushort>();
        NdsDirectory root = ReadDirectory(RootDirectoryId, string.Empty, "/", null, 0);
        if (directoriesById.Count != directoryCount)
        {
            throw new InvalidDataException(
                $"The NitroFS declares {directoryCount} directories, but {directoriesById.Count} are reachable from the root.");
        }

        NdsDirectory[] directories = directoriesById.Values.OrderBy(static directory => directory.Id).ToArray();
        NdsFile[] orderedFiles = files.OrderBy(static file => file.Id).ToArray();
        return new(root, directories, orderedFiles, allocations);

        NdsDirectory ReadDirectory(
            ushort directoryId,
            string name,
            string fullPath,
            NdsDirectory? parent,
            int depth)
        {
            if (depth > options.MaximumDirectoryDepth || directoryId < RootDirectoryId)
            {
                throw new InvalidDataException($"NitroFS directory 0x{directoryId:X4} exceeds the configured limits.");
            }

            int index = directoryId - RootDirectoryId;
            if (index >= directoryCount || !visiting.Add(directoryId) || directoriesById.ContainsKey(directoryId))
            {
                throw new InvalidDataException($"NitroFS directory 0x{directoryId:X4} is invalid, duplicated, or cyclic.");
            }

            int recordOffset = index * 8;
            int subTableOffset = checked((int)NdsBinary.ReadUInt32(fnt, recordOffset));
            int fileId = NdsBinary.ReadUInt16(fnt, recordOffset + 4);
            ushort recordedParent = NdsBinary.ReadUInt16(fnt, recordOffset + 6);
            ushort expectedParent = parent?.Id ?? checked((ushort)directoryCount);
            if (recordedParent != expectedParent || subTableOffset < directoryCount * 8 || subTableOffset >= fnt.Length)
            {
                throw new InvalidDataException($"NitroFS directory record 0x{directoryId:X4} is inconsistent.");
            }

            var directory = new NdsDirectory(directoryId, name, fullPath, parent);
            directoriesById.Add(directoryId, directory);
            var childDirectories = new List<NdsDirectory>();
            var childFiles = new List<NdsFile>();
            var childNames = new HashSet<string>(StringComparer.Ordinal);
            int cursor = subTableOffset;
            while (cursor < fnt.Length)
            {
                byte descriptor = fnt[cursor++];
                if (descriptor == 0)
                {
                    directory.SetChildren(childDirectories, childFiles);
                    visiting.Remove(directoryId);
                    return directory;
                }

                bool isDirectory = (descriptor & 0x80) != 0;
                int nameLength = descriptor & 0x7F;
                if (nameLength == 0 || cursor > fnt.Length - nameLength)
                {
                    throw new InvalidDataException($"NitroFS directory 0x{directoryId:X4} contains a truncated name.");
                }

                string childName = NdsBinary.ReadAscii(fnt, cursor, nameLength);
                cursor += nameLength;
                ValidateName(childName, childNames);
                string childPath = fullPath == "/" ? "/" + childName : fullPath + "/" + childName;
                if (isDirectory)
                {
                    if (cursor > fnt.Length - 2)
                    {
                        throw new InvalidDataException($"NitroFS child directory '{childPath}' has no directory ID.");
                    }

                    ushort childId = NdsBinary.ReadUInt16(fnt, cursor);
                    cursor += 2;
                    childDirectories.Add(ReadDirectory(childId, childName, childPath, directory, depth + 1));
                }
                else
                {
                    if ((uint)fileId >= allocations.Length)
                    {
                        throw new InvalidDataException($"NitroFS file '{childPath}' references missing FAT ID {fileId}.");
                    }

                    NdsFileAllocation allocation = allocations[fileId];
                    var file = new NdsFile(source, fileId, childName, childPath, allocation.Data, directory);
                    childFiles.Add(file);
                    files.Add(file);
                    fileId++;
                }
            }

            throw new InvalidDataException($"NitroFS directory 0x{directoryId:X4} has no terminator.");
        }
    }

    private static NdsFileAllocation[] ParseAllocations(long imageLength, ReadOnlySpan<byte> fat)
    {
        if (fat.Length % 8 != 0)
        {
            throw new InvalidDataException("The NitroFS file allocation table length is not a multiple of eight.");
        }

        var allocations = new NdsFileAllocation[fat.Length / 8];
        for (int fileId = 0; fileId < allocations.Length; fileId++)
        {
            int offset = fileId * 8;
            uint start = NdsBinary.ReadUInt32(fat, offset);
            uint end = NdsBinary.ReadUInt32(fat, offset + 4);
            if (end < start || end > imageLength)
            {
                throw new InvalidDataException($"NitroFS FAT entry {fileId} lies outside the image.");
            }

            allocations[fileId] = new(fileId, new(start, end - start));
        }

        return allocations;
    }

    private static void ValidateTableLengths(NdsHeader header, NdsReadOptions options)
    {
        if (header.FileNameTable.Length > options.MaximumFileNameTableBytes ||
            header.FileAllocationTable.Length > options.MaximumFileAllocationTableBytes ||
            header.FileNameTable.Length > Array.MaxLength ||
            header.FileAllocationTable.Length > Array.MaxLength)
        {
            throw new InvalidDataException("A NitroFS table exceeds the configured parsing limits.");
        }

        if (header.FileNameTable.IsEmpty != header.FileAllocationTable.IsEmpty)
        {
            throw new InvalidDataException("NitroFS filename and allocation tables must either both be present or both be absent.");
        }
    }

    private static void ValidateName(string name, HashSet<string> siblingNames)
    {
        if (string.IsNullOrEmpty(name) ||
            name is "." or ".." ||
            name.Contains('/', StringComparison.Ordinal) ||
            name.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidDataException("NitroFS contains an unsafe or empty entry name.");
        }

        if (!siblingNames.Add(name))
        {
            throw new InvalidDataException($"NitroFS contains duplicate sibling name '{name}'.");
        }
    }
}
