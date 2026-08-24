using System.Buffers.Binary;
using NdsForge.Nitro.Containers;

namespace NdsForge.Nitro.Archives;

/// <summary>Replaces NARC payloads without renumbering allocations or rewriting the parsed filename hierarchy.</summary>
public sealed class NarcArchiveBuilder
{
    private readonly NarcArchive _source;
    private readonly byte[][] _files;
    private readonly HashSet<int> _changed = [];

    /// <summary>Copies file payloads so later caller mutation cannot alter a pending build.</summary>
    internal NarcArchiveBuilder(NarcArchive source)
    {
        _source = source;
        _files = source.Files.Select(static file => file.Data.ToArray()).ToArray();
    }

    /// <summary>Replaces one allocation by stable FAT identifier.</summary>
    /// <param name="fileId">Zero-based file identifier.</param>
    /// <param name="data">Complete replacement payload.</param>
    /// <returns>This builder for fluent edits.</returns>
    public NarcArchiveBuilder ReplaceFile(int fileId, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fileId, _files.Length);

        _files[fileId] = data.ToArray();
        _changed.Add(fileId);
        return this;
    }

    /// <summary>Replaces one allocation by exact slash-prefixed FNT path.</summary>
    /// <param name="fullPath">Case-sensitive archive path.</param>
    /// <param name="data">Complete replacement payload.</param>
    /// <returns>This builder for fluent edits.</returns>
    public NarcArchiveBuilder ReplaceFile(string fullPath, ReadOnlySpan<byte> data)
    {
        NarcFile file = _source.FindFile(fullPath) ?? throw new FileNotFoundException(
            $"The NARC does not contain a named file at '{fullPath}'.",
            fullPath);
        return ReplaceFile(file.Id, data);
    }

    /// <summary>Writes an exact preservation result when possible, otherwise a deterministic aligned reconstruction.</summary>
    /// <param name="options">Header, alignment, padding, and preservation policy.</param>
    /// <returns>A complete NARC byte array.</returns>
    public byte[] Build(NarcWriteOptions? options = null)
    {
        options ??= new NarcWriteOptions();
        options.Validate();
        if (CanPreserveLayout(options))
        {
            (byte[] original, _) = _source.GetPreservationData();
            byte[] result = original.ToArray();
            foreach (int fileId in _changed)
            {
                _files[fileId].CopyTo(result, _source.Files[fileId].OriginalOffset);
            }

            return result;
        }

        return Rebuild(options);
    }

    /// <summary>Requires unchanged lengths and marker before patching the original private byte copy.</summary>
    private bool CanPreserveLayout(NarcWriteOptions options) =>
        options.PreserveSourceLayout &&
        (options.HeaderByteOrder is null || options.HeaderByteOrder == _source.HeaderByteOrder) &&
        _changed.All(fileId => _files[fileId].Length == _source.Files[fileId].Data.Length);

    /// <summary>Serializes fixed-order BTAF, BTNF, and GMIF blocks using caller-selected payload alignment.</summary>
    private byte[] Rebuild(NarcWriteOptions options)
    {
        (_, byte[] nameTable) = _source.GetPreservationData();
        int fatLength = checked(12 + (_files.Length * 8));
        int fntLength = checked(8 + nameTable.Length);
        using var payload = new MemoryStream();
        var ranges = new (int Start, int End)[_files.Length];
        for (int fileId = 0; fileId < _files.Length; fileId++)
        {
            int start = checked((int)payload.Length);
            payload.Write(_files[fileId]);
            int end = checked((int)payload.Length);
            ranges[fileId] = (start, end);
            while (payload.Length % options.FileAlignment != 0)
            {
                payload.WriteByte(options.PaddingByte);
            }
        }

        int imageLength = checked(8 + (int)payload.Length);
        int totalLength = checked(0x10 + fatLength + fntLength + imageLength);
        byte[] result = new byte[totalLength];
        WriteHeader(result, options.HeaderByteOrder ?? _source.HeaderByteOrder);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)totalLength);

        int fatOffset = 0x10;
        "BTAF"u8.CopyTo(result.AsSpan(fatOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(fatOffset + 4), (uint)fatLength);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(fatOffset + 8), (uint)_files.Length);
        for (int fileId = 0; fileId < ranges.Length; fileId++)
        {
            int entryOffset = fatOffset + 12 + (fileId * 8);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(entryOffset), (uint)ranges[fileId].Start);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(entryOffset + 4), (uint)ranges[fileId].End);
        }

        int fntOffset = fatOffset + fatLength;
        "BTNF"u8.CopyTo(result.AsSpan(fntOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(fntOffset + 4), (uint)fntLength);
        nameTable.CopyTo(result, fntOffset + 8);

        int imageOffset = fntOffset + fntLength;
        "GMIF"u8.CopyTo(result.AsSpan(imageOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(imageOffset + 4), (uint)imageLength);
        payload.ToArray().CopyTo(result, imageOffset + 8);
        return result;
    }

    /// <summary>Writes the two observed marker/version representations and common little-endian fields.</summary>
    private static void WriteHeader(Span<byte> result, NitroByteOrder byteOrder)
    {
        "NARC"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt16LittleEndian(result[4..], byteOrder == NitroByteOrder.LittleEndian ? (ushort)0xFEFF : (ushort)0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(result[6..], byteOrder == NitroByteOrder.LittleEndian ? (ushort)1 : (ushort)0x0100);
        BinaryPrimitives.WriteUInt16LittleEndian(result[12..], 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(result[14..], 3);
    }
}
