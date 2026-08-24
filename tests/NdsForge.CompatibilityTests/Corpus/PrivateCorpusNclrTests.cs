using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Palettes;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Parses and reconstructs every direct NCLR palette allocation in the private corpus.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusNclrTests
{
    /// <summary>Locks exact colors and metadata after comparison with the independently compiled Texim parser.</summary>
    [Fact]
    public async Task EveryNclrPreservesAndCanonicallyRebuilds()
    {
        int archiveCount = 0;
        long colorCount = 0;
        int fourBitCount = 0;
        int eightBitCount = 0;
        int extendedCount = 0;
        int mappedCount = 0;
        var archiveDigests = new List<byte[]>();
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(
                CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            foreach (NdsFileAllocation allocation in image.FileSystem.Allocations)
            {
                using Stream stream = image.OpenRead(allocation.Data);
                Span<byte> signature = new byte[4];
                if (stream.Read(signature) != signature.Length || !signature.SequenceEqual("RLCN"u8))
                {
                    continue;
                }

                stream.Position = 0;
                byte[] encoded = new byte[allocation.Data.Length];
                stream.ReadExactly(encoded);
                NclrPalette palette = NclrPalette.Parse(encoded);
                Assert.Equal(encoded, palette.CreateBuilder().Build());
                NclrPalette canonical = NclrPalette.Parse(palette.CreateBuilder().Build(preserveSourceLayout: false));
                Assert.Equal(palette.Colors, canonical.Colors);
                Assert.Equal(palette.PaletteMapping, canonical.PaletteMapping);

                archiveCount++;
                colorCount += palette.Colors.Count;
                fourBitCount += palette.Depth == NitroColorDepth.Indexed4Bpp ? 1 : 0;
                eightBitCount += palette.Depth == NitroColorDepth.Indexed8Bpp ? 1 : 0;
                extendedCount += palette.IsExtendedPalette ? 1 : 0;
                mappedCount += palette.PaletteMapping is null ? 0 : 1;
                archiveDigests.Add(HashPalette(palette));
            }
        }

        using IncrementalHash aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] digest in archiveDigests.OrderBy(static value => Convert.ToHexString(value), StringComparer.Ordinal))
        {
            aggregate.AppendData(digest);
        }

        Assert.Equal(4006, archiveCount);
        Assert.Equal(1042096, colorCount);
        Assert.Equal(3352, fourBitCount);
        Assert.Equal(654, eightBitCount);
        Assert.Equal(37, extendedCount);
        Assert.Equal(291, mappedCount);
        Assert.Equal(
            "4ACFB97CE5B077C710F704D9D40F0C43F132C48F1605B71129E4184527A0B1A8",
            Convert.ToHexString(aggregate.GetHashAndReset()));
    }

    /// <summary>Frames format metadata, stored words, and optional mapping into a per-file digest.</summary>
    private static byte[] HashPalette(NclrPalette palette)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendInt32(hash, (int)palette.Depth);
        hash.AppendData([palette.IsExtendedPalette ? (byte)1 : (byte)0]);
        foreach (NitroColor555 color in palette.Colors)
        {
            AppendUInt16(hash, color.PackedValue);
        }

        if (palette.PaletteMapping is not null)
        {
            foreach (ushort value in palette.PaletteMapping)
            {
                AppendUInt16(hash, value);
            }
        }

        return hash.GetHashAndReset();
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendUInt16(IncrementalHash hash, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
