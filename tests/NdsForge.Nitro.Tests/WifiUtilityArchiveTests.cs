using System.Security.Cryptography;
using NdsForge.Nitro.Archives;

namespace NdsForge.Nitro.Tests;

public sealed class WifiUtilityArchiveTests
{
    [Theory]
    [InlineData(false, "6B22C77D5E45F116BABBB5612CF475D0236469118EE3CAFB26D25A83E22C199C")]
    [InlineData(true, "10104371547D644BE972439AFEE696AC2DDBCADBF40075CD13CFF58E8BBA6C20")]
    public void EveryAllocationAndNameRelationshipIsExposed(bool unnamed, string digest)
    {
        byte[] bytes = WifiUtilityFixture.Create(unnamed);
        Assert.Equal(digest, Convert.ToHexString(SHA256.HashData(bytes)));
        WifiUtilityArchive archive = WifiUtilityArchive.Parse(bytes);
        Assert.Equal(unnamed ? 4 : 3, archive.Files.Count);
        Assert.Equal(2, archive.Directories.Count);
        Assert.Equal(16, archive.NameTableOffset);
        Assert.Equal(46, archive.NameTableLength);
        Assert.Equal(64, archive.AllocationTableOffset);
        Assert.Equal(unnamed ? 32 : 24, archive.AllocationTableLength);
        Assert.Equal((ushort)0xF000, archive.Root.Id);
        Assert.Null(archive.Root.ParentId);
        Assert.Equal("/", archive.Root.FullPath);
        Assert.Equal([0], archive.Root.FileIds);
        Assert.Equal([(ushort)0xF001], archive.Root.ChildIds);
        Assert.Equal((ushort)0xF000, archive.Directories[1].ParentId);
        Assert.Equal([1, 2], archive.Directories[1].FileIds);
        Assert.Equal("/sub/b.bin", archive.Files[1].FullPath);
        Assert.Equal("b.bin", archive.Files[1].Name);
        Assert.Same(archive.Files[1], archive.FindFile("/sub/b.bin"));
        Assert.Null(archive.FindFile("/SUB/b.bin"));
        Assert.Throws<ArgumentNullException>(() => archive.FindFile(null!));
        Assert.Empty(archive.Files[2].Data.ToArray());
        if (unnamed)
        {
            Assert.Null(archive.Files[3].FullPath);
            Assert.Null(archive.Files[3].ParentId);
            Assert.Equal([1, 3, 5, 7, 9], archive.Files[3].Data.ToArray());
        }
        Assert.Equal(bytes, archive.CreateBuilder().Build());
        bytes.AsSpan().Clear();
        Assert.Equal(digest, Convert.ToHexString(SHA256.HashData(archive.WritePreserved())));
    }

    [Theory]
    [InlineData(4, 112, "6B22C77D5E45F116BABBB5612CF475D0236469118EE3CAFB26D25A83E22C199C")]
    [InlineData(32, 160, "EFA43CE92665EB6C1B95C76E489507957B6C918F3391D3B9121E1D93742292E9")]
    public void CanonicalAlignmentMatchesCompleteOutputIdentity(int alignment, int length, string digest)
    {
        WifiUtilityArchive archive = WifiUtilityArchive.Parse(WifiUtilityFixture.Create());
        byte[] output = archive.CreateBuilder().Build(new() { PreserveSourceLayout = false, FileAlignment = alignment });
        Assert.Equal(length, output.Length);
        Assert.Equal(digest, Convert.ToHexString(SHA256.HashData(output)));
        Assert.Equal(archive.Files.Select(static file => file.FullPath), WifiUtilityArchive.Parse(output).Files.Select(static file => file.FullPath));
    }

    [Fact]
    public void ShrinkingAnAllocationRetainsItsIdentityAndEmptyEntries()
    {
        WifiUtilityArchive archive = WifiUtilityArchive.Parse(WifiUtilityFixture.Create());
        byte[] output = archive.CreateBuilder().ReplaceFile(0, []).Build();
        Assert.Equal("224F49A7869F03FB7F037274591A605EEACBCB32996ECC1BD76FE2FDE8D0CA6A", Convert.ToHexString(SHA256.HashData(output)));
        Assert.Equal(108, output.Length);
        WifiUtilityArchive parsed = WifiUtilityArchive.Parse(output);
        Assert.Empty(parsed.Files[0].Data.ToArray());
        Assert.Equal("/a.bin", parsed.Files[0].FullPath);
        Assert.Equal(archive.Files[1].Data.ToArray(), parsed.Files[1].Data.ToArray());
    }

    [Fact]
    public void GrowingAndRenamingAnAllocationMatchesCompleteOutputIdentity()
    {
        WifiUtilityArchive archive = WifiUtilityArchive.Parse(WifiUtilityFixture.Create());
        byte[] grown = Enumerable.Range(0, 33).Select(static index => (byte)(index * 7 + 3)).ToArray();
        byte[] output = archive.CreateBuilder().ReplaceFile(0, grown).RenameFile(0, "z.bin").Build();
        Assert.Equal("78CDA2139DAC8C870E3161F127E3ED76CEC30A0456E1758943782AEB75F34825", Convert.ToHexString(SHA256.HashData(output)));
        Assert.Equal(144, output.Length);
        Assert.Equal(grown, WifiUtilityArchive.Parse(output).FindFile("/z.bin")!.Data.ToArray());
    }

