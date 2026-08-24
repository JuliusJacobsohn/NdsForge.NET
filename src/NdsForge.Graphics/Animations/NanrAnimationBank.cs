using System.Buffers.Binary;

namespace NdsForge.Graphics.Animations;

/// <summary>Models a standard NANR animation bank and its cell references.</summary>
public sealed class NanrAnimationBank
{
    private readonly byte[] _originalData;

    private NanrAnimationBank(ushort version, ushort declaredFrameCount, uint bankConstant,
        uint frameDescriptorOffset, uint framePayloadOffset, IReadOnlyList<NanrSequence> sequences,
        ReadOnlyMemory<byte> labelData, ReadOnlyMemory<byte> userExtendedInfo, byte[] originalData)
    {
        Version = version;
        DeclaredFrameCount = declaredFrameCount;
        BankConstant = bankConstant;
        FrameDescriptorOffset = frameDescriptorOffset;
        FramePayloadOffset = framePayloadOffset;
        Sequences = Array.AsReadOnly(sequences.ToArray());
        LabelData = labelData;
        UserExtendedInfo = userExtendedInfo;
        _originalData = originalData;
    }

    /// <summary>Gets the raw standard-file version.</summary>
    public ushort Version { get; }

    /// <summary>Gets the redundant total-frame count stored by the animation bank.</summary>
    public ushort DeclaredFrameCount { get; }

    /// <summary>Gets the exact constant or flags word stored by the animation bank.</summary>
    public uint BankConstant { get; }

    /// <summary>Gets the ABNK-relative offset of the frame-descriptor area.</summary>
    public uint FrameDescriptorOffset { get; }

    /// <summary>Gets the ABNK-relative offset of the frame-payload area.</summary>
    public uint FramePayloadOffset { get; }

    /// <summary>Gets animation sequences in serialized order.</summary>
    public IReadOnlyList<NanrSequence> Sequences { get; }

    /// <summary>Gets the ambiguous LABL payload opaquely and without its block header.</summary>
    public ReadOnlyMemory<byte> LabelData { get; }

    /// <summary>Gets opaque UEXT data without its block header.</summary>
    public ReadOnlyMemory<byte> UserExtendedInfo { get; }

