using System.Buffers.Binary;
using NdsForge.Nitro.Containers;

namespace NdsForge.Nitro.Text;

/// <summary>Provides a bounded, read-only model of a BMG message bundle.</summary>
public sealed class BmgMessageBundle
{
    private readonly byte[] _originalData;

    private BmgMessageBundle(NitroByteOrder byteOrder, BmgEncoding encoding, uint messageId,
        uint headerField14, uint headerField18, uint headerField1C, uint declaredSectionCount,
        bool hasMissingTrailingSection, bool hasMissingTrailingPadding, IReadOnlyList<BmgMessage> messages,
        IReadOnlyList<BmgAuxiliarySection> auxiliarySections, byte[] originalData)
    {
        ByteOrder = byteOrder;
        Encoding = encoding;
        MessageId = messageId;
        HeaderField14 = headerField14;
        HeaderField18 = headerField18;
        HeaderField1C = headerField1C;
        DeclaredSectionCount = declaredSectionCount;
        HasMissingTrailingSection = hasMissingTrailingSection;
        HasMissingTrailingPadding = hasMissingTrailingPadding;
        Messages = Array.AsReadOnly(messages.ToArray());
        AuxiliarySections = Array.AsReadOnly(auxiliarySections.ToArray());
        _originalData = originalData;
    }

    /// <summary>Gets the integer and UTF-16 byte order.</summary>
    public NitroByteOrder ByteOrder { get; }

    /// <summary>Gets the declared character encoding.</summary>
    public BmgEncoding Encoding { get; }

    /// <summary>Gets the identifier stored in the INF1 section.</summary>
    public uint MessageId { get; }

    /// <summary>Gets the opaque header word at offset <c>0x14</c>.</summary>
    public uint HeaderField14 { get; }

    /// <summary>Gets the opaque header word at offset <c>0x18</c>.</summary>
    public uint HeaderField18 { get; }

    /// <summary>Gets the opaque header word at offset <c>0x1C</c>.</summary>
    public uint HeaderField1C { get; }

    /// <summary>Gets the section count stored by the header.</summary>
    public uint DeclaredSectionCount { get; }

    /// <summary>Gets whether the declared file ends one or more entries before its stored section count.</summary>
    public bool HasMissingTrailingSection { get; }

    /// <summary>Gets whether a final FLI1 section claims aligned padding absent from the allocation.</summary>
    public bool HasMissingTrailingPadding { get; }

    /// <summary>Gets messages in INF1 order.</summary>
    public IReadOnlyList<BmgMessage> Messages { get; }

    /// <summary>Gets all sections other than INF1 and DAT1 in serialized order.</summary>
    public IReadOnlyList<BmgAuxiliarySection> AuxiliarySections { get; }

