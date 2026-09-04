using System.Buffers.Binary;

namespace NdsForge.Graphics.Sprites;

/// <summary>Models a standard NCER cell bank with exact OAM entries.</summary>
public sealed class NcerCellBank
{
    private readonly byte[] _originalData;
    private readonly int[][] _objectOffsets;

    private NcerCellBank(ushort version, ushort attributes, NitroSpriteTileMapping mapping,
        IReadOnlyList<NcerCell> cells, ReadOnlyMemory<byte> labelData, ReadOnlyMemory<byte> userExtendedInfo,
        byte[] originalData, int[][] objectOffsets)
    {
        Version = version;
        Attributes = attributes;
        Mapping = mapping;
        Cells = Array.AsReadOnly(cells.ToArray());
        LabelData = labelData;
        UserExtendedInfo = userExtendedInfo;
        _originalData = originalData;
        _objectOffsets = objectOffsets;
    }

    /// <summary>Gets the raw standard-file version.</summary>
    public ushort Version { get; }
    /// <summary>Gets the exact cell-bank attribute word.</summary>
    public ushort Attributes { get; }
    /// <summary>Gets whether cell records include explicit bounds.</summary>
    public bool HasExplicitBounds => (Attributes & 1) != 0;
    /// <summary>Gets the object-character mapping mode.</summary>
    public NitroSpriteTileMapping Mapping { get; }
    /// <summary>Gets cells in serialized order.</summary>
    public IReadOnlyList<NcerCell> Cells { get; }
    /// <summary>Gets the ambiguous LABL payload opaquely and without its block header.</summary>
    public ReadOnlyMemory<byte> LabelData { get; }
    /// <summary>Gets opaque UEXT data without its block header.</summary>
    public ReadOnlyMemory<byte> UserExtendedInfo { get; }

