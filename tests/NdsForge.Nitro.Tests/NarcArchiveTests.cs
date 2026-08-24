using System.Buffers.Binary;
using NdsForge.Nitro.Archives;
using NdsForge.Nitro.Containers;

namespace NdsForge.Nitro.Tests;

public sealed class NarcArchiveTests
{
    [Fact]
    public void ParsesNamedHierarchyAndStableFileIds()
    {
        byte[] nameTable =
        [
            16, 0, 0, 0, 0, 0, 2, 0,
            23, 0, 0, 0, 1, 0, 0, 0xF0,
            1, (byte)'a', 0x81, (byte)'d', 1, 0xF0, 0,
            1, (byte)'b', 0, 0xFF, 0xFF,
        ];
        byte[] encoded = CreateArchive(["first"u8.ToArray(), "second"u8.ToArray()], nameTable);

        NarcArchive archive = NarcArchive.Parse(encoded);

        Assert.Equal(2, archive.Files.Count);
        Assert.Equal("/a", archive.GetFile(0).FullPath);
        Assert.Equal("/d/b", archive.GetFile(1).FullPath);
        Assert.Equal("second"u8.ToArray(), archive.FindFile("/d/b")!.Data.ToArray());
        Assert.Single(archive.Root.Directories);
        Assert.Equal((ushort)0xF001, archive.Root.Directories[0].Id);
    }

    [Fact]
    public void PreservesCompleteSourceAndPatchesSameLengthReplacementInPlace()
    {
        byte[] encoded = CreateArchive(
            ["ABC"u8.ToArray()],
            [8, 0, 0, 0, 0, 0, 1, 0, 0, 0xFF, 0xFF, 0xFF],
            trailing: [0xEE, 0xEE]);
        NarcArchive archive = NarcArchive.Parse(encoded);

        byte[] unchanged = archive.CreateBuilder().Build();
        byte[] changed = archive.CreateBuilder().ReplaceFile(0, "XYZ"u8).Build();

        Assert.Equal(encoded, unchanged);
        Assert.Equal(encoded.Length, changed.Length);
        Assert.Equal([0xEE, 0xEE], changed[^2..]);
        Assert.Equal("XYZ"u8.ToArray(), NarcArchive.Parse(changed).GetFile(0).Data.ToArray());
    }

    [Theory]
    [InlineData(NitroByteOrder.LittleEndian)]
    [InlineData(NitroByteOrder.BigEndian)]
    public void RebuildsSizeChangingPayloadWithSelectedHeaderOrder(NitroByteOrder byteOrder)
    {
        byte[] encoded = CreateArchive(
            ["ABC"u8.ToArray()],
            [8, 0, 0, 0, 0, 0, 1, 0, 0, 0xFF, 0xFF, 0xFF]);
        NarcArchive source = NarcArchive.Parse(encoded);

        byte[] rebuilt = source.CreateBuilder()
            .ReplaceFile(0, "a longer payload"u8)
            .Build(new NarcWriteOptions
            {
                HeaderByteOrder = byteOrder,
                PaddingByte = 0x7E,
                PreserveSourceLayout = false,
            });
        NarcArchive reparsed = NarcArchive.Parse(rebuilt);

        Assert.Equal(byteOrder, reparsed.HeaderByteOrder);
        Assert.Equal("a longer payload"u8.ToArray(), reparsed.GetFile(0).Data.ToArray());
        Assert.Equal(rebuilt.Length, BinaryPrimitives.ReadInt32LittleEndian(rebuilt.AsSpan(8)));
        Assert.Equal(0, rebuilt.Length % 4);
    }

    [Fact]
    public void RejectsMalformedHeadersBlocksAllocationsNamesAndLimits()
    {
        byte[] valid = CreateArchive(
            ["ABC"u8.ToArray()],
            [8, 0, 0, 0, 0, 0, 1, 0, 0, 0xFF, 0xFF, 0xFF]);

        Assert.Throws<InvalidDataException>(() => NarcArchive.Parse(valid.AsSpan()[..15]));
        Assert.Throws<InvalidDataException>(() => NarcArchive.Parse(Mutate(valid, 16, (byte)'X')));
        Assert.Throws<InvalidDataException>(() => NarcArchive.Parse(Mutate(valid, 32, 5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => NarcArchive.Parse(
            valid,
            new NarcReadOptions { MaximumFileCount = 0 }));
        Assert.Throws<InvalidDataException>(() => NarcArchive.Parse(CreateArchive(
            ["ABC"u8.ToArray()],
            [8, 0, 0, 0, 0, 0, 1, 0, 1, (byte)'a', 1, (byte)'a', 0, 0, 0])));
    }

    [Fact]
    public void CreatesDeterministicUnnamedArchiveFromPayloads()
    {
        ReadOnlyMemory<byte>[] files = ["one"u8.ToArray(), "second"u8.ToArray()];

        NarcArchive archive = NarcArchive.CreateUnnamed(files);
        byte[] first = archive.CreateBuilder().Build();
        byte[] second = NarcArchive.CreateUnnamed(files).CreateBuilder().Build();

        Assert.Equal(NitroByteOrder.BigEndian, archive.HeaderByteOrder);
        Assert.Equal(first, second);
        Assert.Equal("one"u8.ToArray(), archive.GetFile(0).Data.ToArray());
        Assert.Null(archive.GetFile(0).FullPath);
    }

    private static byte[] CreateArchive(
        IReadOnlyList<byte[]> files,
        byte[] nameTable,
        NitroByteOrder byteOrder = NitroByteOrder.LittleEndian,
        byte[]? trailing = null)
    {
        int fatLength = 12 + (files.Count * 8);
        int imagePayloadLength = files.Sum(static file => (file.Length + 3) & ~3);
        int imageLength = 8 + imagePayloadLength;
        int declaredLength = 16 + fatLength + 8 + nameTable.Length + imageLength;
        byte[] result = new byte[declaredLength + (trailing?.Length ?? 0)];
        "NARC"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), byteOrder == NitroByteOrder.LittleEndian ? (ushort)0xFEFF : (ushort)0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), byteOrder == NitroByteOrder.LittleEndian ? (ushort)1 : (ushort)0x0100);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), declaredLength);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), 3);
        "BTAF"u8.CopyTo(result.AsSpan(16));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(20), fatLength);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(24), files.Count);
        int payloadCursor = 0;
        for (int index = 0; index < files.Count; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28 + (index * 8)), payloadCursor);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(32 + (index * 8)), payloadCursor + files[index].Length);
            payloadCursor = (payloadCursor + files[index].Length + 3) & ~3;
        }

        int fntOffset = 16 + fatLength;
        "BTNF"u8.CopyTo(result.AsSpan(fntOffset));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(fntOffset + 4), nameTable.Length + 8);
        nameTable.CopyTo(result, fntOffset + 8);
        int imageOffset = fntOffset + 8 + nameTable.Length;
        "GMIF"u8.CopyTo(result.AsSpan(imageOffset));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(imageOffset + 4), imageLength);
        payloadCursor = imageOffset + 8;
        foreach (byte[] file in files)
        {
            file.CopyTo(result, payloadCursor);
            payloadCursor = (payloadCursor + file.Length + 3) & ~3;
        }

        trailing?.CopyTo(result, declaredLength);
        return result;
    }

    private static byte[] Mutate(byte[] source, int offset, byte value)
    {
        byte[] result = source.ToArray();
        result[offset] = value;
        return result;
    }
}
