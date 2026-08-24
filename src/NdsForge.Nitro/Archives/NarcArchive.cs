using System.Buffers.Binary;
using NdsForge.Nitro.Containers;

namespace NdsForge.Nitro.Archives;

/// <summary>Provides bounded access to a Nintendo NARC allocation table, optional names, and payloads.</summary>
public sealed class NarcArchive
{
    private const int StandardHeaderLength = 0x10;
    private readonly byte[] _nameTable;
    private readonly byte[] _originalData;
    private readonly IReadOnlyDictionary<string, NarcFile> _filesByPath;

    /// <summary>Publishes a fully validated archive and retains private source bytes for preservation writes.</summary>
    private NarcArchive(
        NitroByteOrder headerByteOrder,
        byte[] originalData,
        byte[] nameTable,
        IReadOnlyList<NarcFile> files,
        NarcDirectory root,
        IReadOnlyList<NarcDirectory> directories)
    {
        HeaderByteOrder = headerByteOrder;
        _originalData = originalData;
        _nameTable = nameTable;
        Files = files;
        Root = root;
        Directories = directories;
        _filesByPath = files
            .Where(static file => file.FullPath is not null)
            .ToDictionary(static file => file.FullPath!, StringComparer.Ordinal);
    }

    /// <summary>Gets the marker/version representation used by the standard-file header.</summary>
    public NitroByteOrder HeaderByteOrder { get; }

    /// <summary>Gets every allocation in stable FAT identifier order, including unnamed entries.</summary>
    public IReadOnlyList<NarcFile> Files { get; }

    /// <summary>Gets the root of the optional filename hierarchy.</summary>
    public NarcDirectory Root { get; }

    /// <summary>Gets every reachable directory in numeric identifier order.</summary>
    public IReadOnlyList<NarcDirectory> Directories { get; }

    /// <summary>Creates a deterministic archive whose allocations intentionally have no filename entries.</summary>
    /// <param name="files">Payloads in the stable identifier order assigned by the new FAT.</param>
    /// <param name="headerByteOrder">Marker/version representation selected for the standard-file header.</param>
    /// <returns>A parsed archive ready for lookup, replacement, or writing.</returns>
    public static NarcArchive CreateUnnamed(
        IReadOnlyList<ReadOnlyMemory<byte>> files,
        NitroByteOrder headerByteOrder = NitroByteOrder.BigEndian)
    {
        ArgumentNullException.ThrowIfNull(files);
        var entries = new NarcFile[files.Count];
        for (int fileId = 0; fileId < entries.Length; fileId++)
        {
            entries[fileId] = new(fileId, files[fileId].ToArray(), 0);
        }

        byte[] nameTable = [4, 0, 0, 0, 0, 0, 1, 0];
        var root = new NarcDirectory(0xF000, string.Empty, "/", null);
        root.SetChildren([], []);
        var provisional = new NarcArchive(headerByteOrder, [], nameTable, entries, root, [root]);
        byte[] encoded = provisional.CreateBuilder().Build(new NarcWriteOptions
        {
            HeaderByteOrder = headerByteOrder,
            PreserveSourceLayout = false,
        });
        return Parse(encoded);
    }

