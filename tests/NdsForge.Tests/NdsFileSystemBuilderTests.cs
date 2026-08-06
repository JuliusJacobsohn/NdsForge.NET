using System.Buffers.Binary;

namespace NdsForge.Tests;

public sealed class NdsFileSystemBuilderTests
{
    [Fact]
    public void SupportsStructuralEditsAndDeterministicFileIds()
    {
        var builder = new NdsFileSystemBuilder();
        builder.AddFile("/z/root.bin", [3]);
        builder.AddFile("/a/two.bin", [2]);
        builder.AddFile("/a/one.bin", [1]);
        builder.MoveFile("/a/two.bin", "/z/moved.bin");
        builder.MoveDirectory("/a", "/renamed");
        builder.RemoveFile("/z/root.bin");

        NdsFileSystemBuildSnapshot first = builder.BuildSnapshot();
        NdsFileSystemBuildSnapshot second = builder.BuildSnapshot();

        Assert.Equal(first.FileNameTable, second.FileNameTable);
        Assert.Equal(["/renamed/one.bin", "/z/moved.bin"], first.FilesInIdOrder.Select(static file => file.Path));
        Assert.Equal((ushort)0xF000, first.DirectoryIds["/"]);
        Assert.Equal((ushort)0xF001, first.DirectoryIds["/renamed"]);
        Assert.Equal((ushort)0xF002, first.DirectoryIds["/z"]);
    }

    [Fact]
    public void PreservesExplicitEmptyDirectories()
    {
        var builder = new NdsFileSystemBuilder();
        builder.CreateDirectory("/empty/child");

        NdsFileSystemBuildSnapshot snapshot = builder.BuildSnapshot();

        Assert.Equal(3, snapshot.DirectoryIds.Count);
        Assert.Empty(snapshot.FilesInIdOrder);
        Assert.NotEmpty(snapshot.FileNameTable);
    }

    [Fact]
    public async Task SerializedTablesRoundTripThroughProductionReader()
    {
        var builder = new NdsFileSystemBuilder();
        builder.CreateDirectory("/empty");
        builder.AddFile("/root.dat", [0x10, 0x20]);
        builder.AddFile("/nested/second.bin", [0x30]);
        builder.AddFile("/nested/first.bin", [0x40, 0x50, 0x60]);

        using NdsImage image = NdsImage.Load(CreateImage(builder.BuildSnapshot()));

        Assert.Equal(["/", "/empty", "/nested"], image.FileSystem.Directories.Select(static value => value.FullPath));
        Assert.Equal(
            ["/root.dat", "/nested/first.bin", "/nested/second.bin"],
            image.FileSystem.Files.Select(static value => value.FullPath));
        Assert.Equal(
            [0x40, 0x50, 0x60],
            await image.FileSystem.GetFile("/nested/first.bin").ReadAllBytesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void RejectsFileAsParentWithoutCorruptingTree()
    {
        var builder = new NdsFileSystemBuilder();
        builder.AddFile("/occupied", [1]);

        Assert.Throws<IOException>(() => builder.AddFile("/occupied/child", [2]));
        Assert.Throws<IOException>(() => builder.CreateDirectory("/occupied/child"));
        Assert.Equal(["/occupied"], builder.Files.Select(static file => file.Path));
        Assert.Equal(["/"], builder.Directories);
    }

    [Fact]
    public void FailedMoveLeavesSourceUntouched()
    {
        var builder = new NdsFileSystemBuilder();
        builder.AddFile("/source", [1]);
        builder.AddFile("/occupied", [2]);

        Assert.Throws<IOException>(() => builder.MoveFile("/source", "/occupied/child"));
        Assert.Equal(["/occupied", "/source"], builder.Files.Select(static file => file.Path));
    }

    [Fact]
    public void RejectsMovingDirectoryIntoItsOwnSubtree()
    {
        var builder = new NdsFileSystemBuilder();
        builder.CreateDirectory("/parent/child");

        Assert.Throws<IOException>(() => builder.MoveDirectory("/parent", "/parent/child/new"));
    }

    private static byte[] CreateImage(NdsFileSystemBuildSnapshot snapshot)
    {
        const int fntOffset = 0x800;
        const int fatOffset = 0x1000;
        int payloadOffset = 0x1800;
        byte[] image = new byte[0x4000];
        SyntheticImage.CreateHeaderOnly().AsSpan(0, 0x200).CopyTo(image);
        snapshot.FileNameTable.CopyTo(image, fntOffset);
        for (int fileId = 0; fileId < snapshot.FilesInIdOrder.Count; fileId++)
        {
            ReadOnlySpan<byte> contents = snapshot.FilesInIdOrder[fileId].Contents.Span;
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(fatOffset + (fileId * 8)), (uint)payloadOffset);
            contents.CopyTo(image.AsSpan(payloadOffset));
            payloadOffset += contents.Length;
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(fatOffset + (fileId * 8) + 4), (uint)payloadOffset);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x40), fntOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x44), (uint)snapshot.FileNameTable.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x48), fatOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x4C), (uint)(snapshot.FilesInIdOrder.Count * 8));
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x15E), NdsChecksums.ComputeCrc16(image.AsSpan(0, 0x15E)));
        return image;
    }
}
