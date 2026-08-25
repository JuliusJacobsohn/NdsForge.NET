using System.Security.Cryptography;

namespace NdsForge.Tests;

public sealed class NdsImageBuilderTests
{
    [Fact]
    public async Task BuildsDeterministicValidatedImageWithProgramsBannerAndNitroFs()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.FileSystem.AddFile("/data/two.bin", [2]);
        builder.FileSystem.AddFile("/data/one.bin", [1, 1]);
        builder.Banner = new NdsBannerBuilder()
            .SetTitle(NdsBannerLanguage.English, "Builder Test")
            .Build();

        byte[] first = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        byte[] second = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(first);

        Assert.Equal(first, second);
        Assert.True(image.Validate().IsValid);
        Assert.Equal("BUILD TEST", image.Header.Title);
        Assert.Equal("BT01", image.Header.GameCode);
        Assert.Equal("HB", image.Header.MakerCode);
        Assert.Equal("Builder Test", image.Banner!.Titles[NdsBannerLanguage.English]);
        Assert.Equal(["/data/one.bin", "/data/two.bin"], image.FileSystem.Files.Select(static file => file.FullPath));
        Assert.Equal(
            [1, 1],
            await image.FileSystem.GetFile(0).ReadAllBytesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            [0xA9, 0x01, 0x02],
            await ReadRegionAsync(image, image.Header.Arm9.Data, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildsExplicitEmptyNitroFsDirectoriesWithoutFatEntries()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.FileSystem.CreateDirectory("/empty/child");

        byte[] data = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(data);

        Assert.Equal(["/", "/empty", "/empty/child"], image.FileSystem.Directories.Select(static value => value.FullPath));
        Assert.Empty(image.FileSystem.Files);
        Assert.True(image.Header.FileAllocationTable.IsEmpty);
    }

    [Fact]
    public async Task BuildsDsiEnhancedImageWithExplicitHomebrewIntegrity()
    {
        byte[] hmacKey = [1, 2, 3, 4];
        NdsImageBuilder builder = CreateBuilder();
        byte[] arm9Data = Enumerable.Range(0, NdsSecureArea.ByteLength + 32)
            .Select(static index => (byte)(index * 17))
            .ToArray();
        builder.Arm9 = new(NdsProcessor.Arm9, arm9Data, 0x02000000, 0x02000000);
        builder.Kind = NdsImageKind.NintendoDsiEnhanced;
        builder.Arm9i = new(NdsProcessor.Arm9i, [0x91, 1, 2, 3, 4], 0x02E00000, 0x02E00000);
        builder.Arm7i = new(NdsProcessor.Arm7i, [0x71, 5, 6], 0x02E80000, 0x02E80000);
        builder.Banner = new NdsBannerBuilder().SetTitle(NdsBannerLanguage.English, "DSi Build").Build();
        var metadata = new NdsDsiBuildMetadata
        {
            DsiFlags = 0xA0,
            RegionFlags = 0x11223340,
            AccessControl = 0x55660000,
            ScfgExtMask = 0x99AABBCC,
            ApplicationFlags = 0,
            EulaVersion = 9,
            AgeRatingsUsage = 0x80,
            Arm7DeviceListAddress = 0x02E81000,
            TitleId = 0x0003000442543031,
            PublicSaveSize = 0x10000,
            PrivateSaveSize = 0x20000,
            Digests = new() { SectorSize = 0x200, BlockSectorCount = 2 },
            Integrity = NdsDsiIntegrityOptions.CreateHmacSha1(
                hmacKey,
                NdsDsiSignatureMode.NoGbaDevelopmentMarker),
        };
        metadata.CryptoPolicy = NdsDsiCryptoPolicy.HasDsiRegion | NdsDsiCryptoPolicy.UsesModcrypt;
        metadata.Regions = NdsDsiRegionPermissions.Japan | NdsDsiRegionPermissions.Europe;
        metadata.AccessControlFlags = NdsDsiAccessCapabilities.SdCard | NdsDsiAccessCapabilities.PhotoRead;
        metadata.ApplicationFeatures = NdsDsiApplicationFeatures.RequiresEula |
            NdsDsiApplicationFeatures.ShowsWirelessIcon;
        metadata.SetMemoryBankSettings(Enumerable.Range(0, 0x30).Select(static index => (byte)index).ToArray());
        metadata.SetSharedDataFileSizes([9, 8, 7, 6, 5, 4]);
        metadata.SetAgeRatings(Enumerable.Repeat((byte)0x80, 16).ToArray());
        metadata.SetAgeRating(new(NdsDsiAgeRatingAuthority.Esrb, 0xEA));
        builder.DsiMetadata = metadata;

        byte[] data = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(data);
        NdsDsiHeader dsi = Assert.IsType<NdsDsiHeader>(image.Header.Dsi);

        Assert.Equal(NdsImageKind.NintendoDsiEnhanced, image.Header.Kind);
        Assert.Equal(0xA3, image.Header.DsiFlags);
        Assert.Equal(0x11223345U, dsi.RegionFlags);
        Assert.Equal(0x55660808U, dsi.AccessControl);
        Assert.Equal(NdsDsiApplicationFeatures.RequiresEula |
            NdsDsiApplicationFeatures.ShowsWirelessIcon, dsi.ApplicationFeatures);
        Assert.Equal(Enumerable.Range(0, 0x30).Select(static index => (byte)index), dsi.MemoryBankSettings.ToArray());
        Assert.Equal([9, 8, 7, 6, 5, 4], dsi.SharedDataFileSizes);
        Assert.Equal(9, dsi.EulaVersion);
        Assert.Equal(0x80, dsi.AgeRatingsUsage);
        Assert.Equal(0xEA, dsi.Ratings[(int)NdsDsiAgeRatingAuthority.Esrb].RawValue);
        Assert.Equal(0x0003000442543031UL, dsi.TitleId);
        Assert.Equal((uint)data.Length, dsi.TotalImageSize);
        Assert.Equal((uint)builder.Banner.RawData.Length, dsi.BannerSize);
        Assert.False(dsi.SectorHashTable.IsEmpty);
        Assert.False(dsi.BlockHashTable.IsEmpty);
        Assert.NotEqual(new byte[20], dsi.DigestMasterHmac.ToArray());
        Assert.True(image.Header.UsedImageSize < image.Header.Arm9i!.Data.Offset);
        Assert.Equal(
            [0x91, 1, 2, 3, 4],
            await ReadRegionAsync(image, image.Header.Arm9i.Data, TestContext.Current.CancellationToken));
#pragma warning disable CA5350 // The test independently verifies the DSi format's mandated HMAC-SHA1 bytes.
        Assert.Equal(HMACSHA1.HashData(hmacKey, new byte[] { 0x91, 1, 2, 3, 4 }), dsi.Arm9iHmac.ToArray());
        Assert.Equal(
            HMACSHA1.HashData(hmacKey, arm9Data.AsSpan(NdsSecureArea.ByteLength)),
            dsi.Arm9WithoutSecureAreaHmac.ToArray());
        byte[] firstSector = await ReadRegionAsync(
            image,
            new(dsi.NtrDigest.Offset, Math.Min(dsi.DigestSectorSize, dsi.NtrDigest.Length)),
            TestContext.Current.CancellationToken);
        byte[] sectorHashes = await ReadRegionAsync(image, dsi.SectorHashTable, TestContext.Current.CancellationToken);
        Assert.Equal(HMACSHA1.HashData(hmacKey, firstSector), sectorHashes[..20]);
        byte[] blockHashes = await ReadRegionAsync(image, dsi.BlockHashTable, TestContext.Current.CancellationToken);
        Assert.Equal(HMACSHA1.HashData(hmacKey, sectorHashes[..40]), blockHashes[..20]);
        Assert.Equal(HMACSHA1.HashData(hmacKey, blockHashes), dsi.DigestMasterHmac.ToArray());
        byte[] expectedMarkerHash = SHA1.HashData(data.AsSpan(0, 0xE00));
#pragma warning restore CA5350
        Assert.Equal(expectedMarkerHash, dsi.RsaSignature.Slice(0x6C, 20).ToArray());
        Assert.Equal(0, dsi.RsaSignature.Span[0]);
        Assert.Equal(1, dsi.RsaSignature.Span[1]);
        Assert.True(image.Validate().IsValid);
        Assert.True(image.Validate(new NdsValidationOptions().SetDsiHmacKey(hmacKey)).IsValid);

        byte[] secureAreaTamper = data.ToArray();
        secureAreaTamper[checked((int)image.Header.Arm9.Data.Offset + 0x100)] ^= 0xFF;
        using (NdsImage tamperedSecureArea = NdsImage.Load(secureAreaTamper))
        {
            NdsValidationResult validation = tamperedSecureArea.Validate(
                new NdsValidationOptions().SetDsiHmacKey(hmacKey));
            Assert.Contains(validation.Diagnostics, static diagnostic => diagnostic.Code == "NDS1310");
            Assert.DoesNotContain(validation.Diagnostics, static diagnostic => diagnostic.Code == "NDS1322");
        }

        byte[] nonSecureAreaTamper = data.ToArray();
        nonSecureAreaTamper[checked((int)image.Header.Arm9.Data.Offset + NdsSecureArea.ByteLength)] ^= 0xFF;
        using (NdsImage tamperedNonSecureArea = NdsImage.Load(nonSecureAreaTamper))
        {
            NdsValidationResult validation = tamperedNonSecureArea.Validate(
                new NdsValidationOptions().SetDsiHmacKey(hmacKey));
            Assert.Contains(validation.Diagnostics, static diagnostic => diagnostic.Code == "NDS1310");
            Assert.Contains(validation.Diagnostics, static diagnostic => diagnostic.Code == "NDS1322");
        }

        byte[] tamperedData = data.ToArray();
        tamperedData[checked((int)image.Header.Arm9i.Data.Offset)] ^= 0xFF;
        using (NdsImage tampered = NdsImage.Load(tamperedData))
        {
            NdsValidationResult tamperedValidation = tampered.Validate(
                new NdsValidationOptions().SetDsiHmacKey(hmacKey));
            Assert.Contains(tamperedValidation.Diagnostics, static diagnostic => diagnostic.Code == "NDS1313");
            Assert.Contains(tamperedValidation.Diagnostics, static diagnostic => diagnostic.Code == "NDS1317");
        }

        byte[] tamperedTableData = data.ToArray();
        tamperedTableData[checked((int)dsi.SectorHashTable.Offset)] ^= 0xFF;
        using (NdsImage tamperedTable = NdsImage.Load(tamperedTableData))
        {
            NdsValidationResult tableValidation = tamperedTable.Validate(
                new NdsValidationOptions().SetDsiHmacKey(hmacKey));
            Assert.Contains(tableValidation.Diagnostics, static diagnostic => diagnostic.Code == "NDS1318");
        }

        NdsImageBuilder imported = await NdsImageBuilder.FromImageAsync(image, TestContext.Current.CancellationToken);
        imported.FileSystem.AddFile("/added.bin", [9, 8, 7]);
        byte[] rebuiltData = await imported.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage rebuilt = NdsImage.Load(rebuiltData);

        Assert.Equal(NdsImageKind.NintendoDsiEnhanced, rebuilt.Header.Kind);
        Assert.Equal([0x91, 1, 2, 3, 4], await ReadRegionAsync(
            rebuilt,
            rebuilt.Header.Arm9i!.Data,
            TestContext.Current.CancellationToken));
        Assert.Equal(0x0003000442543031UL, rebuilt.Header.Dsi!.TitleId);
        Assert.Equal(0xA3, rebuilt.Header.DsiFlags);
        Assert.Equal(0x11223345U, rebuilt.Header.Dsi.RegionFlags);
        Assert.Equal(0x55660808U, rebuilt.Header.Dsi.AccessControl);
        Assert.Equal([9, 8, 7, 6, 5, 4], rebuilt.Header.Dsi.SharedDataFileSizes);
        Assert.Equal(0xEA, rebuilt.Header.Dsi.Ratings[(int)NdsDsiAgeRatingAuthority.Esrb].RawValue);
        Assert.Equal(new byte[20], rebuilt.Header.Dsi.Arm9iHmac.ToArray());
        Assert.Equal([9, 8, 7], await rebuilt.FileSystem.GetFile("/added.bin")
            .ReadAllBytesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildsImportsAndVerifiesOptionalDebugProgram()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.DebugProgram = new([0xDE, 0xB6, 0x01, 0x02, 0x03], 0x027F_0000);

        byte[] first = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        NdsImageBuilder imported;
        using (NdsImage image = NdsImage.Load(first))
        {
            Assert.Equal((uint)5, image.Header.DebugRomSize);
            Assert.Equal(0x027F_0000U, image.Header.DebugLoadAddress);
            Assert.Equal([0xDE, 0xB6, 0x01, 0x02, 0x03], await ReadRegionAsync(
                image,
                image.Header.DebugRom,
                TestContext.Current.CancellationToken));
            imported = await NdsImageBuilder.FromImageAsync(image, TestContext.Current.CancellationToken);
        }

        byte[] rebuilt = await imported.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage reparsed = NdsImage.Load(rebuilt);
        NdsImageManifest manifest = await reparsed.CreateManifestAsync(TestContext.Current.CancellationToken);
        Assert.Equal((uint)5, reparsed.Header.DebugRomSize);
        Assert.Equal(0x027F_0000U, reparsed.Header.DebugLoadAddress);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[] { 0xDE, 0xB6, 0x01, 0x02, 0x03 })),
            manifest.Header.DebugRomSha256);
        Assert.Equal([0xDE, 0xB6, 0x01, 0x02, 0x03], await ReadRegionAsync(
            reparsed,
            reparsed.Header.DebugRom,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReportsConcreteLayoutAndLeavesDestinationOpen()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.FileSystem.AddFile("asset.bin", [4, 5, 6]);
        using var destination = new MemoryStream();

        NdsImageBuildResult result = await builder.WriteAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(destination.CanRead);
        Assert.Equal(destination.Length, result.PhysicalSize);
        Assert.Equal(1, result.FileCount);
        Assert.Equal(1, result.AllocationCount);
        Assert.Equal(0x4000, result.Arm9.Offset);
        Assert.True(result.FileAllocationTable.Offset > result.FileNameTable.Offset);
    }

    [Fact]
    public async Task BuildsOverlayTablesWithFileIdsIndependentFromOverlayIds()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.FileSystem.AddFile("named.bin", [1]);
        builder.AddOverlay(new(
            NdsProcessor.Arm9,
            id: 77,
            contents: [7, 7, 7],
            loadAddress: 0x02001000,
            ramSize: 3));
        builder.AddOverlay(new(
            NdsProcessor.Arm7,
            id: 12,
            contents: [8, 8],
            loadAddress: 0x02390000,
            ramSize: 2,
            bssSize: 4));

        byte[] data = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(data);

        Assert.Equal((uint)77, image.Arm9Overlays[0].Id);
        Assert.Equal((uint)1, image.Arm9Overlays[0].FileId);
        Assert.Null(image.Arm9Overlays[0].File);
        Assert.Equal((uint)12, image.Arm7Overlays[0].Id);
        Assert.Equal((uint)2, image.Arm7Overlays[0].FileId);
        Assert.Equal((uint)4, image.Arm7Overlays[0].BssSize);
        Assert.Equal(
            [7, 7, 7],
            await ReadRegionAsync(image, image.Arm9Overlays[0].Data!.Value, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NdstoolProfilePlacesHiddenOverlaysBeforeNamedFiles()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.Version = 0;
        builder.FileSystem.AddFile("/named.bin", [1, 2]);
        builder.AddOverlay(new(
            NdsProcessor.Arm9,
            id: 77,
            contents: [7, 7, 7],
            loadAddress: 0x02001000,
            ramSize: 3));
        builder.AddOverlay(new(
            NdsProcessor.Arm7,
            id: 12,
            contents: [8, 8],
            loadAddress: 0x02390000,
            ramSize: 2));

        byte[] data = await builder.BuildAsync(
            new() { Profile = NdsImageBuildProfile.Ndstool1503 },
            TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(data);

        Assert.Equal((uint)0, image.Arm9Overlays[0].FileId);
        Assert.Equal((uint)1, image.Arm7Overlays[0].FileId);
        Assert.Equal(2, image.FileSystem.GetFile("/named.bin").Id);
        Assert.Equal(image.Header.Arm9.Data.Offset + 0x800 + 3 + 12, image.Header.Arm9OverlayTable.Offset);
        Assert.True(image.Validate().IsValid);
    }

    [Fact]
    public async Task NdstoolProfileRejectsRelationshipsItsCliCannotRepresent()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.FileSystem.AddFile("/shared.bin", [1]);
        builder.AddOverlay(NdsOverlayDefinition.LinkToFile(
            NdsProcessor.Arm9,
            id: 1,
            filePath: "/shared.bin",
            loadAddress: 0x02002000,
            ramSize: 1));
        using var destination = new MemoryStream([9, 8, 7], writable: true);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await builder.WriteAsync(
                destination,
                new() { Profile = NdsImageBuildProfile.Ndstool1503 },
                TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], destination.ToArray());
    }

    [Fact]
    public async Task OverlayCanShareNamedNitroFsAllocationWithoutDuplication()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.FileSystem.AddFile("/overlays/shared.bin", [9, 8, 7]);
        builder.AddOverlay(NdsOverlayDefinition.LinkToFile(
            NdsProcessor.Arm9,
            id: 42,
            filePath: "/overlays/shared.bin",
            loadAddress: 0x02002000,
            ramSize: 3));
        using var destination = new MemoryStream();

        NdsImageBuildResult result = await builder.WriteAsync(
            destination,
            cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(destination.ToArray());

        Assert.Equal(1, result.FileCount);
        Assert.Equal(1, result.AllocationCount);
        Assert.Equal((uint)0, image.Arm9Overlays[0].FileId);
        Assert.Same(image.FileSystem.GetFile("/overlays/shared.bin"), image.Arm9Overlays[0].File);
    }

    [Fact]
    public async Task MissingLinkedOverlayFileFailsBeforeDestinationMutation()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.AddOverlay(NdsOverlayDefinition.LinkToFile(
            NdsProcessor.Arm9,
            id: 1,
            filePath: "/missing.bin",
            loadAddress: 0x02002000,
            ramSize: 1));
        using var destination = new MemoryStream([1, 2, 3], writable: true);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await builder.WriteAsync(destination, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([1, 2, 3], destination.ToArray());
    }

    [Fact]
    public async Task PreservesArm9SdkFooterOutsideDeclaredProgramLength()
    {
        NdsImageBuilder builder = CreateBuilder();
        builder.Arm9!.SetFooter([
            0x21, 0x06, 0xC0, 0xDE,
            1, 2, 3, 4,
            5, 6, 7, 8,
        ]);

        byte[] data = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage image = NdsImage.Load(data);

        Assert.Equal(3, image.Header.Arm9.Data.Length);
        Assert.Equal(new NdsRegion(image.Header.Arm9.Data.End, 12), image.Header.Arm9.Footer);
        Assert.Equal(15, image.Header.Arm9.CompleteData.Length);
        Assert.Equal(
            [0xA9, 0x01, 0x02, 0x21, 0x06, 0xC0, 0xDE, 1, 2, 3, 4, 5, 6, 7, 8],
            await ReadRegionAsync(image, image.Header.Arm9.CompleteData, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportsDetachedRecipeForStructuralRebuild()
    {
        NdsImageBuilder original = CreateBuilder();
        original.NormalCardControl = 0x11223344;
        original.SecureTransferTimeout = 0x5566;
        original.FileSystem.CreateDirectory("/empty");
        original.FileSystem.AddFile("/old/shared.bin", [3, 2, 1]);
        original.AddOverlay(NdsOverlayDefinition.LinkToFile(
            NdsProcessor.Arm9,
            id: 9,
            filePath: "/old/shared.bin",
            loadAddress: 0x02003000,
            ramSize: 3));
        byte[] sourceBytes = await original.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        NdsImageBuilder imported;
        using (NdsImage source = NdsImage.Load(sourceBytes))
        {
            imported = await NdsImageBuilder.FromImageAsync(source, TestContext.Current.CancellationToken);
        }

        imported.FileSystem.MoveDirectory("/old", "/new");
        byte[] rebuiltBytes = await imported.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        using NdsImage rebuilt = NdsImage.Load(rebuiltBytes);

        Assert.Equal(0x11223344U, rebuilt.Header.NormalCardControl);
        Assert.Equal((ushort)0x5566, rebuilt.Header.SecureTransferTimeout);
        Assert.Equal(["/", "/empty", "/new"], rebuilt.FileSystem.Directories.Select(static directory => directory.FullPath));
        Assert.Equal("/new/shared.bin", rebuilt.Arm9Overlays[0].File!.FullPath);
    }

    [Fact]
    public async Task RejectsMissingOrMismatchedProgramsBeforeTruncatingDestination()
    {
        var builder = new NdsImageBuilder
        {
            Arm9 = new(NdsProcessor.Arm7, [1], 0x02000000, 0x02000000),
            Arm7 = new(NdsProcessor.Arm7, [2], 0x02380000, 0x02380000),
        };
        using var destination = new MemoryStream([9, 8, 7], writable: true);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await builder.WriteAsync(destination, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([9, 8, 7], destination.ToArray());
    }

    [Fact]
    public async Task PathBuildRequiresExplicitOverwriteAndCommitsValidImage()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NdsForgeTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "built.nds");
        try
        {
            NdsImageBuilder builder = CreateBuilder();
            await builder.WriteAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<IOException>(async () =>
                await builder.WriteAsync(path, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true));

            await builder.WriteAsync(
                path,
                new() { OverwriteDestination = true },
                TestContext.Current.CancellationToken);
            using NdsImage image = await NdsImage.OpenAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(image.Validate().IsValid);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static NdsImageBuilder CreateBuilder() => new()
    {
        Title = "BUILD TEST",
        GameCode = "BT01",
        MakerCode = "HB",
        Version = 2,
        Arm9 = new(NdsProcessor.Arm9, [0xA9, 0x01, 0x02], 0x02000000, 0x02000000),
        Arm7 = new(NdsProcessor.Arm7, [0xA7, 0x03], 0x02380000, 0x02380000),
    };

    private static async ValueTask<byte[]> ReadRegionAsync(
        NdsImage image,
        NdsRegion region,
        CancellationToken cancellationToken)
    {
        using Stream stream = image.OpenRead(region);
        byte[] data = new byte[region.Length];
        await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(true);
        return data;
    }
}