    /// <summary>Parses one bounded BMG message bundle, optionally followed by allocation padding.</summary>
    /// <param name="data">Complete BMG allocation.</param>
    /// <returns>A detached message-bundle model.</returns>
    public static BmgMessageBundle Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x20 || !data[..8].SequenceEqual("MESGbmg1"u8))
            throw new InvalidDataException("The input does not begin with a BMG header.");

        Candidate little = InspectCandidate(data, NitroByteOrder.LittleEndian);
        Candidate big = InspectCandidate(data, NitroByteOrder.BigEndian);
        Candidate selected = SelectCandidate(little, big);
        int fileLength = checked((int)selected.FileLength);
        var sections = new List<Section>();
        int cursor = 0x20;
        bool missingTrailing = false;
        for (uint sectionIndex = 0; sectionIndex < selected.SectionCount; sectionIndex++)
        {
            if (cursor == fileLength)
            {
                missingTrailing = true;
                break;
            }
            if (cursor > data.Length - 8) throw new InvalidDataException("The BMG section list is truncated.");
            string signature = System.Text.Encoding.ASCII.GetString(data.Slice(cursor, 4));
            uint rawLength = ReadUInt32(data[(cursor + 4)..], selected.ByteOrder);
            if (rawLength < 8 || rawLength > fileLength - cursor)
                throw new InvalidDataException("A BMG section length is invalid.");
            int availableLength = Math.Min((int)rawLength, data.Length - cursor);
            if (availableLength != rawLength &&
                (signature != "FLI1" || sectionIndex + 1 != selected.SectionCount ||
                 rawLength - availableLength > 31 || cursor + rawLength != fileLength))
                throw new InvalidDataException("A BMG section extends beyond the allocation.");
            sections.Add(new(signature, cursor, availableLength));
            cursor += (int)rawLength;
        }
        if (cursor != fileLength) throw new InvalidDataException("BMG sections do not cover the declared file.");

        Section inf1 = FindSingleSection(sections, "INF1");
        Section dat1 = FindSingleSection(sections, "DAT1");
        if (inf1.Length < 0x10) throw new InvalidDataException("The BMG INF1 section is truncated.");
        int count = ReadUInt16(data[(inf1.Offset + 8)..], selected.ByteOrder);
        int entryLength = ReadUInt16(data[(inf1.Offset + 10)..], selected.ByteOrder);
        uint messageId = ReadUInt32(data[(inf1.Offset + 12)..], selected.ByteOrder);
        if (entryLength < 4 || count > (inf1.Length - 16) / entryLength)
            throw new InvalidDataException("The BMG INF1 entry table is invalid.");

        ReadOnlySpan<byte> datBody = data.Slice(dat1.Offset + 8, dat1.Length - 8);
        var messages = new BmgMessage[count];
        for (int index = 0; index < count; index++)
        {
            int entry = inf1.Offset + 16 + (index * entryLength);
            uint offset = ReadUInt32(data[entry..], selected.ByteOrder);
            ReadOnlyMemory<byte> attributes = data.Slice(entry + 4, entryLength - 4).ToArray();
            messages[index] = ParseMessage(datBody, offset, attributes, selected.Encoding, selected.ByteOrder);
        }

        var auxiliary = new List<BmgAuxiliarySection>();
        foreach (Section section in sections)
        {
            if (section.Signature is "INF1" or "DAT1") continue;
            auxiliary.Add(new(section.Signature, data.Slice(section.Offset + 8, section.Length - 8).ToArray()));
        }
        return new(selected.ByteOrder, selected.Encoding, messageId,
            ReadUInt32(data[0x14..], selected.ByteOrder), ReadUInt32(data[0x18..], selected.ByteOrder),
            ReadUInt32(data[0x1C..], selected.ByteOrder), selected.SectionCount, missingTrailing,
            selected.HasMissingPadding,
            messages, auxiliary, data.ToArray());
    }

    /// <summary>Returns an isolated byte-exact copy of the original allocation.</summary>
    /// <returns>The complete source allocation, including bytes after the declared BMG file.</returns>
    public byte[] WritePreserved() => _originalData.ToArray();

    private static BmgMessage ParseMessage(ReadOnlySpan<byte> dat, uint rawOffset,
        ReadOnlyMemory<byte> attributes, BmgEncoding encoding, NitroByteOrder byteOrder)
    {
        if (rawOffset > int.MaxValue || rawOffset >= dat.Length)
            throw new InvalidDataException("A BMG message offset lies outside DAT1.");
        int width = encoding == BmgEncoding.Utf16 ? 2 : 1;
        int cursor = (int)rawOffset;
        if (cursor > dat.Length - width) throw new InvalidDataException("A BMG message terminator is truncated.");
        var parts = new List<BmgMessagePart>();
        int textStart = cursor;
        while (!IsCodeUnit(dat, cursor, width, byteOrder, 0))
        {
            if (IsCodeUnit(dat, cursor, width, byteOrder, 0x1A))
            {
                if (cursor != textStart)
                    parts.Add(new(BmgMessagePartKind.Text, dat[textStart..cursor].ToArray(), null, 0));
                if (cursor > dat.Length - width - 2)
                    throw new InvalidDataException("A BMG control-sequence header is truncated.");
                byte length = dat[cursor + width];
                byte code = dat[cursor + width + 1];
                if (length < width + 2 || length > dat.Length - cursor)
                    throw new InvalidDataException("A BMG control-sequence length is invalid.");
                parts.Add(new(BmgMessagePartKind.Control,
                    dat.Slice(cursor + width + 2, length - width - 2).ToArray(), code, length));
                cursor += length;
                textStart = cursor;
            }
            else
            {
                cursor += width;
            }
            if (cursor > dat.Length - width)
                throw new InvalidDataException("A BMG message is not null-terminated within DAT1.");
        }
        if (cursor != textStart)
            parts.Add(new(BmgMessagePartKind.Text, dat[textStart..cursor].ToArray(), null, 0));
        return new(rawOffset, attributes, rawOffset == 0, parts, encoding, byteOrder);
    }

    private static bool IsCodeUnit(ReadOnlySpan<byte> data, int offset, int width,
        NitroByteOrder byteOrder, byte value)
    {
        if (width == 1) return data[offset] == value;
        return byteOrder == NitroByteOrder.LittleEndian
            ? data[offset] == value && data[offset + 1] == 0
            : data[offset] == 0 && data[offset + 1] == value;
    }

    private static Candidate InspectCandidate(ReadOnlySpan<byte> data, NitroByteOrder byteOrder)
    {
        uint fileLength = ReadUInt32(data[8..], byteOrder);
        uint sectionCount = ReadUInt32(data[12..], byteOrder);
        byte rawEncoding = data[16];
        if (fileLength is < 0x20 || fileLength > data.Length + 31u || sectionCount is 0 or > 64 || rawEncoding is < 1 or > 4)
            return new(byteOrder, fileLength, sectionCount, default, false, false);
        int cursor = 0x20;
        bool missingPadding = false;
        for (uint index = 0; index < sectionCount; index++)
        {
            if (cursor == fileLength)
                return new(byteOrder, fileLength, sectionCount, (BmgEncoding)rawEncoding, true, missingPadding);
            if (cursor > data.Length - 8 || cursor > fileLength - 8)
                return new(byteOrder, fileLength, sectionCount, default, false, false);
            bool isFinalFlowIndex = data.Slice(cursor, 4).SequenceEqual("FLI1"u8) && index + 1 == sectionCount;
            uint length = ReadUInt32(data[(cursor + 4)..], byteOrder);
            if (length < 8 || length > fileLength - cursor)
                return new(byteOrder, fileLength, sectionCount, default, false, false);
            long end = (long)cursor + length;
            if (end > data.Length)
            {
                if (!isFinalFlowIndex || end != fileLength || end - data.Length > 31)
                    return new(byteOrder, fileLength, sectionCount, default, false, false);
                missingPadding = true;
            }
            cursor += (int)length;
        }
        return new(byteOrder, fileLength, sectionCount, (BmgEncoding)rawEncoding,
            cursor == fileLength, missingPadding);
    }

    private static Candidate SelectCandidate(Candidate little, Candidate big)
    {
        if (little.IsValid != big.IsValid) return little.IsValid ? little : big;
        if (!little.IsValid) throw new InvalidDataException("The BMG byte order or section layout is invalid.");
        if (little.FileLength != big.FileLength) return little.FileLength < big.FileLength ? little : big;
        throw new InvalidDataException("The BMG byte order is ambiguous.");
    }

    private static Section FindSingleSection(IReadOnlyList<Section> sections, string signature)
    {
        Section[] matches = sections.Where(section => section.Signature == signature).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException(
            $"The BMG must contain exactly one {signature} section.");
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, NitroByteOrder byteOrder) =>
        byteOrder == NitroByteOrder.LittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data)
            : BinaryPrimitives.ReadUInt16BigEndian(data);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, NitroByteOrder byteOrder) =>
        byteOrder == NitroByteOrder.LittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data)
            : BinaryPrimitives.ReadUInt32BigEndian(data);

    private readonly record struct Candidate(NitroByteOrder ByteOrder, uint FileLength,
        uint SectionCount, BmgEncoding Encoding, bool IsValid, bool HasMissingPadding);

    private readonly record struct Section(string Signature, int Offset, int Length);
}
