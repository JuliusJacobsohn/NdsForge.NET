using System.Buffers.Binary;

namespace NdsForge.Graphics.Sprites;

/// <summary>Edits NCER OAM words with exact preservation or deterministic reconstruction.</summary>
public sealed class NcerCellBankBuilder
{
    private readonly NcerCellBank _source;
    private readonly NitroObjectEntry[][] _objects;
    private readonly HashSet<(int Cell, int Object)> _changed = [];

    internal NcerCellBankBuilder(NcerCellBank source)
    {
        _source = source;
        _objects = source.Cells.Select(cell => cell.Objects.ToArray()).ToArray();
    }

    /// <summary>Replaces one exact OAM entry.</summary>
    /// <param name="cellIndex">Zero-based cell index.</param>
    /// <param name="objectIndex">Zero-based object index within the cell.</param>
    /// <param name="value">Replacement exact OAM words.</param>
    /// <returns>This builder.</returns>
    public NcerCellBankBuilder ReplaceObject(int cellIndex, int objectIndex, NitroObjectEntry value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cellIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cellIndex, _objects.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(objectIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(objectIndex, _objects[cellIndex].Length);
        _objects[cellIndex][objectIndex] = value;
        _changed.Add((cellIndex, objectIndex));
        return this;
    }

    /// <summary>Writes an exact preservation result by default or a canonical NCER.</summary>
    /// <param name="preserveSourceLayout">Retains all original bytes except explicitly replaced OAM words when true.</param>
    /// <returns>Complete NCER bytes.</returns>
    public byte[] Build(bool preserveSourceLayout = true)
    {
        if (preserveSourceLayout)
        {
            (byte[] source, int[][] offsets) = _source.GetPreservationData();
            byte[] result = source.ToArray();
            foreach ((int cell, int obj) in _changed)
            {
                WriteObject(result.AsSpan(offsets[cell][obj]), _objects[cell][obj]);
            }
            return result;
        }
        return WriteCanonical(_source, _objects);
    }

    private static byte[] WriteCanonical(NcerCellBank source, NitroObjectEntry[][] objects)
    {
        int count = source.Cells.Count;
        int recordSize = source.HasExplicitBounds ? 0x10 : 0x08;
        int objectBytes = objects.Sum(items => checked(items.Length * 6));
        bool hasExtended = source.Cells.Any(cell => cell.ExtendedAttribute is not null);
        int baseBodyLength = checked(0x18 + (count * recordSize) + objectBytes);
        int extensionStart = hasExtended ? Align4(baseBodyLength) : baseBodyLength;
        int extensionLength = hasExtended ? checked(0x10 + (count * 8)) : 0;
        int cebkBodyLength = checked(extensionStart + extensionLength);
        byte[] cebk = new byte[checked(8 + cebkBodyLength)];
        "KBEC"u8.CopyTo(cebk);
        BinaryPrimitives.WriteUInt32LittleEndian(cebk.AsSpan(4), (uint)cebk.Length);
        int body = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(cebk.AsSpan(body), checked((ushort)count));
        BinaryPrimitives.WriteUInt16LittleEndian(cebk.AsSpan(body + 2), source.Attributes);
        BinaryPrimitives.WriteUInt32LittleEndian(cebk.AsSpan(body + 4), 0x18);
        BinaryPrimitives.WriteInt32LittleEndian(cebk.AsSpan(body + 8), (int)source.Mapping);
        if (hasExtended) BinaryPrimitives.WriteUInt32LittleEndian(cebk.AsSpan(body + 20), (uint)extensionStart);
        int records = body + 0x18;
        int objectBase = records + (count * recordSize);
        int objectCursor = objectBase;
        for (int cellIndex = 0; cellIndex < count; cellIndex++)
        {
            NcerCell cell = source.Cells[cellIndex];
            int record = records + (cellIndex * recordSize);
            BinaryPrimitives.WriteUInt16LittleEndian(cebk.AsSpan(record), checked((ushort)objects[cellIndex].Length));
            BinaryPrimitives.WriteUInt16LittleEndian(cebk.AsSpan(record + 2), cell.Attributes);
            BinaryPrimitives.WriteUInt32LittleEndian(cebk.AsSpan(record + 4), (uint)(objectCursor - objectBase));
            if (source.HasExplicitBounds && cell.Bounds is NcerCellBounds bounds)
            {
                BinaryPrimitives.WriteInt16LittleEndian(cebk.AsSpan(record + 8), bounds.MaximumX);
                BinaryPrimitives.WriteInt16LittleEndian(cebk.AsSpan(record + 10), bounds.MaximumY);
                BinaryPrimitives.WriteInt16LittleEndian(cebk.AsSpan(record + 12), bounds.MinimumX);
                BinaryPrimitives.WriteInt16LittleEndian(cebk.AsSpan(record + 14), bounds.MinimumY);
            }
            foreach (NitroObjectEntry value in objects[cellIndex])
            {
                WriteObject(cebk.AsSpan(objectCursor), value);
                objectCursor += 6;
            }
        }
        if (hasExtended) WriteExtended(cebk.AsSpan(body + extensionStart), source.Cells);

        byte[] labels = WriteBlock("LBAL"u8, source.LabelData.Span);
        byte[] user = WriteBlock("TXEU"u8, source.UserExtendedInfo.Span);
        int blockCount = 1 + (labels.Length == 0 ? 0 : 1) + (user.Length == 0 ? 0 : 1);
        int total = checked(0x10 + cebk.Length + labels.Length + user.Length);
        byte[] result = new byte[total];
        "RECN"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 0xFEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), source.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), (ushort)blockCount);
        int cursor = 0x10;
        cebk.CopyTo(result.AsSpan(cursor)); cursor += cebk.Length;
        labels.CopyTo(result.AsSpan(cursor)); cursor += labels.Length;
        user.CopyTo(result.AsSpan(cursor));
        return result;
    }

    private static void WriteObject(Span<byte> target, NitroObjectEntry value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(target, value.Attribute0);
        BinaryPrimitives.WriteUInt16LittleEndian(target[2..], value.Attribute1);
        BinaryPrimitives.WriteUInt16LittleEndian(target[4..], value.Attribute2);
    }

    private static void WriteExtended(Span<byte> target, IReadOnlyList<NcerCell> cells)
    {
        int length = 0x10 + (cells.Count * 8);
        "TACU"u8.CopyTo(target);
        BinaryPrimitives.WriteInt32LittleEndian(target[4..], length);
        BinaryPrimitives.WriteUInt16LittleEndian(target[8..], (ushort)cells.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(target[10..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(target[12..], 8);
        int values = 8 + (cells.Count * 4);
        for (int i = 0; i < cells.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(target[(16 + (i * 4))..], (uint)(values + (i * 4)));
            BinaryPrimitives.WriteUInt32LittleEndian(target[(8 + values + (i * 4))..], cells[i].ExtendedAttribute ?? 0);
        }
    }

    private static byte[] WriteBlock(ReadOnlySpan<byte> magic, ReadOnlySpan<byte> body)
    {
        if (body.Length == 0) return [];
        byte[] result = new byte[8 + body.Length];
        magic.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length);
        body.CopyTo(result.AsSpan(8));
        return result;
    }

    private static int Align4(int value) => checked((value + 3) & ~3);
}