    [Fact]
    public void SameSizedReplacementPreservesOpaquePaddingAndDoesNotAliasCallerBuffers()
    {
        byte[] source = [.. WifiUtilityFixture.Create(), 9, 8, 7];
        source[62] = 0xA5;
        source[91] = 0xFE;
        WifiUtilityArchive archive = WifiUtilityArchive.Parse(source);
        byte[] replacement = [3, 2, 1];
        WifiUtilityArchiveBuilder builder = archive.CreateBuilder().ReplaceFile("/a.bin", replacement);
        replacement.AsSpan().Clear();
        byte[] output = builder.Build();
        Assert.Equal(source.Length, output.Length);
        byte[] expected = (byte[])source.Clone();
        new byte[] { 3, 2, 1 }.CopyTo(expected, 88);
        Assert.Equal(expected, output);
        Assert.Equal(source, archive.WritePreserved());
    }

    [Fact]
    public void RenamingLongerAndShorterSegmentsRebasesSubtablesWithoutChangingIds()
    {
        WifiUtilityArchive archive = WifiUtilityArchive.Parse(WifiUtilityFixture.Create());
        byte[] output = archive.CreateBuilder().RenameFile(0, "long-name.bin").RenameDirectory(0xF001, "x").RenameFile(1, "c").Build();
        WifiUtilityArchive renamed = WifiUtilityArchive.Parse(output);
        Assert.Equal(["/long-name.bin", "/x/c", "/x/empty.bin"], renamed.Files.Select(static file => file.FullPath));
        Assert.Equal("x", renamed.Directories[1].Name);
        Assert.Equal(archive.Files.Select(static file => file.Data.ToArray()), renamed.Files.Select(static file => file.Data.ToArray()));
        Assert.Equal(archive.Files.Select(static file => file.Id), renamed.Files.Select(static file => file.Id));
    }

    [Fact]
    public void SharedAllocationsArePreservedOnNoOpAndSeparatedWhenAnEditWouldAffectAnotherFile()
    {
        byte[] source = WifiUtilityFixture.Create();
        WifiUtilityFixture.Write32(source, 72, 88);
        WifiUtilityFixture.Write32(source, 76, 91);
        WifiUtilityArchive archive = WifiUtilityArchive.Parse(source);
        Assert.Equal(source, archive.CreateBuilder().Build());
        byte[] output = archive.CreateBuilder().ReplaceFile(0, [9, 8, 7]).Build();
        WifiUtilityArchive parsed = WifiUtilityArchive.Parse(output);
        Assert.Equal([9, 8, 7], parsed.Files[0].Data.ToArray());
        Assert.Equal([0x11, 0x22, 0x33], parsed.Files[1].Data.ToArray());
        Assert.NotEqual(parsed.Files[0].Offset, parsed.Files[1].Offset);
    }

    [Fact]
    public void RevertingRenamesRestoresExactPreservation()
    {
        byte[] source = [.. WifiUtilityFixture.Create(), 1, 2, 3];
        WifiUtilityArchive archive = WifiUtilityArchive.Parse(source);
        byte[] output = archive.CreateBuilder().RenameFile(0, "other").RenameDirectory(0xF001, "other")
            .RenameFile(0, "a.bin").RenameDirectory(0xF001, "sub").Build();
        Assert.Equal(source, output);
    }

    [Fact]
    public void EmptyArchiveAndUnusedZeroAllocationRemainRepresentable()
    {
        byte[] empty = new byte[28];
        WifiUtilityFixture.Write32(empty, 0, 16);
        WifiUtilityFixture.Write32(empty, 4, 9);
        WifiUtilityFixture.Write32(empty, 8, 28);
        WifiUtilityFixture.Write32(empty, 16, 8);
        empty[22] = 1;
        WifiUtilityArchive archive = WifiUtilityArchive.Parse(empty, new() { MaximumFileCount = 0 });
        Assert.Empty(archive.Files);
        Assert.Single(archive.Directories);
        Assert.Equal(empty, archive.CreateBuilder().Build(new() { PreserveSourceLayout = false }));
        byte[] unused = [.. empty, .. new byte[8]];
        WifiUtilityFixture.Write32(unused, 12, 8);
        WifiUtilityArchive withUnused = WifiUtilityArchive.Parse(unused);
        Assert.Null(Assert.Single(withUnused.Files).Name);
        Assert.Empty(withUnused.Files[0].Data.ToArray());
        Assert.Equal(unused, withUnused.CreateBuilder().Build());
        Assert.Equal(36, withUnused.CreateBuilder().Build(new() { PreserveSourceLayout = false }).Length);
    }

    [Fact]
    public void TableAndPayloadAlignmentAreIndependentAndOpaqueTablePaddingSurvives()
    {
        byte[] bytes = WifiUtilityFixture.Create();
        WifiUtilityFixture.Write32(bytes, 4, 47);
        bytes[62] = 0xFE;
        WifiUtilityArchive archive = WifiUtilityArchive.Parse(bytes);
        byte[] output = archive.CreateBuilder().Build(new()
        {
            PreserveSourceLayout = false,
            TableAlignment = 128,
            FileAlignment = 64,
            PaddingByte = 0xA5,
        });
        WifiUtilityArchive rebuilt = WifiUtilityArchive.Parse(output);
        Assert.Equal(128, rebuilt.AllocationTableOffset);
        Assert.Equal(192, rebuilt.Files[0].Offset);
        Assert.Equal(0xFE, output[62]);
        Assert.All(output[63..128], value => Assert.Equal((byte)0xA5, value));
    }
}