    /// <summary>Parses one bounded little-endian NCER standard file.</summary>
    /// <param name="data">Complete NCER allocation, optionally followed by padding.</param>
    /// <returns>A detached cell-bank model.</returns>
    public static NcerCellBank Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x10 || !data[..4].SequenceEqual("RECN"u8) ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 0xFEFF)
            throw new InvalidDataException("The input does not begin with a supported NCER header.");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        uint rawLength = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (rawLength < 0x10 || rawLength > data.Length || headerLength != 0x10 || blockCount <= 0)
            throw new InvalidDataException("The NCER length, header size, or block count is invalid.");

        int fileLength = (int)rawLength;
        var blocks = new List<(int Offset, int Length)>();
        int cursor = 0x10;
        for (int i = 0; i < blockCount; i++)
        {
            if (cursor > fileLength - 8) throw new InvalidDataException("The NCER block list is truncated.");
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 4)..]);
            if (size < 8 || size > fileLength - cursor) throw new InvalidDataException("An NCER block length is invalid.");
            blocks.Add((cursor, (int)size));
            cursor += (int)size;
        }
        if (!NitroStandardFilePadding.IsValid(data, cursor, fileLength))
            throw new InvalidDataException("NCER blocks do not cover the declared file or valid alignment padding.");

        (int Offset, int Length) cebk = FindBlock(data, blocks, "KBEC"u8);
        if (cebk.Length < 0x20) throw new InvalidDataException("The NCER has no valid CEBK block.");
        int body = cebk.Offset + 8;
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(data[body..]);
        ushort attributes = BinaryPrimitives.ReadUInt16LittleEndian(data[(body + 2)..]);
        uint rawCellsOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[(body + 4)..]);
        int rawMapping = BinaryPrimitives.ReadInt32LittleEndian(data[(body + 8)..]);
        uint extendedOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[(body + 20)..]);
        int recordSize = (attributes & 1) != 0 ? 0x10 : 0x08;
        if (rawCellsOffset > cebk.Length - 8 || count > (cebk.Length - 8 - rawCellsOffset) / recordSize)
            throw new InvalidDataException("The NCER cell table lies outside CEBK.");
        int cellTable = checked(body + (int)rawCellsOffset);
        int objectBase = checked(cellTable + (count * recordSize));
        var cells = new NcerCell[count];
        var objectOffsets = new int[count][];
        for (int cellIndex = 0; cellIndex < count; cellIndex++)
        {
            int record = cellTable + (cellIndex * recordSize);
            ushort objectCount = BinaryPrimitives.ReadUInt16LittleEndian(data[record..]);
            ushort cellAttributes = BinaryPrimitives.ReadUInt16LittleEndian(data[(record + 2)..]);
            uint relativeObjectOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[(record + 4)..]);
            int firstObject = checked(objectBase + (int)relativeObjectOffset);
            if (firstObject < objectBase || firstObject > body + cebk.Length - 8 ||
                objectCount > (body + cebk.Length - 8 - firstObject) / 6)
                throw new InvalidDataException("An NCER OAM list lies outside CEBK.");
            NcerCellBounds? bounds = null;
            if ((attributes & 1) != 0)
            {
                short maxX = BinaryPrimitives.ReadInt16LittleEndian(data[(record + 8)..]);
                short maxY = BinaryPrimitives.ReadInt16LittleEndian(data[(record + 10)..]);
                short minX = BinaryPrimitives.ReadInt16LittleEndian(data[(record + 12)..]);
                short minY = BinaryPrimitives.ReadInt16LittleEndian(data[(record + 14)..]);
                bounds = new(minX, minY, maxX, maxY);
            }
            var objects = new NitroObjectEntry[objectCount];
            objectOffsets[cellIndex] = new int[objectCount];
            for (int objectIndex = 0; objectIndex < objectCount; objectIndex++)
            {
                int offset = firstObject + (objectIndex * 6);
                objectOffsets[cellIndex][objectIndex] = offset;
                objects[objectIndex] = new(
                    BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 4)..]));
            }
            cells[cellIndex] = new(cellAttributes, bounds, objects, null);
        }

        if (extendedOffset != 0) cells = ReadExtendedAttributes(data, body, cebk.Length - 8, extendedOffset, cells);
        ReadOnlyMemory<byte> labels = ReadBlockBody(data, blocks, "LBAL"u8);
        ReadOnlyMemory<byte> userInfo = ReadBlockBody(data, blocks, "TXEU"u8);
        return new(version, attributes, (NitroSpriteTileMapping)rawMapping, cells, labels, userInfo,
            data.ToArray(), objectOffsets);
    }

    /// <summary>Creates an isolated OAM replacement builder.</summary>
    /// <returns>A mutable edit plan.</returns>
    public NcerCellBankBuilder CreateBuilder() => new(this);

    internal (byte[] Data, int[][] Offsets) GetPreservationData() => (_originalData, _objectOffsets);

    private static NcerCell[] ReadExtendedAttributes(ReadOnlySpan<byte> data, int body, int bodyLength, uint rawOffset, NcerCell[] cells)
    {
        if (rawOffset > bodyLength - 0x10) throw new InvalidDataException("The NCER UACT extension lies outside CEBK.");
        int extension = body + (int)rawOffset;
        if (!data.Slice(extension, 4).SequenceEqual("TACU"u8)) throw new InvalidDataException("The NCER cell extension has invalid magic.");
        int extensionBody = extension + 8;
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(data[extensionBody..]);
        ushort attributesPerCell = BinaryPrimitives.ReadUInt16LittleEndian(data[(extensionBody + 2)..]);
        uint pointerOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[(extensionBody + 4)..]);
        if (count != cells.Length || attributesPerCell != 1) throw new InvalidDataException("The NCER UACT layout is unsupported.");
        for (int index = 0; index < cells.Length; index++)
        {
            int pointer = checked(extensionBody + (int)pointerOffset + (index * 4));
            uint valueOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[pointer..]);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[(extensionBody + (int)valueOffset)..]);
            NcerCell old = cells[index];
            cells[index] = new(old.Attributes, old.Bounds, old.Objects, value);
        }
        return cells;
    }

    private static ReadOnlyMemory<byte> ReadBlockBody(ReadOnlySpan<byte> data, IReadOnlyList<(int Offset, int Length)> blocks, ReadOnlySpan<byte> magic)
    {
        (int Offset, int Length) block = FindBlock(data, blocks, magic);
        return block.Length == 0 ? ReadOnlyMemory<byte>.Empty : data.Slice(block.Offset + 8, block.Length - 8).ToArray();
    }

    private static (int Offset, int Length) FindBlock(ReadOnlySpan<byte> data, IReadOnlyList<(int Offset, int Length)> blocks, ReadOnlySpan<byte> magic)
    {
        (int Offset, int Length) result = default;
        foreach ((int offset, int length) in blocks)
        {
            if (!data.Slice(offset, 4).SequenceEqual(magic)) continue;
            if (result.Length != 0) throw new InvalidDataException("The NCER repeats a standard block.");
            result = (offset, length);
        }
        return result;
    }
}
