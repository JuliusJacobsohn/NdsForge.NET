using System.Buffers.Binary;
using System.Text;
using NdsForge.Nitro.Containers;
using NdsForge.Nitro.Text;

namespace NdsForge.Nitro.Tests;

public sealed class BmgMessageBundleTests
{
    [Fact]
    public void ParsePreservesUtf16MessagesControlsAndAuxiliarySections()
    {
        byte[] encoded = CreateLittleEndianUtf16();

        BmgMessageBundle bundle = BmgMessageBundle.Parse(encoded);

        Assert.Equal(NitroByteOrder.LittleEndian, bundle.ByteOrder);
        Assert.Equal(BmgEncoding.Utf16, bundle.Encoding);
        Assert.Equal(0x12345678u, bundle.MessageId);
        Assert.Equal(0x11111111u, bundle.HeaderField14);
        Assert.Equal(0x22222222u, bundle.HeaderField18);
        Assert.Equal(0x33333333u, bundle.HeaderField1C);
        Assert.Equal(4u, bundle.DeclaredSectionCount);
        Assert.True(bundle.HasMissingTrailingSection);
        Assert.False(bundle.HasMissingTrailingPadding);
        BmgAuxiliarySection auxiliary = Assert.Single(bundle.AuxiliarySections);
        Assert.Equal("MID1", auxiliary.Signature);
        Assert.Equal([1, 2, 3], auxiliary.Data.ToArray());

        Assert.Collection(bundle.Messages,
            message =>
            {
                Assert.True(message.IsNull);
                Assert.Equal(0u, message.DataOffset);
                Assert.Equal([0x10, 0x20], message.Attributes.ToArray());
                Assert.Empty(message.Parts);
                Assert.Equal(string.Empty, message.GetText());
            },
            message =>
            {
                Assert.False(message.IsNull);
                Assert.Equal("AB", message.GetText());
                Assert.Equal([0x30, 0x40], message.Attributes.ToArray());
                Assert.Collection(message.Parts,
                    part => AssertText(part, [0x41, 0]),
                    part =>
                    {
                        Assert.Equal(BmgMessagePartKind.Control, part.Kind);
                        Assert.Equal((byte)0x7F, part.ControlCode);
                        Assert.Equal((byte)6, part.SerializedLength);
                        Assert.Equal([0xAA, 0xBB], part.Data.ToArray());
                    },
                    part => AssertText(part, [0x42, 0]));
            });
        Assert.Equal(encoded, bundle.WritePreserved());
        Assert.NotSame(encoded, bundle.WritePreserved());
    }

    [Fact]
    public void ParseReadsBigEndianWindows1252()
    {
        byte[] encoded = CreateSingleMessage(NitroByteOrder.BigEndian, BmgEncoding.Windows1252,
            [0x80, 0x20, 0x41, 0]);

        BmgMessageBundle bundle = BmgMessageBundle.Parse(encoded);

        Assert.Equal(NitroByteOrder.BigEndian, bundle.ByteOrder);
        Assert.Equal("€ A", Assert.Single(bundle.Messages).GetText());
        Assert.False(bundle.HasMissingTrailingSection);
        Assert.False(bundle.HasMissingTrailingPadding);
    }

    [Fact]
    public void ShiftJisRequiresCallerSelectedDecoder()
    {
        byte[] encoded = CreateSingleMessage(NitroByteOrder.BigEndian, BmgEncoding.ShiftJis,
            [0x41, 0x42, 0]);
        BmgMessage message = Assert.Single(BmgMessageBundle.Parse(encoded).Messages);

        Assert.Throws<InvalidOperationException>(() => message.GetText());
        Assert.Equal("AB", message.GetText(Encoding.ASCII));
    }

    [Fact]
    public void ParseAcceptsMissingFinalFlowIndexPadding()
    {
        byte[] inf = new byte[16];
        Write16(inf, 2, 4, NitroByteOrder.LittleEndian);
        byte[] encoded = CreateBundle(NitroByteOrder.LittleEndian, BmgEncoding.Utf8,
            [("INF1", inf), ("DAT1", new byte[] { 0 }), ("FLI1", new byte[8])], 3, 0);
        int flowIndex = encoded.Length - 16;
        Write32(encoded, 8, (uint)(encoded.Length + 8), NitroByteOrder.LittleEndian);
        Write32(encoded, flowIndex + 4, 24, NitroByteOrder.LittleEndian);

        BmgMessageBundle bundle = BmgMessageBundle.Parse(encoded);

        Assert.True(bundle.HasMissingTrailingPadding);
        Assert.Equal(8, Assert.Single(bundle.AuxiliarySections).Data.Length);
    }