    /// <summary>Parses a copied NARC payload under caller-configurable count and depth limits.</summary>
    /// <param name="data">Complete archive bytes, optionally followed by allocation padding.</param>
    /// <param name="options">Safety limits, or defaults suitable for trusted local files.</param>
    /// <returns>A fully linked archive whose payload memory cannot alias the caller's buffer.</returns>
    public static NarcArchive Parse(ReadOnlySpan<byte> data, NarcReadOptions? options = null)
    {
        options ??= new NarcReadOptions();
        options.Validate();
        if (data.Length < StandardHeaderLength || !data[..4].SequenceEqual("NARC"u8))
        {
            throw new InvalidDataException("The input does not begin with a complete NARC header.");
        }

        NitroByteOrder byteOrder = ReadHeaderByteOrder(data);
        uint rawDeclaredLength = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (rawDeclaredLength < StandardHeaderLength || rawDeclaredLength > data.Length ||
            headerLength != StandardHeaderLength || blockCount != 3)
        {
            throw new InvalidDataException("The NARC length, header size, or block count is invalid.");
        }

        int declaredLength = (int)rawDeclaredLength;
        ReadOnlySpan<byte> declared = data[..declaredLength];
        int fatOffset = StandardHeaderLength;
        int fatLength = ReadBlockLength(declared, fatOffset, "BTAF"u8, 12);
        uint rawFileCount = BinaryPrimitives.ReadUInt32LittleEndian(declared[(fatOffset + 8)..]);
        if (rawFileCount > options.MaximumFileCount || rawFileCount > int.MaxValue)
        {
            throw new InvalidDataException($"The NARC file count {rawFileCount} exceeds configured limits.");
        }

        int fileCount = (int)rawFileCount;
        if (fatLength != checked(12 + (fileCount * 8)))
        {
            throw new InvalidDataException("The NARC allocation block size does not match its file count.");
        }

        int fntOffset = checked(fatOffset + fatLength);
        int fntLength = ReadBlockLength(declared, fntOffset, "BTNF"u8, 8);
        int imageOffset = checked(fntOffset + fntLength);
        int imageLength = ReadBlockLength(declared, imageOffset, "GMIF"u8, 8);
        if (checked(imageOffset + imageLength) != declaredLength)
        {
            throw new InvalidDataException("The three NARC blocks do not cover the declared archive length.");
        }

        int payloadOffset = imageOffset + 8;
        int payloadLength = imageLength - 8;
        var files = new NarcFile[fileCount];
        int previousEnd = 0;
        for (int fileId = 0; fileId < fileCount; fileId++)
        {
            int entryOffset = fatOffset + 12 + (fileId * 8);
            uint rawStart = BinaryPrimitives.ReadUInt32LittleEndian(declared[entryOffset..]);
            uint rawEnd = BinaryPrimitives.ReadUInt32LittleEndian(declared[(entryOffset + 4)..]);
            if (rawStart > rawEnd || rawEnd > payloadLength || rawStart < previousEnd)
            {
                throw new InvalidDataException($"NARC FAT entry {fileId} is outside or overlaps the data block.");
            }

            int start = (int)rawStart;
            int end = (int)rawEnd;
            files[fileId] = new(fileId, declared.Slice(payloadOffset + start, end - start).ToArray(), payloadOffset + start);
            previousEnd = end;
        }

        byte[] nameTable = declared.Slice(fntOffset + 8, fntLength - 8).ToArray();
        (NarcDirectory root, IReadOnlyList<NarcDirectory> directories) =
            NarcNameTableParser.Parse(nameTable, files, options);
        return new(byteOrder, data.ToArray(), nameTable, files, root, directories);
    }

    /// <summary>Gets one file by stable identifier and rejects an out-of-range request.</summary>
    /// <param name="fileId">Zero-based allocation identifier.</param>
    /// <returns>The corresponding named or unnamed file.</returns>
    public NarcFile GetFile(int fileId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileId);
        return fileId < Files.Count ? Files[fileId] : throw new ArgumentOutOfRangeException(nameof(fileId));
    }

    /// <summary>Finds one named file by its exact slash-prefixed, case-sensitive path.</summary>
    /// <param name="fullPath">Canonical path beginning with <c>/</c>.</param>
    /// <returns>The named file, or <see langword="null"/> when the path is absent.</returns>
    public NarcFile? FindFile(string fullPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(fullPath);
        return _filesByPath.GetValueOrDefault(fullPath);
    }

    /// <summary>Creates an isolated replacement builder retaining stable identifiers and the parsed name table.</summary>
    /// <returns>A mutable payload-replacement plan detached from caller buffers.</returns>
    public NarcArchiveBuilder CreateBuilder() => new(this);

    /// <summary>Exposes private preservation state only to the paired builder.</summary>
    internal (byte[] OriginalData, byte[] NameTable) GetPreservationData() => (_originalData, _nameTable);

    /// <summary>Validates the marker/version pair while documenting that all later integers remain little-endian.</summary>
    private static NitroByteOrder ReadHeaderByteOrder(ReadOnlySpan<byte> data)
    {
        ushort marker = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        return (marker, version) switch
        {
            (0xFEFF, 0x0001) => NitroByteOrder.LittleEndian,
            (0xFFFE, 0x0100) => NitroByteOrder.BigEndian,
            _ => throw new InvalidDataException("The NARC byte-order marker or version is unsupported."),
        };
    }

    /// <summary>Checks one mandatory block signature and bounded little-endian size.</summary>
    private static int ReadBlockLength(ReadOnlySpan<byte> data, int offset, ReadOnlySpan<byte> magic, int minimumLength)
    {
        if (offset < 0 || offset > data.Length - 8 || !data.Slice(offset, 4).SequenceEqual(magic))
        {
            throw new InvalidDataException($"The NARC block at 0x{offset:X} has an invalid signature or header.");
        }

        uint rawLength = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
        if (rawLength < minimumLength || rawLength > int.MaxValue || rawLength > data.Length - offset)
        {
            throw new InvalidDataException($"The NARC block at 0x{offset:X} has an invalid length {rawLength}.");
        }

        return (int)rawLength;
    }
}
