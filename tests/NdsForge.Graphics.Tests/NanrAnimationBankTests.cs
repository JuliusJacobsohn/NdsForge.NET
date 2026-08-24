using System.Buffers.Binary;
using NdsForge.Graphics.Animations;

namespace NdsForge.Graphics.Tests;

public sealed class NanrAnimationBankTests
{
    [Fact]
    public void ParseReadsAllSequenceAndFrameFields()
    {
        byte[] encoded = CreateNanr();

        NanrAnimationBank bank = NanrAnimationBank.Parse(encoded);

        Assert.Equal((ushort)0x0100, bank.Version);
        Assert.Equal((ushort)3, bank.DeclaredFrameCount);
        Assert.Equal(0x12345678u, bank.BankConstant);
        Assert.Equal(0x48u, bank.FrameDescriptorOffset);
        Assert.Equal(0x60u, bank.FramePayloadOffset);
        Assert.Equal([0x11, 0x22], bank.LabelData.ToArray());
        Assert.Equal([0x33, 0x44, 0x55, 0x66], bank.UserExtendedInfo.ToArray());

        Assert.Collection(bank.Sequences,
            sequence =>
            {
                Assert.Equal((ushort)0, sequence.DataType);
                Assert.Equal((ushort)1, sequence.PlaybackMode);
                Assert.Equal((ushort)0, sequence.LoopStartFrame);
                Assert.Equal((ushort)0xBEEF, sequence.SequenceFlags);
                Assert.Equal(0u, sequence.FrameOffset);
                Assert.Collection(sequence.Frames,
                    frame => AssertFrame(frame, 0, 5, 0xCAFE, 7),
                    frame => AssertFrame(frame, 2, 6, 0xBABE, 8));
            },
            sequence =>
            {
                Assert.Equal((ushort)2, sequence.DataType);
                Assert.Equal((ushort)3, sequence.PlaybackMode);
                Assert.Equal((ushort)1, sequence.LoopStartFrame);
                Assert.Equal((ushort)0xFACE, sequence.SequenceFlags);
                Assert.Equal(16u, sequence.FrameOffset);
                NanrFrame frame = Assert.Single(sequence.Frames);
                AssertFrame(frame, 4, 9, 0xABCD, 9);
            });
        Assert.Equal(encoded, bank.WritePreserved());
        Assert.NotSame(encoded, bank.WritePreserved());
    }

    [Fact]
    public void ParseRejectsFrameCountMismatch()
    {
        byte[] encoded = CreateNanr();
        BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(0x1A), 4);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => NanrAnimationBank.Parse(encoded));

        Assert.Contains("total-frame", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsUnsupportedVariant()
    {
        byte[] encoded = CreateNanr();
        BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(0x34), 3);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => NanrAnimationBank.Parse(encoded));

        Assert.Contains("variant", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsPayloadOutsideBank()
    {
        byte[] encoded = CreateNanr();
        BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(0x60), uint.MaxValue);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => NanrAnimationBank.Parse(encoded));

        Assert.Contains("payload", error.Message, StringComparison.Ordinal);
    }

    private static void AssertFrame(NanrFrame frame, uint offset, ushort duration, ushort flags, ushort cell)
    {
        Assert.Equal(offset, frame.DataOffset);
        Assert.Equal(duration, frame.Duration);
        Assert.Equal(flags, frame.DescriptorFlags);
        Assert.Equal(cell, frame.CellIndex);
    }

    private static byte[] CreateNanr()
    {
        byte[] result = new byte[0x9E];
        "RNAN"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 0xFEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), 0x0100);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)result.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), 3);

        "KNBA"u8.CopyTo(result.AsSpan(0x10));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x14), 0x78);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x18), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x1A), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x1C), 0x12345678);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x20), 0x48);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x24), 0x60);

        WriteSequence(result.AsSpan(0x30), 2, 0, 1, 0, 0xBEEF, 0);
        WriteSequence(result.AsSpan(0x40), 1, 2, 3, 1, 0xFACE, 16);
        WriteFrame(result.AsSpan(0x60), 0, 5, 0xCAFE);
        WriteFrame(result.AsSpan(0x68), 2, 6, 0xBABE);
        WriteFrame(result.AsSpan(0x70), 4, 9, 0xABCD);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x78), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x7A), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x7C), 9);

        "LBAL"u8.CopyTo(result.AsSpan(0x88));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x8C), 10);
        result[0x90] = 0x11;
        result[0x91] = 0x22;
        "TXEU"u8.CopyTo(result.AsSpan(0x92));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x96), 12);
        result[0x9A] = 0x33;
        result[0x9B] = 0x44;
        result[0x9C] = 0x55;
        result[0x9D] = 0x66;
        return result;
    }

    private static void WriteSequence(Span<byte> target, uint frames, ushort type, ushort playback,
        ushort loop, ushort flags, uint offset)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(target, frames);
        BinaryPrimitives.WriteUInt16LittleEndian(target[4..], type);
        BinaryPrimitives.WriteUInt16LittleEndian(target[6..], playback);
        BinaryPrimitives.WriteUInt16LittleEndian(target[8..], loop);
        BinaryPrimitives.WriteUInt16LittleEndian(target[10..], flags);
        BinaryPrimitives.WriteUInt32LittleEndian(target[12..], offset);
    }

    private static void WriteFrame(Span<byte> target, uint offset, ushort duration, ushort flags)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(target, offset);
        BinaryPrimitives.WriteUInt16LittleEndian(target[4..], duration);
        BinaryPrimitives.WriteUInt16LittleEndian(target[6..], flags);
    }
}
