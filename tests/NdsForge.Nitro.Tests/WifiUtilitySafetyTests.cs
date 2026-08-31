using NdsForge.Nitro.Archives;

namespace NdsForge.Nitro.Tests;

public sealed class WifiUtilitySafetyTests
{
    [Theory]
    [InlineData("header")]
    [InlineData("payload")]
    [InlineData("name-range")]
    [InlineData("fat-range")]
    [InlineData("fat-stride")]
    [InlineData("tables-overlap")]
    [InlineData("header-overlap")]
    [InlineData("directory-count")]
    [InlineData("subtable")]
    [InlineData("cycle")]
    [InlineData("parent")]
    [InlineData("child-id")]
    [InlineData("file-id")]
    [InlineData("reversed")]
    [InlineData("outside")]
    [InlineData("payload-metadata")]
    [InlineData("reserved-name")]
    [InlineData("name-truncation")]
    [InlineData("missing-terminator")]
    [InlineData("separator")]
    [InlineData("unreachable")]
    [InlineData("duplicate-file-id")]
    [InlineData("overlapping-subtables")]
    public void MalformedRangesAndRelationshipsFailBoundedly(string kind)
    {
        byte[] data = WifiUtilityFixture.Create();
        switch (kind)
        {
            case "header": data = data[..12]; break;
            case "payload": data = data[..108]; break;
            case "name-range": WifiUtilityFixture.Write32(data, 4, uint.MaxValue); break;
            case "fat-range": WifiUtilityFixture.Write32(data, 8, uint.MaxValue); break;
            case "fat-stride": WifiUtilityFixture.Write32(data, 12, 23); break;
            case "tables-overlap": WifiUtilityFixture.Write32(data, 8, 32); break;
            case "header-overlap": WifiUtilityFixture.Write32(data, 0, 4); break;
            case "directory-count": data[22] = 0; break;
            case "subtable": WifiUtilityFixture.Write32(data, 16, uint.MaxValue); break;
            case "cycle": data[42] = 0; break;
            case "parent": data[30] = 1; break;
            case "child-id": data[42] = 3; break;
            case "file-id": data[28] = 3; break;
            case "reversed": WifiUtilityFixture.Write32(data, 68, 87); break;
            case "outside": WifiUtilityFixture.Write32(data, 68, uint.MaxValue); break;
            case "payload-metadata": WifiUtilityFixture.Write32(data, 64, 40); break;
            case "reserved-name": data[32] = 0x80; break;
            case "name-truncation": data[32] = 127; break;
            case "missing-terminator": data[61] = 1; break;
            case "separator": data[33] = (byte)'/'; break;
            case "unreachable": data[38] = 0; break;
            case "duplicate-file-id": data[28] = 0; break;
            case "overlapping-subtables": WifiUtilityFixture.Write32(data, 24, 28); break;
        }
        Assert.Throws<InvalidDataException>(() => WifiUtilityArchive.Parse(data));
    }

    [Fact]
    public void InputLimitsApplyBeforeGraphTraversalAndAllowExactBoundaries()
    {
        byte[] data = WifiUtilityFixture.Create();
        Assert.Throws<InvalidDataException>(() => WifiUtilityArchive.Parse(data, new() { MaximumArchiveBytes = 111 }));
        Assert.Throws<InvalidDataException>(() => WifiUtilityArchive.Parse(data, new() { MaximumFileCount = 2 }));
        Assert.Throws<InvalidDataException>(() => WifiUtilityArchive.Parse(data, new() { MaximumDirectoryCount = 1 }));
        Assert.Throws<InvalidDataException>(() => WifiUtilityArchive.Parse(data, new() { MaximumDirectoryDepth = 0 }));
        Assert.Equal(3, WifiUtilityArchive.Parse(data, new()
        {
            MaximumArchiveBytes = 112,
            MaximumFileCount = 3,
            MaximumDirectoryCount = 2,
            MaximumDirectoryDepth = 1,
        }).Files.Count);
    }