    [Fact]
    public void ParseRejectsControlSequenceOutsideDat1()
    {
        byte[] encoded = CreateSingleMessage(NitroByteOrder.LittleEndian, BmgEncoding.Utf8,
            [0x1A, 0x20, 1, 0]);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => BmgMessageBundle.Parse(encoded));

        Assert.Contains("control-sequence", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMessageWithoutTerminator()
    {
        byte[] encoded = CreateSingleMessage(NitroByteOrder.LittleEndian, BmgEncoding.Utf8, [0x41]);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => BmgMessageBundle.Parse(encoded));

        Assert.Contains("null-terminated", error.Message, StringComparison.Ordinal);
    }

    private static void AssertText(BmgMessagePart part, byte[] expected)
    {
        Assert.Equal(BmgMessagePartKind.Text, part.Kind);
        Assert.Null(part.ControlCode);
        Assert.Equal((byte)0, part.SerializedLength);
        Assert.Equal(expected, part.Data.ToArray());
    }

    private static byte[] CreateLittleEndianUtf16()
    {
        byte[] dat = [0, 0, 0x41, 0, 0x1A, 0, 6, 0x7F, 0xAA, 0xBB, 0x42, 0, 0, 0];
        byte[] inf = new byte[28];
        Write16(inf, 0, 2, NitroByteOrder.LittleEndian);
        Write16(inf, 2, 6, NitroByteOrder.LittleEndian);
        Write32(inf, 4, 0x12345678, NitroByteOrder.LittleEndian);
        Write32(inf, 8, 0, NitroByteOrder.LittleEndian);
        inf[12] = 0x10; inf[13] = 0x20;
        Write32(inf, 14, 2, NitroByteOrder.LittleEndian);
        inf[18] = 0x30; inf[19] = 0x40;
        byte[] result = CreateBundle(NitroByteOrder.LittleEndian, BmgEncoding.Utf16,
            [("INF1", inf), ("DAT1", dat), ("MID1", new byte[] { 1, 2, 3 })], declaredSections: 4, padding: 5);
        Write32(result, 0x14, 0x11111111, NitroByteOrder.LittleEndian);
        Write32(result, 0x18, 0x22222222, NitroByteOrder.LittleEndian);
        Write32(result, 0x1C, 0x33333333, NitroByteOrder.LittleEndian);
        return result;
    }

    private static byte[] CreateSingleMessage(NitroByteOrder byteOrder, BmgEncoding encoding, byte[] message)
    {
        byte[] dat = new byte[1 + message.Length];
        message.CopyTo(dat, 1);
        byte[] inf = new byte[20];
        Write16(inf, 0, 1, byteOrder);
        Write16(inf, 2, 4, byteOrder);
        Write32(inf, 8, 1, byteOrder);
        return CreateBundle(byteOrder, encoding, [("INF1", inf), ("DAT1", dat)], 2, 0);
    }

    private static byte[] CreateBundle(NitroByteOrder byteOrder, BmgEncoding encoding,
        IReadOnlyList<(string Signature, byte[] Body)> sections, uint declaredSections, int padding)
    {
        int fileLength = 0x20 + sections.Sum(static section => 8 + section.Body.Length);
        byte[] result = new byte[fileLength + padding];
        "MESGbmg1"u8.CopyTo(result);
        Write32(result, 8, (uint)fileLength, byteOrder);
        Write32(result, 12, declaredSections, byteOrder);
        result[16] = (byte)encoding;
        int cursor = 0x20;
        foreach ((string signature, byte[] body) in sections)
        {
            Encoding.ASCII.GetBytes(signature).CopyTo(result, cursor);
            Write32(result, cursor + 4, (uint)(8 + body.Length), byteOrder);
            body.CopyTo(result, cursor + 8);
            cursor += 8 + body.Length;
        }
        result.AsSpan(fileLength).Fill(0xFF);
        return result;
    }

    private static void Write16(Span<byte> data, int offset, ushort value, NitroByteOrder byteOrder)
    {
        if (byteOrder == NitroByteOrder.LittleEndian)
            BinaryPrimitives.WriteUInt16LittleEndian(data[offset..], value);
        else
            BinaryPrimitives.WriteUInt16BigEndian(data[offset..], value);
    }

    private static void Write32(Span<byte> data, int offset, uint value, NitroByteOrder byteOrder)
    {
        if (byteOrder == NitroByteOrder.LittleEndian)
            BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);
        else
            BinaryPrimitives.WriteUInt32BigEndian(data[offset..], value);
    }
}