    /// <summary>Parses one bounded little-endian NANR standard file.</summary>
    /// <param name="data">Complete NANR allocation, optionally followed by padding.</param>
    /// <returns>A detached animation-bank model.</returns>
    public static NanrAnimationBank Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x10 || !data[..4].SequenceEqual("RNAN"u8) ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 0xFEFF)
            throw new InvalidDataException("The input does not begin with a supported NANR header.");

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        uint rawLength = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (rawLength < 0x10 || rawLength > data.Length || headerLength != 0x10 || blockCount <= 0)
            throw new InvalidDataException("The NANR length, header size, or block count is invalid.");

        int fileLength = (int)rawLength;
        var blocks = new List<(int Offset, int Length)>();
        int cursor = 0x10;
        for (int index = 0; index < blockCount; index++)
        {
            if (cursor > fileLength - 8) throw new InvalidDataException("The NANR block list is truncated.");
            uint rawBlockLength = BinaryPrimitives.ReadUInt32LittleEndian(data[(cursor + 4)..]);
            if (rawBlockLength < 8 || rawBlockLength > fileLength - cursor)
                throw new InvalidDataException("A NANR block length is invalid.");
            blocks.Add((cursor, (int)rawBlockLength));
            cursor += (int)rawBlockLength;
        }
        if (cursor != fileLength) throw new InvalidDataException("NANR blocks do not cover the declared file.");

        (int Offset, int Length) abnk = FindBlock(data, blocks, "KNBA"u8);
        if (abnk.Length < 0x20) throw new InvalidDataException("The NANR has no valid ABNK block.");
        int body = abnk.Offset + 8;
        int bodyLength = abnk.Length - 8;
        ushort sequenceCount = BinaryPrimitives.ReadUInt16LittleEndian(data[body..]);
        ushort declaredFrameCount = BinaryPrimitives.ReadUInt16LittleEndian(data[(body + 2)..]);
        uint bankConstant = BinaryPrimitives.ReadUInt32LittleEndian(data[(body + 4)..]);
        uint rawDescriptorOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[(body + 8)..]);
        uint rawPayloadOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[(body + 12)..]);
        if (sequenceCount > (bodyLength - 0x18) / 0x10)
            throw new InvalidDataException("The NANR sequence table lies outside ABNK.");
        if (rawDescriptorOffset > bodyLength || rawPayloadOffset > bodyLength)
            throw new InvalidDataException("A NANR frame area lies outside ABNK.");

        var sequences = new NanrSequence[sequenceCount];
        long frameCountSum = 0;
        for (int sequenceIndex = 0; sequenceIndex < sequenceCount; sequenceIndex++)
        {
            int record = body + 0x18 + (sequenceIndex * 0x10);
            uint rawFrameCount = BinaryPrimitives.ReadUInt32LittleEndian(data[record..]);
            ushort dataType = BinaryPrimitives.ReadUInt16LittleEndian(data[(record + 4)..]);
            ushort playbackMode = BinaryPrimitives.ReadUInt16LittleEndian(data[(record + 6)..]);
            ushort loopStartFrame = BinaryPrimitives.ReadUInt16LittleEndian(data[(record + 8)..]);
            ushort sequenceFlags = BinaryPrimitives.ReadUInt16LittleEndian(data[(record + 10)..]);
            uint rawFrameOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[(record + 12)..]);
            if (dataType > 2) throw new InvalidDataException("The NANR frame-data variant is unsupported.");
            if (rawFrameCount > int.MaxValue) throw new InvalidDataException("A NANR frame count is too large.");
            frameCountSum += rawFrameCount;
            if (frameCountSum > ushort.MaxValue || rawFrameOffset > bodyLength - rawDescriptorOffset ||
                rawFrameCount > (bodyLength - rawDescriptorOffset - rawFrameOffset) / 8)
                throw new InvalidDataException("A NANR frame-descriptor list lies outside ABNK.");

            var frames = new NanrFrame[(int)rawFrameCount];
            int descriptor = checked(body + (int)rawDescriptorOffset + (int)rawFrameOffset);
            for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
            {
                uint rawDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[descriptor..]);
                ushort duration = BinaryPrimitives.ReadUInt16LittleEndian(data[(descriptor + 4)..]);
                ushort descriptorFlags = BinaryPrimitives.ReadUInt16LittleEndian(data[(descriptor + 6)..]);
                if (rawDataOffset > bodyLength - rawPayloadOffset || bodyLength - rawPayloadOffset - rawDataOffset < 2)
                    throw new InvalidDataException("A NANR frame payload lies outside ABNK.");
                int payload = checked(body + (int)rawPayloadOffset + (int)rawDataOffset);
                ushort cellIndex = BinaryPrimitives.ReadUInt16LittleEndian(data[payload..]);
                frames[frameIndex] = new(rawDataOffset, duration, descriptorFlags, cellIndex);
                descriptor += 8;
            }
            sequences[sequenceIndex] = new(dataType, playbackMode, loopStartFrame, sequenceFlags, rawFrameOffset, frames);
        }
        if (frameCountSum != declaredFrameCount)
            throw new InvalidDataException("The NANR total-frame count does not match its sequences.");

        ReadOnlyMemory<byte> labels = ReadBlockBody(data, blocks, "LBAL"u8);
        ReadOnlyMemory<byte> userInfo = ReadBlockBody(data, blocks, "TXEU"u8);
        return new(version, declaredFrameCount, bankConstant, rawDescriptorOffset, rawPayloadOffset,
            sequences, labels, userInfo, data.ToArray());
    }

    /// <summary>Returns the original bounded allocation exactly, including trailing padding.</summary>
    /// <returns>A new byte array containing the source allocation.</returns>
    public byte[] WritePreserved() => _originalData.ToArray();

    private static ReadOnlyMemory<byte> ReadBlockBody(ReadOnlySpan<byte> data,
        IReadOnlyList<(int Offset, int Length)> blocks, ReadOnlySpan<byte> magic)
    {
        (int Offset, int Length) block = FindBlock(data, blocks, magic);
        return block.Length == 0
            ? ReadOnlyMemory<byte>.Empty
            : data.Slice(block.Offset + 8, block.Length - 8).ToArray();
    }

    private static (int Offset, int Length) FindBlock(ReadOnlySpan<byte> data,
        IReadOnlyList<(int Offset, int Length)> blocks, ReadOnlySpan<byte> magic)
    {
        (int Offset, int Length) result = default;
        foreach ((int offset, int length) in blocks)
        {
            if (!data.Slice(offset, 4).SequenceEqual(magic)) continue;
            if (result.Length != 0) throw new InvalidDataException("The NANR repeats a standard block.");
            result = (offset, length);
        }
        return result;
    }
}