    [Fact]
    public void InvalidReadLimitsFailExplicitly()
    {
        WifiUtilityReadOptions[] limits =
        [
            new() { MaximumArchiveBytes = 0 }, new() { MaximumArchiveBytes = int.MaxValue },
            new() { MaximumFileCount = -1 }, new() { MaximumFileCount = 61441 },
            new() { MaximumDirectoryCount = 0 }, new() { MaximumDirectoryCount = 4097 },
            new() { MaximumDirectoryDepth = -1 },
        ];
        foreach (WifiUtilityReadOptions option in limits)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WifiUtilityArchive.Parse([], option));
        }
    }

    [Fact]
    public void OutputLimitsCoverPreservationAlignmentGrowthAndRenaming()
    {
        WifiUtilityArchive archive = WifiUtilityArchive.Parse(WifiUtilityFixture.Create());
        Assert.Throws<InvalidDataException>(() => archive.CreateBuilder().Build(new() { MaximumOutputBytes = 111 }));
        Assert.Throws<InvalidDataException>(() => archive.CreateBuilder().Build(new() { PreserveSourceLayout = false, MaximumOutputBytes = 111 }));
        Assert.Throws<InvalidDataException>(() => archive.CreateBuilder().Build(new() { PreserveSourceLayout = false, FileAlignment = 1 << 30 }));
        Assert.Throws<InvalidDataException>(() => archive.CreateBuilder().ReplaceFile(0, new byte[200]).Build(new() { MaximumOutputBytes = 150 }));
        Assert.Throws<InvalidDataException>(() => archive.CreateBuilder().RenameFile(0, "long-name.bin").Build(new() { MaximumOutputBytes = 20 }));
        Assert.Equal(112, archive.CreateBuilder().Build(new() { MaximumOutputBytes = 112 }).Length);
    }

    [Fact]
    public void InvalidWriteLimitsFailExplicitly()
    {
        WifiUtilityArchiveBuilder builder = WifiUtilityArchive.Parse(WifiUtilityFixture.Create()).CreateBuilder();
        Assert.Throws<ArgumentException>(() => builder.Build(new() { FileAlignment = 0 }));
        Assert.Throws<ArgumentException>(() => builder.Build(new() { FileAlignment = 3 }));
        Assert.Throws<ArgumentException>(() => builder.Build(new() { TableAlignment = 2 }));
        Assert.Throws<ArgumentException>(() => builder.Build(new() { TableAlignment = 6 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(new() { MaximumOutputBytes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(new() { MaximumOutputBytes = int.MaxValue }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a\0b")]
    [InlineData("\u0100")]
    public void RenamesCannotInventAmbiguousOrLossySegments(string name)
    {
        WifiUtilityArchiveBuilder builder = WifiUtilityArchive.Parse(WifiUtilityFixture.Create()).CreateBuilder();
        Assert.Throws<InvalidDataException>(() => builder.RenameFile(0, name));
    }

    [Fact]
    public void InvalidIdsAndConflictingNamesNeverProduceAnArchive()
    {
        WifiUtilityArchiveBuilder builder = WifiUtilityArchive.Parse(WifiUtilityFixture.Create(true)).CreateBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.ReplaceFile(-1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.ReplaceFile(4, []));
        Assert.Throws<FileNotFoundException>(() => builder.ReplaceFile("/absent", []));
        Assert.Throws<InvalidOperationException>(() => builder.RenameFile(3, "name"));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.RenameDirectory(0xF000, "root"));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.RenameDirectory(0xFFFF, "absent"));
        Assert.Throws<ArgumentNullException>(() => builder.RenameFile(0, null!));
        Assert.Throws<ArgumentNullException>(() => builder.RenameDirectory(0xF001, null!));
        Assert.Throws<InvalidDataException>(() => builder.RenameFile(0, new string('x', 128)));
        Assert.Throws<InvalidDataException>(() => builder.RenameFile(0, "sub").Build());
    }
}
