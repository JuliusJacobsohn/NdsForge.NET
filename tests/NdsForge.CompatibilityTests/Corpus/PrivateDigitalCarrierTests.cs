using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Checks content-addressed digital fixtures without shipping executable bytes or depending on original filenames.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateDigitalCarrierTests
{
    private static readonly string[] Identities =
    [
        "1AB92D6BA7D1CF251D5AAFD44FA39AA08E59B3B3EA72B7A88FA3474C7805021B",
        "40261D490390A8DDC158599258E60181352D92C1055D34E5E0C1D2CA4688EAC7",
        "7FCF94F3B840ED240175A386C0A28C246BD40BCC791E8505C100CA78BA49B6E1",
        "D5FC352703739458F7E74C85127A71F70FB85D9B9F5004F62D138D7041CB62CB",
        "E1CD231B24E236097C30563919DE93CA7C19659B634E8223642764A23252466C",
    ];

    [Fact]
    [Trait("CorpusTier", "Full")]
    public async Task DigitalCarrierMetadataAndPayloadsMatchAndSurviveCopiesAndSemanticBuilds()
    {
        Dictionary<string, string> fixtures = FindFixtures();
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string identity in Identities)
        {
            byte[] bytes = await File.ReadAllBytesAsync(fixtures[identity], TestContext.Current.CancellationToken).ConfigureAwait(true);
            using (NdsImage image = NdsImage.Load(bytes))
            {
                NdsDigitalSrlLayout carrier = Assert.IsType<NdsDigitalSrlLayout>(image.CarrierLayout);
                digest.AppendData(Convert.FromHexString(identity));
                AppendInteger(digest, carrier.TitleId, 8);
                digest.AppendData([image.Header.DeviceCapacityExponent]);
                AppendInteger(digest, image.Header.SecureAreaCrc, 2);
                AppendInteger(digest, image.Header.UsedImageSize, 4);
                AppendInteger(digest, image.Header.Dsi!.TotalImageSize, 4);
                foreach (NdsProgram program in Programs(image))
                {
                    AppendInteger(digest, checked((ulong)program.Data.Offset), 4);
                    AppendInteger(digest, checked((ulong)program.Data.Length), 4);
                    AppendInteger(digest, program.LoadAddress, 4);
                    using Stream stream = image.OpenRead(program.Data);
                    digest.AppendData(await SHA256.HashDataAsync(stream, TestContext.Current.CancellationToken).ConfigureAwait(true));
                }
                Assert.Equal("F3CC103136423A57975750907EBC1D367E2985AC6338976D4D5A439F50323F4A",
                    Convert.ToHexString(SHA256.HashData(carrier.PostHeaderData.Span)));
            }
            await VerifyWritesAsync(bytes).ConfigureAwait(true);
            for (int index = 0; index < 0x3000; index++) { bytes[0x1000 + index] = (byte)((index * 37 + 11) ^ (index >> 5)); }
            Assert.Equal("1C8B1BB730345766B1D6AD9FEFE7B36786D2DA1A452CDF197DE3D5581A697309",
                Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0x1000, 0x3000))));
            foreach (byte capacity in new byte[] { 0, 10 })
            {
                bytes[0x14] = capacity;
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x15E), NdsChecksums.ComputeCrc16(bytes.AsSpan(0, 0x15E)));
                await VerifyWritesAsync(bytes).ConfigureAwait(true);
            }
        }
        CorpusExpectations.AssertDigest("3C53806B79C7C8FE1015468E2F97800A6FEC8AA6DBDA34EF874AC256F6AD4D8E", Convert.ToHexString(digest.GetHashAndReset()));
    }

    private static async Task VerifyWritesAsync(byte[] source)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using NdsImage image = NdsImage.Load(source);
        Assert.Equal(NdsSecureAreaState.Absent, NdsSecureArea.Inspect(image).State);
        using var destination = new MemoryStream();
        await image.Edit().SaveAsync(destination, new() { VerifyOutput = image.Validate().IsValid }, token).ConfigureAwait(false);
        Assert.Equal(source, destination.ToArray());
        NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(image, token).ConfigureAwait(false);
        builder.DsiMetadata!.Integrity = NdsDsiIntegrityOptions.Unauthenticated;
        builder.Title = "DIGITAL TEST";
        builder.FileSystem.AddFile("/carrier-test.bin", [1, 4, 9, 16]);
        NdsImageBuildResult result = await builder.WriteAsync(destination, new() { FileAlignment = 0x200 }, token).ConfigureAwait(false);
        byte[] bytes = destination.ToArray();
        using NdsImage output = NdsImage.Load(bytes);
        Assert.True(output.Validate().IsValid);
        Assert.Equal(NdsImageCarrier.DigitalSrl, output.CarrierLayout.Kind);
        Assert.Equal(image.Header.Dsi!.TitleId, output.Header.Dsi!.TitleId);
        Assert.Equal(image.Header.DeviceCapacityExponent, output.Header.DeviceCapacityExponent);
        Assert.Equal(result.PhysicalSize, output.Header.Dsi.TotalImageSize);
        Assert.Equal(output.Length, output.SizeInfo.DeclaredContentEnd);
        Assert.Null(output.SizeInfo.TrailingData);
        Assert.Equal(0, output.Length % 512);
        Assert.Equal(0, output.Header.Arm9i!.Data.Offset % 1024);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x90)));
        Assert.Equal(NdsChecksums.ComputeCrc16(bytes.AsSpan(0x4000, 0x4000)), output.Header.SecureAreaCrc);
        Assert.Equal(image.CarrierLayout.PostHeaderData.ToArray(), output.CarrierLayout.PostHeaderData.ToArray());
        foreach ((NdsProgram original, NdsProgram rebuilt) in Programs(image).Zip(Programs(output)))
        {
            Assert.Equal(original.LoadAddress, rebuilt.LoadAddress);
            Assert.Equal(original.EntryAddress, rebuilt.EntryAddress);
            Assert.Equal(original.Data.Length, rebuilt.Data.Length);
            using Stream left = image.OpenRead(original.Data);
            using Stream right = output.OpenRead(rebuilt.Data);
            Assert.Equal(await SHA256.HashDataAsync(left, token).ConfigureAwait(false), await SHA256.HashDataAsync(right, token).ConfigureAwait(false));
        }
        foreach (NdsFile file in image.FileSystem.Files)
        {
            Assert.Equal(await file.ReadAllBytesAsync(token).ConfigureAwait(false),
                await output.FileSystem.GetFile(file.FullPath).ReadAllBytesAsync(token).ConfigureAwait(false));
        }
        Assert.All(output.FileSystem.Allocations.Where(static allocation => !allocation.Data.IsEmpty),
            static allocation => Assert.Equal(0, allocation.Data.Offset % 512));
    }

    private static NdsProgram[] Programs(NdsImage image) => [image.Header.Arm9, image.Header.Arm7, image.Header.Arm9i!, image.Header.Arm7i!];

    private static void AppendInteger(IncrementalHash digest, ulong value, int length)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        digest.AppendData(bytes[..length]);
    }

    private static Dictionary<string, string> FindFixtures()
    {
        string? root = Environment.GetEnvironmentVariable("NDSFORGE_DIGITAL_CORPUS");
        if (string.IsNullOrWhiteSpace(root))
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NdsForge.slnx"))) { directory = directory.Parent; }
            root = directory is null ? string.Empty : Path.Combine(directory.FullName, "fixtures", "private", "digital-srl");
        }
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(root))
        {
            foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(path).ToUpperInvariant() is not (".NDS" or ".DSI" or ".SRL" or ".APP")) { continue; }
                using FileStream stream = File.OpenRead(path);
                string hash = Convert.ToHexString(SHA256.HashData(stream));
                if (Identities.Contains(hash, StringComparer.Ordinal)) { result[hash] = path; }
            }
        }
        if (result.Count != Identities.Length)
        {
            const string message = "The five exact digital SRL fixtures are required; set NDSFORGE_DIGITAL_CORPUS to their private directory.";
            if (Environment.GetEnvironmentVariable("NDSFORGE_REQUIRE_CORPUS") == "1") { Assert.Fail(message); }
            Assert.Skip(message);
        }
        return result;
    }
}
