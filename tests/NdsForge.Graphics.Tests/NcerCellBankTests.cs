using System.Buffers.Binary;
using NdsForge.Graphics.Sprites;

namespace NdsForge.Graphics.Tests;

public sealed class NcerCellBankTests
{
    [Fact]
    public void ParsePreservesAlignedZeroFillAndUnknownMappingValue()
    {
        const int rawMapping = 1244644;
        byte[] encoded = CreatePaddedNcer(rawMapping);

        NcerCellBank bank = NcerCellBank.Parse(encoded);
        byte[] canonical = bank.CreateBuilder().Build(preserveSourceLayout: false);

        Assert.Equal(rawMapping, (int)bank.Mapping);
        Assert.Equal(encoded, bank.CreateBuilder().Build());
        Assert.Equal(rawMapping, (int)NcerCellBank.Parse(canonical).Mapping);
    }

    [Fact]
    public void ParseRejectsNonzeroDeclaredAlignmentFill()
    {
        byte[] encoded = CreatePaddedNcer(0);
        encoded[^1] = 1;

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => NcerCellBank.Parse(encoded));

        Assert.Contains("alignment padding", error.Message, StringComparison.Ordinal);
    }

    private static byte[] CreatePaddedNcer(int mapping)
    {
        byte[] result = new byte[0x34];
        "RECN"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 0xFEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), 0x0100);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)result.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), 1);
        "KBEC"u8.CopyTo(result.AsSpan(0x10));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x14), 0x21);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x1C), 0x18);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x20), mapping);
        return result;
    }
}
