using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsElfProgramImporterTests
{
    [Fact]
    public void ImportsUnorderedSegmentsFillsGapsAndTranslatesEntryPoint()
    {
        byte[] elf = CreateElf(
            0x0000_2001,
            new(0x0000_2000, 0x0200_0008, [9, 10]),
            new(0x0000_1000, 0x0200_0000, [1, 2, 3, 4]));

        NdsElfImportResult result = NdsElfProgramImporter.Import(elf, NdsProcessor.Arm9);

        Assert.Equal(0x0200_0000u, result.Program.LoadAddress);
        Assert.Equal(0x0200_0009u, result.Program.EntryAddress);
        Assert.Equal([1, 2, 3, 4, 0, 0, 0, 0, 9, 10], result.Program.Contents.ToArray());
        Assert.False(result.HasOverlaySegments);
        Assert.Empty(result.Overlays);
    }

    [Fact]
    public void SelectsOriginalAndTwlModesIndependently()
    {
        byte[] elf = CreateElf(
            0x0200_0000,
            new(0x0200_0000, 0x0200_0000, [1, 2]),
            new(0x02E0_0000, 0x02E0_0000, [3, 4, 5], Flags: 0x0010_0000));

        NdsElfImportResult ntr = NdsElfProgramImporter.Import(elf, NdsProcessor.Arm9);
        NdsElfImportResult twl = NdsElfProgramImporter.Import(elf, NdsProcessor.Arm9i);

        Assert.Equal([1, 2], ntr.Program.Contents.ToArray());
        Assert.Equal([3, 4, 5], twl.Program.Contents.ToArray());
        Assert.Equal(0x02E0_0000u, twl.Program.LoadAddress);
    }

    [Fact]
    public void DsiArm7ImportReportsWramFromBssOnlySegments()
    {
        byte[] elf = CreateElf(
            0x02E0_0000,
            new(0x0300_2000, 0x0300_2000, [], MemorySize: 0x80, Flags: 0x0010_0000),
            new(0x02E0_0000, 0x02E0_0000, [7, 8], Flags: 0x0010_0000));

        NdsElfImportResult result = NdsElfProgramImporter.Import(elf, NdsProcessor.Arm7i);

        Assert.Equal(0x0300_2000u, result.Arm7WramAddress);
        Assert.Equal([7, 8], result.Program.Contents.ToArray());
    }

    [Fact]
    public void ImportsOverlayTablePayloadsBssAndPackedControlFields()
    {
        var table = new byte[24];
        WriteUInt32(table, 0, 0x0200_1000);
        WriteUInt32(table, 4, 0x0200_1010);
        WriteUInt32(table, 8, 0xAB00_0012);
        WriteUInt32(table, 12, 0x0200_2000);
        WriteUInt32(table, 16, 0x0200_2020);
        byte[] elf = CreateElf(
            0x0200_0000,
            new(0x0200_0000, 0x0200_0000, [0x11]),
            new(0, 0, table, Flags: 0x0020_0000),
            new(0x0230_0000, 0x0230_0000, [1, 2, 3], MemorySize: 8, Flags: 0x0020_0000),
            new(0x0231_0000, 0x0231_0000, [4, 5], MemorySize: 2, Flags: 0x0020_0000));

        NdsElfImportResult result = NdsElfProgramImporter.Import(elf, NdsProcessor.Arm9);

        Assert.True(result.HasOverlaySegments);
        Assert.Equal(2, result.Overlays.Count);
        NdsOverlayDefinition first = result.Overlays[0];
        Assert.Equal(0u, first.Id);
        Assert.Equal(0x0230_0000u, first.LoadAddress);
        Assert.Equal(3u, first.RamSize);
        Assert.Equal(5u, first.BssSize);
        Assert.Equal(0x0200_1000u, first.StaticInitializerStart);
        Assert.Equal(0x0200_1010u, first.StaticInitializerEnd);
        Assert.Equal(0x12u, first.CompressedSize);
        Assert.Equal(0xAB, first.Flags);
        Assert.Equal([1, 2, 3], first.Contents.ToArray());
    }

    [Fact]
    public void OverlayImportCanBeDisabledWithoutHidingItsPresence()
    {
        byte[] table = new byte[12];
        byte[] elf = CreateElf(
            0x0200_0000,
            new(0x0200_0000, 0x0200_0000, [1]),
            new(0, 0, table, Flags: 0x0020_0000),
            new(0x0230_0000, 0x0230_0000, [2], Flags: 0x0020_0000));

        NdsElfImportResult result = NdsElfProgramImporter.Import(
            elf,
            NdsProcessor.Arm9,
            new() { ImportOverlays = false });

        Assert.True(result.HasOverlaySegments);
        Assert.Empty(result.Overlays);
    }

    [Fact]
    public void MalformedHeadersSegmentsAndOverlapsAreRejected()
    {
        byte[] invalidClass = CreateElf(0, new TestSegment(0x0200_0000, 0x0200_0000, [1]));
        invalidClass[4] = 2;
        byte[] invalidMemory = CreateElf(0, new TestSegment(0x0200_0000, 0x0200_0000, [1, 2], MemorySize: 1));
        byte[] invalidAlignment = CreateElf(0, new TestSegment(0x0200_0000, 0x0200_0000, [1], Alignment: 3));
        byte[] overlapping = CreateElf(
            0,
            new(0, 0x0200_0000, [1, 2, 3]),
            new(4, 0x0200_0002, [4, 5]));
        byte[] excessiveGap = CreateElf(
            0,
            new(0, 0, [1]),
            new(0x1000_0000, 0x1000_0000, [2]));
        byte[] incompleteOverlays = CreateElf(
            0,
            new(0, 0x0200_0000, [1]),
            new(0, 0, new byte[12], Flags: 0x0020_0000));

        Assert.Throws<InvalidDataException>(() => NdsElfProgramImporter.Import(invalidClass, NdsProcessor.Arm9));
        Assert.Throws<InvalidDataException>(() => NdsElfProgramImporter.Import(invalidMemory, NdsProcessor.Arm9));
        Assert.Throws<InvalidDataException>(() => NdsElfProgramImporter.Import(invalidAlignment, NdsProcessor.Arm9));
        Assert.Throws<InvalidDataException>(() => NdsElfProgramImporter.Import(overlapping, NdsProcessor.Arm9));
        Assert.Throws<InvalidDataException>(() => NdsElfProgramImporter.Import(excessiveGap, NdsProcessor.Arm9));
        Assert.Throws<InvalidDataException>(() => NdsElfProgramImporter.Import(incompleteOverlays, NdsProcessor.Arm9));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NdsElfProgramImporter.Import(invalidClass, (NdsProcessor)99));
    }

    [Fact]
    public async Task StreamImportHonorsBoundsOwnershipAndCancellation()
    {
        byte[] elf = CreateElf(0x0200_0000, new TestSegment(0x0200_0000, 0x0200_0000, [1, 2, 3]));
        var retained = new MemoryStream(elf);
        NdsElfImportResult result = await NdsElfProgramImporter.ImportAsync(
            retained,
            NdsProcessor.Arm7,
            leaveOpen: true,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        var disposed = new MemoryStream(elf);
        await NdsElfProgramImporter.ImportAsync(
            disposed,
            NdsProcessor.Arm7,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal([1, 2, 3], result.Program.Contents.ToArray());
        Assert.True(retained.CanRead);
        Assert.False(disposed.CanRead);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await NdsElfProgramImporter.ImportAsync(
                new MemoryStream(elf),
                NdsProcessor.Arm7,
                options: new() { MaxInputBytes = 52 },
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task ImportedResultAppliesDirectlyToAValidatedBuildRecipe()
    {
        byte[] elf = CreateElf(0x0200_0000, new TestSegment(0x0200_0000, 0x0200_0000, [1, 2, 3, 4]));
        NdsElfImportResult import = NdsElfProgramImporter.Import(elf, NdsProcessor.Arm9);
        var builder = new NdsImageBuilder
        {
            GameCode = "ELF1",
            MakerCode = "HB",
            Arm7 = new(NdsProcessor.Arm7, [7, 8], 0x0238_0000, 0x0238_0000),
        };
        import.ApplyTo(builder);

        byte[] imageBytes = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        using NdsImage image = NdsImage.Load(imageBytes);

        Assert.Equal(0x0200_0000u, image.Header.Arm9.LoadAddress);
        Assert.True(image.Validate().IsValid);
    }

    private static byte[] CreateElf(uint entryPoint, params TestSegment[] segments)
    {
        const int headerSize = 52;
        const int programHeaderSize = 32;
        int dataOffset = checked(headerSize + (segments.Length * programHeaderSize));
        int totalLength = checked(dataOffset + segments.Sum(static segment => segment.Data.Length));
        var output = new byte[totalLength];
        output[0] = 0x7F;
        "ELF"u8.CopyTo(output.AsSpan(1));
        output[4] = 1;
        output[5] = 1;
        output[6] = 1;
        WriteUInt16(output, 16, 2);
        WriteUInt16(output, 18, 40);
        WriteUInt32(output, 20, 1);
        WriteUInt32(output, 24, entryPoint);
        WriteUInt32(output, 28, headerSize);
        WriteUInt16(output, 40, headerSize);
        WriteUInt16(output, 42, programHeaderSize);
        WriteUInt16(output, 44, checked((ushort)segments.Length));
        int currentData = dataOffset;
        for (int index = 0; index < segments.Length; index++)
        {
            TestSegment segment = segments[index];
            int item = headerSize + (index * programHeaderSize);
            WriteUInt32(output, item, 1);
            WriteUInt32(output, item + 4, checked((uint)currentData));
            WriteUInt32(output, item + 8, segment.VirtualAddress);
            WriteUInt32(output, item + 12, segment.PhysicalAddress);
            WriteUInt32(output, item + 16, checked((uint)segment.Data.Length));
            WriteUInt32(output, item + 20, segment.MemorySize ?? checked((uint)segment.Data.Length));
            WriteUInt32(output, item + 24, segment.Flags);
            WriteUInt32(output, item + 28, segment.Alignment);
            segment.Data.CopyTo(output, currentData);
            currentData += segment.Data.Length;
        }

        return output;
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);

    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);

    private sealed record TestSegment(
        uint VirtualAddress,
        uint PhysicalAddress,
        byte[] Data,
        uint? MemorySize = null,
        uint Flags = 0,
        uint Alignment = 1);
}
