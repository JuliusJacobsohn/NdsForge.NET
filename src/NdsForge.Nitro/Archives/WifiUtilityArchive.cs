using System.Buffers.Binary;

namespace NdsForge.Nitro.Archives;

/// <summary>Reads the plain FNT/FAT asset container used by the SDK Wi-Fi utility.</summary>
/// <remarks>The envelope has no magic or established version field. A leading 0x10 is commonly a table offset, not compression.</remarks>
public sealed class WifiUtilityArchive
{
    private readonly byte[] _source;
    private readonly IReadOnlyDictionary<string, WifiUtilityFile> _paths;

    /// <summary>Publishes a validated source with all allocation and name relationships resolved.</summary>
    private WifiUtilityArchive(byte[] source, int nameOffset, int nameLength, int fatOffset, int fatLength,
        WifiUtilityFile[] files, WifiUtilityDirectory[] directories, IReadOnlyList<WifiUtilityNameEntry> names)
    {
        _source = source;
        NameTableOffset = nameOffset;
        NameTableLength = nameLength;
        AllocationTableOffset = fatOffset;
        AllocationTableLength = fatLength;
        Files = Array.AsReadOnly(files);
        Directories = Array.AsReadOnly(directories);
        NameEntries = names;
        _paths = files.Where(static file => file.FullPath is not null)
            .ToDictionary(static file => file.FullPath!, StringComparer.Ordinal);
    }

    /// <summary>Gets all FAT allocations in native identity order, including unnamed and empty entries.</summary>
    public IReadOnlyList<WifiUtilityFile> Files { get; }
    /// <summary>Gets all reachable directories in native identity order.</summary>
    public IReadOnlyList<WifiUtilityDirectory> Directories { get; }
    /// <summary>Gets the directory with native identifier 0xF000.</summary>
    public WifiUtilityDirectory Root => Directories[0];
    /// <summary>Gets the absolute filename-table offset from the envelope.</summary>
    public int NameTableOffset { get; }
    /// <summary>Gets the complete declared filename-table length, including its opaque padding.</summary>
    public int NameTableLength { get; }
    /// <summary>Gets the absolute allocation-table offset from the envelope.</summary>
    public int AllocationTableOffset { get; }
    /// <summary>Gets the complete allocation-table length, exactly eight bytes per file identity.</summary>
    public int AllocationTableLength { get; }

    /// <summary>Parses a copied plain archive with bounded counts, ranges, and directory relationships.</summary>
    /// <param name="data">Complete archive allocation; any trailing bytes are retained for preservation.</param>
    /// <param name="options">Input-memory and graph limits, or conservative defaults.</param>
    /// <returns>An archive independent of the caller's source buffer.</returns>
    public static WifiUtilityArchive Parse(ReadOnlySpan<byte> data, WifiUtilityReadOptions? options = null)
    {
        options ??= new();
        options.Validate();
        if (data.Length < 16 || data.Length > options.MaximumArchiveBytes)
        {
            throw new InvalidDataException("Utility archive input is truncated or exceeds its byte limit.");
        }
        (int nameOffset, int nameLength) = ReadRange(data, 0);
        (int fatOffset, int fatLength) = ReadRange(data, 8);
        if (nameOffset < 16 || nameLength < 9 || fatOffset < 16 || fatLength % 8 != 0 ||
            Intersects(nameOffset, nameLength, fatOffset, fatLength) || fatLength / 8 > options.MaximumFileCount)
        {
            throw new InvalidDataException("Utility name/allocation table bounds or allocation count are invalid.");
        }
        byte[] source = data.ToArray();
        var files = new WifiUtilityFile[fatLength / 8];
        for (int id = 0; id < files.Length; id++)
        {
            int record = fatOffset + id * 8;
            uint start = BinaryPrimitives.ReadUInt32LittleEndian(data[record..]);
            uint end = BinaryPrimitives.ReadUInt32LittleEndian(data[(record + 4)..]);
            if (start > end || end > source.Length || (start != end &&
                (start < 16 || Intersects(start, end - start, nameOffset, nameLength) ||
                Intersects(start, end - start, fatOffset, fatLength))))
            {
                throw new InvalidDataException("A utility allocation is reversed, out of bounds, or overlaps metadata.");
            }
            files[id] = new(id, (int)start, source.AsMemory((int)start, (int)(end - start)));
        }
        (WifiUtilityDirectory[] directories, IReadOnlyList<WifiUtilityNameEntry> names) =
            WifiUtilityNameTable.Parse(source.AsSpan(nameOffset, nameLength), files, options);
        return new(source, nameOffset, nameLength, fatOffset, fatLength, files, directories, names);
    }

    /// <summary>Finds a named file without interpreting the archive path as a host filesystem path.</summary>
    /// <param name="fullPath">Exact case-sensitive slash-prefixed archive path.</param>
    /// <returns>The matching allocation, or null when absent.</returns>
    public WifiUtilityFile? FindFile(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        return _paths.GetValueOrDefault(fullPath);
    }

    /// <summary>Creates a detached edit recipe retaining file and directory identities.</summary>
    /// <returns>An independent builder for replacement, renaming, and deterministic output.</returns>
    public WifiUtilityArchiveBuilder CreateBuilder() => new(this);

    /// <summary>Returns an independent byte-exact source copy, including unused ranges and trailing bytes.</summary>
    /// <returns>The complete originally supplied archive allocation.</returns>
    public byte[] WritePreserved() => (byte[])_source.Clone();

    internal ReadOnlyMemory<byte> Source => _source;
    internal ReadOnlyMemory<byte> NameTable => _source.AsMemory(NameTableOffset, NameTableLength);
    internal IReadOnlyList<WifiUtilityNameEntry> NameEntries { get; }

    /// <summary>Checks unsigned envelope fields before converting them to managed slice lengths.</summary>
    private static (int Offset, int Length) ReadRange(ReadOnlySpan<byte> data, int position)
    {
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 4)..]);
        if (offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException("A utility envelope table extends outside the input.");
        }
        return ((int)offset, (int)length);
    }

    /// <summary>Excludes empty intervals from overlap checks without unchecked endpoint arithmetic.</summary>
    private static bool Intersects(long first, long firstLength, long second, long secondLength) =>
        firstLength != 0 && secondLength != 0 && first < second + secondLength && second < first + firstLength;
}
