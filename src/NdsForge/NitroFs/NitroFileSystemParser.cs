namespace NdsForge;

/// <summary>Reconciles the linked FNT hierarchy with the flat FAT allocation array under explicit safety limits.</summary>
internal static class NitroFileSystemParser
{
    /// <summary>Distinguishes directory references from ordinary zero-based file identifiers.</summary>
    private const ushort RootDirectoryId = 0xF000;

    /// <summary>Reads and decodes both filesystem tables synchronously after validating declared allocation sizes.</summary>
    /// <param name="source">Image source retained by every resulting <see cref="NdsFile"/>.</param>
    /// <param name="header">Header regions locating the FNT and FAT.</param>
    /// <param name="options">Limits for allocation, directory count, and recursion.</param>
    /// <returns>A fully linked tree plus all FAT allocations.</returns>
    public static NdsFileSystem Parse(
        IImageDataSource source,
        NdsHeader header,
        NdsReadOptions options)
    {
        (byte[] fnt, byte[] fat) = ReadTables(source, header, options);
        return ParseTables(source, fnt, fat, options);
    }

    /// <summary>Reads both filesystem tables asynchronously, then performs deterministic in-memory decoding.</summary>
    /// <param name="source">Image source retained by every resulting <see cref="NdsFile"/>.</param>
    /// <param name="header">Header regions locating the FNT and FAT.</param>
    /// <param name="options">Limits for allocation, directory count, and recursion.</param>
    /// <param name="cancellationToken">Cancels table I/O before object publication.</param>
    /// <returns>A fully linked tree plus all FAT allocations.</returns>
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

    /// <summary>Materializes validated table regions for the synchronous parser without accepting partial reads.</summary>
    /// <param name="source">Random-access image bytes.</param>
    /// <param name="header">Source offsets and lengths decoded from the base header.</param>
    /// <param name="options">Allocation ceilings checked before creating arrays.</param>
    /// <returns>Independent FNT and FAT buffers safe for recursive decoding.</returns>
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

    /// <summary>Traverses reachable directory IDs, assigns sequential file IDs, and rejects cycles or dangling records.</summary>
    /// <param name="source">Shared source later used for lazy payload reads.</param>
    /// <param name="fnt">Complete filename table containing main records and variable-length subtables.</param>
    /// <param name="fat">Complete array of eight-byte start/end records.</param>
    /// <param name="options">Traversal depth and directory-count ceilings.</param>
    /// <returns>A filesystem published only after every declared directory is reachable exactly once.</returns>
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

        // This local recursion shares the cycle set and partially built indexes above. Keeping that
        // state lexical prevents malformed images from observing or retaining an incomplete tree.
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

    /// <summary>Decodes FAT half-open ranges and proves that no start/end pair escapes the physical image.</summary>
    /// <param name="imageLength">Authoritative source length rather than the header's claimed used size.</param>
    /// <param name="fat">Bytes whose length must be an exact multiple of the eight-byte record size.</param>
    /// <returns>Allocations indexed so array position always equals <see cref="NdsFileAllocation.FileId"/>.</returns>
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

    /// <summary>Guards allocations and requires the FNT/FAT pair to be jointly present or jointly absent.</summary>
    /// <param name="header">Declared table lengths from offsets <c>0x40</c> through <c>0x4F</c>.</param>
    /// <param name="options">Caller-controlled ceilings already checked for positive values.</param>
    private static void ValidateTableLengths(NdsHeader header, NdsReadOptions options)
    {
        if (header.FileNameTable.Length > options.MaximumFileNameTableBytes ||
            header.FileAllocationTable.Length > options.MaximumFileAllocationTableBytes ||
            header.FileNameTable.Length > Array.MaxLength ||
            header.FileAllocationTable.Length > Array.MaxLength)
        {
            throw new InvalidDataException("A NitroFS table exceeds the configured parsing limits.");
        }

        if (header.FileNameTable.IsEmpty && !header.FileAllocationTable.IsEmpty)
        {
            throw new InvalidDataException("A NitroFS allocation table cannot be interpreted without a filename table.");
        }
    }

    /// <summary>Rejects names unsafe for logical lookup or extraction and enforces uniqueness within one subtable.</summary>
    /// <param name="name">Decoded ASCII entry segment without directory-ID metadata.</param>
    /// <param name="siblingNames">Ordinal set shared by both file and directory children of one parent.</param>
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
