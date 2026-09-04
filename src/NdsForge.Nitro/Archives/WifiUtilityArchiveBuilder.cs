using System.Buffers.Binary;

namespace NdsForge.Nitro.Archives;

/// <summary>Replaces and renames utility assets without renumbering file or directory identities.</summary>
public sealed class WifiUtilityArchiveBuilder
{
    private readonly WifiUtilityArchive _source;
    private readonly Dictionary<int, byte[]> _replacements = [];
    private readonly Dictionary<(bool Directory, int Id), string> _names = [];

    /// <summary>Retains the source's private immutable-by-convention storage and separately owns all edited bytes.</summary>
    internal WifiUtilityArchiveBuilder(WifiUtilityArchive source) => _source = source;

    /// <summary>Replaces a complete allocation, including one without a filename.</summary>
    /// <param name="fileId">Stable zero-based FAT identity.</param>
    /// <param name="data">Payload copied immediately; no automatic compression or embedded-image interpretation occurs.</param>
    /// <returns>This builder.</returns>
    public WifiUtilityArchiveBuilder ReplaceFile(int fileId, ReadOnlySpan<byte> data)
    {
        RequireFile(fileId);
        _replacements[fileId] = data.ToArray();
        return this;
    }

    /// <summary>Replaces an allocation selected by its original source path.</summary>
    /// <param name="fullPath">Exact case-sensitive source path, even after a pending rename.</param>
    /// <param name="data">Complete replacement payload.</param>
    /// <returns>This builder.</returns>
    public WifiUtilityArchiveBuilder ReplaceFile(string fullPath, ReadOnlySpan<byte> data)
    {
        WifiUtilityFile file = _source.FindFile(fullPath) ?? throw new FileNotFoundException("Utility file path was not found.", fullPath);
        return ReplaceFile(file.Id, data);
    }

    /// <summary>Changes a named file's final path segment, retaining its parent and allocation identity.</summary>
    /// <param name="fileId">Named source allocation; unnamed allocations cannot be assigned a new hierarchy through this operation.</param>
    /// <param name="name">One lossless Latin-1 segment; duplicate siblings are rejected when building.</param>
    /// <returns>This builder.</returns>
    public WifiUtilityArchiveBuilder RenameFile(int fileId, string name)
    {
        RequireFile(fileId);
        ArgumentNullException.ThrowIfNull(name);
        WifiUtilityNameTable.ValidateName(name);
        if (_source.Files[fileId].Name is null) { throw new InvalidOperationException("An unnamed utility allocation has no directory name entry to rename."); }
        if (name == _source.Files[fileId].Name) { _names.Remove((false, fileId)); }
        else { _names[(false, fileId)] = name; }
        return this;
    }

    /// <summary>Renames a non-root directory without moving or renumbering its descendants.</summary>
    /// <param name="directoryId">Existing native directory identity other than 0xF000.</param>
    /// <param name="name">One lossless Latin-1 segment; duplicate siblings are rejected when building.</param>
    /// <returns>This builder.</returns>
    public WifiUtilityArchiveBuilder RenameDirectory(ushort directoryId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        WifiUtilityNameTable.ValidateName(name);
        if (directoryId <= 0xF000 || directoryId - 0xF000 >= _source.Directories.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(directoryId), "Only existing non-root utility directories can be renamed.");
        }
        if (name == _source.Directories[directoryId - 0xF000].Name) { _names.Remove((true, directoryId)); }
        else { _names[(true, directoryId)] = name; }
        return this;
    }

    /// <summary>Produces byte-exact preservation when compatible, otherwise a deterministic canonical archive, then reparses it.</summary>
    /// <param name="options">Explicit preservation preference, output-memory limit, alignment, and padding.</param>
    /// <returns>A complete new archive; the original source never changes.</returns>
    public byte[] Build(WifiUtilityWriteOptions? options = null)
    {
        options ??= new();
        options.Validate();
        byte[] result = TryPreserve(options) ?? BuildCanonical(options);
        _ = WifiUtilityArchive.Parse(result, new()
        {
            MaximumArchiveBytes = options.MaximumOutputBytes,
            MaximumDirectoryDepth = 4096,
        });
        return result;
    }

    /// <summary>Checks identities before accessing the source array.</summary>
    private void RequireFile(int fileId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fileId, _source.Files.Count);
    }

    /// <summary>Uses source bytes only if edits cannot change another allocation through a shared interval.</summary>
    private byte[]? TryPreserve(WifiUtilityWriteOptions options)
    {
        if (!options.PreserveSourceLayout || _names.Count != 0 ||
            _replacements.Any(pair => pair.Value.Length != _source.Files[pair.Key].Data.Length)) { return null; }
        if (_source.Source.Length > options.MaximumOutputBytes) { throw new InvalidDataException("Preserved utility output exceeds its byte limit."); }
        byte[] output = _source.WritePreserved();
        foreach ((int id, byte[] bytes) in _replacements) { bytes.CopyTo(output, _source.Files[id].Offset); }
        foreach (WifiUtilityFile file in _source.Files)
        {
            ReadOnlyMemory<byte> expected = GetData(file);
            if (!output.AsSpan(file.Offset, expected.Length).SequenceEqual(expected.Span)) { return null; }
        }
        return output;
    }

    /// <summary>Retains opaque filename-table bytes and ID order while placing all allocations independently.</summary>
    private byte[] BuildCanonical(WifiUtilityWriteOptions options)
    {
        byte[] names = WifiUtilityNameTable.Rename(_source, _names, options.MaximumOutputBytes);
        long fatOffset = Align(16L + names.Length, options.TableAlignment);
        long fatLength = _source.Files.Count * 8L;
        long cursor = Align(fatOffset + fatLength, options.FileAlignment);
        var ranges = new (int Start, int End)[_source.Files.Count];
        foreach (WifiUtilityFile file in _source.Files)
        {
            long end = cursor + GetData(file).Length;
            RequireLength(end, options.MaximumOutputBytes);
            ranges[file.Id] = ((int)cursor, (int)end);
            cursor = Align(end, options.FileAlignment);
        }
        RequireLength(cursor, options.MaximumOutputBytes);
        byte[] result = new byte[(int)cursor];
        result.AsSpan().Fill(options.PaddingByte);
        BinaryPrimitives.WriteUInt32LittleEndian(result, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)names.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)fatOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), (uint)fatLength);
        names.CopyTo(result, 16);
        foreach (WifiUtilityFile file in _source.Files)
        {
            (int start, int end) = ranges[file.Id];
            int record = (int)fatOffset + file.Id * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(record), (uint)start);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(record + 4), (uint)end);
            GetData(file).Span.CopyTo(result.AsSpan(start));
        }
        return result;
    }

    private ReadOnlyMemory<byte> GetData(WifiUtilityFile file) =>
        _replacements.TryGetValue(file.Id, out byte[]? data) ? data : file.Data;

    private static long Align(long value, int alignment) => checked((value + alignment - 1) & ~((long)alignment - 1));

    private static void RequireLength(long length, int maximum)
    {
        if (length > maximum) { throw new InvalidDataException("Canonical utility output exceeds its byte limit."); }
    }
}
