using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Locks cartridge classification and reserved-header bytes to all private corpus identities.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusCarrierTests
{
    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task NineDsiCartridgesMatchBoundaryAndReservedDataDigest()
    {
        using var records = new MemoryStream();
        using var writer = new BinaryWriter(records);
        int count = 0;
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries.OrderBy(static item => item.RomSha256, StringComparer.OrdinalIgnoreCase))
        {
            using NdsImage image = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            if (image.Header.Dsi is null) { continue; }
            NdsCartridgeLayout layout = Assert.IsType<NdsCartridgeLayout>(image.CarrierLayout);
            Assert.Empty(layout.Diagnostics);
            Assert.Equal(0x3000, layout.TwlReservedData.Length);
            Assert.Equal(layout.TwlRegionStart + 0x3000, image.Header.Arm9i!.Data.Offset);
            Assert.True(image.Header.Arm7i!.Data.Offset >= layout.TwlRegionStart + 0x7000);
            Assert.Equal(image.Header.Dsi.NtrDigest.End, image.Header.Dsi.SectorHashTable.Offset);
            Assert.True(image.Header.Dsi.BlockHashTable.End <= image.Header.UsedImageSize);
            byte[] mirror = new byte[0x1000];
            using Stream source = image.OpenRead(new(0x8000, 0x1000));
            await source.ReadExactlyAsync(mirror, TestContext.Current.CancellationToken).ConfigureAwait(true);
            for (int part = 0; part < 3; part++)
            {
                Assert.Equal(mirror, layout.TwlReservedData.Slice(part * 0x1000, 0x1000).ToArray());
            }
            writer.Write(Convert.FromHexString(entry.RomSha256));
            writer.Write(image.Header.UsedImageSize);
            writer.Write(checked((ulong)layout.NtrRegionEnd));
            writer.Write(checked((ulong)layout.TwlRegionStart));
            writer.Write(checked((uint)image.Header.Arm9i.Data.Offset));
            writer.Write(checked((uint)image.Header.Arm9i.Data.Length));
            writer.Write(checked((uint)image.Header.Arm7i.Data.Offset));
            writer.Write(checked((uint)image.Header.Arm7i.Data.Length));
            writer.Write(SHA256.HashData(layout.TwlReservedData.Span));
            count++;
        }
        Assert.Equal(9, count);
        Assert.Equal(900, records.Length);
        CorpusExpectations.AssertDigest("D52345CED83EDC455056CF2E1526360DAF3220EF48C4C2A59C885659394DB097",
            Convert.ToHexString(SHA256.HashData(records.ToArray())));
    }

    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task CartridgeCorpusRetainsItsTwoNonzeroPostHeaderRegions()
    {
        int nonzero = 0;
        int dsi = 0;
        foreach (CorpusExpectationIndexEntry entry in CorpusExpectations.Entries)
        {
            using NdsImage image = await NdsImage.OpenAsync(CorpusExpectations.Resolve(entry),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
            NdsCartridgeLayout layout = Assert.IsType<NdsCartridgeLayout>(image.CarrierLayout);
            Assert.Empty(layout.Diagnostics);
            Assert.Equal(0x3000, layout.PostHeaderData.Length);
            string expected = entry.RomSha256.ToUpperInvariant() switch
            {
                "ADD506595FFAD785E2A426CA826FB2538824E8450BB1AF8F6819260F0739FE6B" => "E8F42815BCD74409F75D72E172B7C0F5FE4D5EE4776612CC12C30DB62A1EA108",
                "671B463B81C0229BFA57E9B9A8DD5FCEF678C1BE615D2D9568FA0CBA5624573B" => "3D826375020A2A0B6CCE4E67C503394D6C3DCAD74D2417FF1EE276B94E589B31",
                _ => "F3CC103136423A57975750907EBC1D367E2985AC6338976D4D5A439F50323F4A",
            };
            Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(layout.PostHeaderData.Span)));
            if (layout.PostHeaderData.Span.ContainsAnyExcept((byte)0)) { nonzero++; }
            if (image.Header.Dsi is not null) { dsi++; }
        }
        Assert.Equal(2, nonzero);
        Assert.Equal(9, dsi);
    }
}
