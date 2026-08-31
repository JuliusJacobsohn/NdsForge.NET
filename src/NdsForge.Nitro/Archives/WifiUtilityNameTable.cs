using System.Buffers.Binary;
using System.Text;

namespace NdsForge.Nitro.Archives;

/// <summary>Identifies one name descriptor for bounded, identity-preserving table edits.</summary>
internal readonly record struct WifiUtilityNameEntry(int Offset, int Length, int Identity, bool IsDirectory);

/// <summary>Walks standard Nitro directory records iteratively, without executing or expanding contained assets.</summary>
internal static class WifiUtilityNameTable
{
    internal static (WifiUtilityDirectory[] Directories, IReadOnlyList<WifiUtilityNameEntry> Names) Parse(
        ReadOnlySpan<byte> table, WifiUtilityFile[] files, WifiUtilityReadOptions options)
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(table[6..]);
        if (count == 0 || count > options.MaximumDirectoryCount || count > table.Length / 8)
        {
            throw new InvalidDataException("Utility directory count is invalid or exceeds its limit.");
        }
        var directories = new WifiUtilityDirectory[count];
        var pending = new Queue<(ushort Id, ushort? Parent, string Name, string Path, int Depth)>();
        var scheduled = new HashSet<ushort> { 0xF000 };
        var assignedFiles = new HashSet<int>();
        var nameEntries = new List<WifiUtilityNameEntry>();
        var subtableRanges = new List<(int Start, int End)>();
        pending.Enqueue((0xF000, null, string.Empty, "/", 0));
        while (pending.TryDequeue(out var item))
        {
            int index = item.Id - 0xF000;
            if (item.Depth > options.MaximumDirectoryDepth || index < 0 || index >= count)
            {
                throw new InvalidDataException("Utility directory identity or depth is outside its limits.");
            }
            int record = index * 8;
            uint start = BinaryPrimitives.ReadUInt32LittleEndian(table[record..]);
            int fileId = BinaryPrimitives.ReadUInt16LittleEndian(table[(record + 4)..]);
            ushort parent = BinaryPrimitives.ReadUInt16LittleEndian(table[(record + 6)..]);
            if (start < count * 8 || start >= table.Length || parent != (item.Parent ?? count))
            {
                throw new InvalidDataException("Utility directory record has invalid parent or subtable bounds.");
            }
            var directory = new WifiUtilityDirectory(item.Id, item.Parent, item.Name, item.Path, (ushort)fileId, (int)start);
            directories[index] = directory;
            var children = new List<ushort>();
            var childFiles = new List<int>();
            var siblings = new HashSet<string>(StringComparer.Ordinal);
            int cursor = (int)start;
            bool terminated = false;
            while (cursor < table.Length)
            {
                int descriptorOffset = cursor;
                byte descriptor = table[cursor++];
                if (descriptor == 0) { terminated = true; break; }
                int length = descriptor & 127;
                bool isDirectory = (descriptor & 128) != 0;
                if (length == 0 || length > table.Length - cursor)
                {
                    throw new InvalidDataException("Utility filename entry is reserved or truncated.");
                }
                string name = Encoding.Latin1.GetString(table.Slice(cursor, length));
                ValidateName(name);
                if (!siblings.Add(name)) { throw new InvalidDataException("Utility directory contains duplicate names."); }
                string path = item.Path == "/" ? "/" + name : item.Path + "/" + name;
                cursor += length;
                if (isDirectory)
                {
                    if (table.Length - cursor < 2) { throw new InvalidDataException("Utility child directory identity is truncated."); }
                    ushort child = BinaryPrimitives.ReadUInt16LittleEndian(table[cursor..]);
                    cursor += 2;
                    if (child < 0xF000 || child - 0xF000 >= count || !scheduled.Add(child))
                    {
                        throw new InvalidDataException("Utility directory relationship is out of bounds, cyclic, or multiply referenced.");
                    }
                    children.Add(child);
                    nameEntries.Add(new(descriptorOffset, length, child, true));
                    pending.Enqueue((child, item.Id, name, path, item.Depth + 1));
                }
                else
                {
                    if (fileId >= files.Length || !assignedFiles.Add(fileId))
                    {
                        throw new InvalidDataException("Utility name refers to an absent or multiply named allocation.");
                    }
                    files[fileId].SetName(name, path, item.Id);
                    childFiles.Add(fileId);
                    nameEntries.Add(new(descriptorOffset, length, fileId++, false));
                }
            }
            if (!terminated) { throw new InvalidDataException("Utility filename subtable has no terminator."); }
            directory.SetChildren(childFiles, children);
            subtableRanges.Add(((int)start, cursor));
        }
        if (scheduled.Count != count) { throw new InvalidDataException("Utility directory table contains unreachable records."); }
        int previousEnd = count * 8;
        foreach ((int start, int end) in subtableRanges.OrderBy(static range => range.Start))
        {
            if (start < previousEnd) { throw new InvalidDataException("Utility filename subtables overlap."); }
            previousEnd = end;
        }
        return (directories, nameEntries.AsReadOnly());
    }

    /// <summary>Maintains one-byte names and unambiguous archive paths without imposing host filename conventions.</summary>
    internal static void ValidateName(string name)
    {
        if (name.Length is 0 or > 127 || name is "." or ".." ||
            name.Any(static character => character is '\0' or '/' or '\\' or > '\u00FF'))
        {
            throw new InvalidDataException("Utility names must be 1–127 non-NUL Latin-1 bytes without path separators or traversal segments.");
        }
    }

    /// <summary>Replaces descriptors and names while retaining every other table byte and adjusting relative subtable offsets.</summary>
    internal static byte[] Rename(WifiUtilityArchive source, IReadOnlyDictionary<(bool Directory, int Id), string> changes, int maximumBytes)
    {
        WifiUtilityNameEntry[] entries = source.NameEntries.Where(entry => changes.ContainsKey((entry.IsDirectory, entry.Identity)))
            .OrderBy(static entry => entry.Offset).ToArray();
        long length = source.NameTable.Length;
        foreach (WifiUtilityNameEntry entry in entries) { length += changes[(entry.IsDirectory, entry.Identity)].Length - entry.Length; }
        if (length > maximumBytes) { throw new InvalidDataException("Utility filename table exceeds the configured output limit."); }
        byte[] result = new byte[(int)length];
        int sourceCursor = 0;
        int targetCursor = 0;
        foreach (WifiUtilityNameEntry entry in entries)
        {
            int copied = entry.Offset - sourceCursor;
            source.NameTable.Span.Slice(sourceCursor, copied).CopyTo(result.AsSpan(targetCursor));
            targetCursor += copied;
            string name = changes[(entry.IsDirectory, entry.Identity)];
            result[targetCursor++] = (byte)(name.Length | (entry.IsDirectory ? 128 : 0));
            targetCursor += Encoding.Latin1.GetBytes(name, result.AsSpan(targetCursor));
            sourceCursor = entry.Offset + 1 + entry.Length;
        }
        source.NameTable.Span[sourceCursor..].CopyTo(result.AsSpan(targetCursor));
        var offsets = new (int Index, uint Offset)[source.Directories.Count];
        for (int index = 0; index < source.Directories.Count; index++)
        {
            offsets[index] = (index, BinaryPrimitives.ReadUInt32LittleEndian(source.NameTable.Span[(index * 8)..]));
        }
        int changeIndex = 0;
        int displacement = 0;
        foreach ((int index, uint original) in offsets.OrderBy(static item => item.Offset))
        {
            while (changeIndex < entries.Length && entries[changeIndex].Offset < original)
            {
                WifiUtilityNameEntry entry = entries[changeIndex++];
                displacement += changes[(entry.IsDirectory, entry.Identity)].Length - entry.Length;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * 8), checked((uint)(original + displacement)));
        }
        return result;
    }
}
