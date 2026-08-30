using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NdsForge.Tests;

public sealed class NdsDsIntegrityValidationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifiesEveryFieldWithExplicitKeysAndEitherSecureAreaStorage(bool encrypted)
    {
        Fixture fixture = await CreateAsync(encrypted).ConfigureAwait(true);
        using NdsImage image = NdsImage.Load(fixture.Data);
        NdsValidationResult result = image.Validate(Options(fixture));
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(static item => item.Message)));
        Assert.Empty(LateFindings(result));
    }

    [Fact]
    public async Task KeylessStructuralChecksRemainSeparateFromRequestedAuthentication()
    {
        Fixture fixture = await CreateAsync().ConfigureAwait(true);
        using NdsImage image = NdsImage.Load(fixture.Data);
        Assert.Empty(LateFindings(image.Validate()));
        string[] missing = LateFindings(image.Validate(new() { ValidateDsAuthentication = true }))
            .Select(static item => item.Code).ToArray();
        Assert.Equal(["NDS1501", "NDS1511", "NDS1530"], missing);
        NdsValidationResult noSecureKey = image.Validate(new NdsValidationOptions().SetDsProgramHmacKey(ProgramKey()));
        Assert.Contains(noSecureKey.Diagnostics, static item => item.Code == "NDS1510" && item.Message.Contains("KEY1", StringComparison.Ordinal));
        Assert.DoesNotContain(noSecureKey.Diagnostics, static item => item.Code is "NDS1512" or "NDS1522");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongHmacKeysDoNotSubstituteForOtherCredentials(bool wrongBanner)
    {
        Fixture fixture = await CreateAsync().ConfigureAwait(true);
        using NdsImage image = NdsImage.Load(fixture.Data);
        NdsValidationOptions options = Options(fixture);
        if (wrongBanner)
        {
            options.SetDsBannerHmacKey([4, 5, 6]);
        }
        else
        {
            options.SetDsProgramHmacKey([4, 5, 6]);
        }

        string[] errors = LateFindings(image.Validate(options)).Select(static item => item.Code).ToArray();
        string[] expected = wrongBanner ? ["NDS1502"] : ["NDS1512", "NDS1522"];
        Assert.Equal(expected, errors);
    }

    [Theory]
    [InlineData("arm9", "NDS1512")]
    [InlineData("arm7", "NDS1512")]
    [InlineData("overlay", "NDS1522")]
    [InlineData("banner", "NDS1502")]
    [InlineData("signature", "NDS1531")]
    public async Task DetectsChangedCoveredBytes(string component, string code)
    {
        Fixture fixture = await CreateAsync().ConfigureAwait(true);
        using NdsImage original = NdsImage.Load(fixture.Data);
        long offset = component switch
        {
            "arm9" => original.Header.Arm9.Data.Offset + 0x4010,
            "arm7" => original.Header.Arm7.Data.Offset,
            "overlay" => original.Arm9Overlays[0].Data!.Value.Offset,
            "banner" => original.Header.BannerOffset + 0x40,
            _ => 0xF80,
        };
        byte[] edited = (byte[])fixture.Data.Clone();
        edited[checked((int)offset)] ^= 0x55;
        using NdsImage image = NdsImage.Load(edited);
        Assert.Contains(image.Validate(Options(fixture)).Diagnostics,
            item => item.Code == code && item.Severity == NdsDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedProgramLayoutsAreExplicitlyUnverified(bool shortProgram)
    {
        Fixture fixture = await CreateAsync().ConfigureAwait(true);
        Write(fixture.Data, shortProgram ? 0x2C : 0x20, shortProgram ? 0x3000u : 0x4004u);
        using NdsImage image = NdsImage.Load(fixture.Data);
        NdsValidationResult result = image.Validate(Options(fixture));
        Assert.Contains(result.Diagnostics, static item => item.Code == "NDS1510" && item.Severity == NdsDiagnosticSeverity.Warning);
        Assert.DoesNotContain(result.Diagnostics, static item => item.Code == "NDS1512");
    }

    [Fact]
    public async Task WrongSecureAreaKeyDoesNotTreatUnrecognizedCiphertextAsVerified()
    {
        Fixture fixture = await CreateAsync(encrypted: true).ConfigureAwait(true);
        NdsValidationOptions options = Options(fixture).SetSecureAreaKeyTable(new(new byte[NdsKey1KeyTable.ByteLength]));
        using NdsImage image = NdsImage.Load(fixture.Data);
        Assert.Contains(image.Validate(options).Diagnostics, static item => item.Code == "NDS1510");
    }

    [Fact]
    public async Task MissingBannerAndUnexpectedAggregateFieldAreDiagnosed()
    {
        Fixture fixture = await CreateAsync().ConfigureAwait(true);
        Write(fixture.Data, 0x68, 0);
        Write(fixture.Data, 0x54, 0);
        using NdsImage image = NdsImage.Load(fixture.Data);
        NdsValidationResult result = image.Validate(Options(fixture));
        Assert.Contains(result.Diagnostics, static item => item.Code == "NDS1500");
        Assert.Contains(result.Diagnostics, static item => item.Code == "NDS1523");
    }

    [Fact]
    public async Task MissingRoundedPaddingIsNotAnAggregateDigestMismatch()
    {
        Fixture fixture = await CreateAsync().ConfigureAwait(true);
        using NdsImage original = NdsImage.Load(fixture.Data);
        int end = checked((int)original.Arm9Overlays[0].Data!.Value.End);
        using NdsImage image = NdsImage.Load(fixture.Data.AsMemory(0, end));
        NdsValidationResult result = image.Validate(Options(fixture));
        Assert.Contains(result.Diagnostics, static item => item.Code == "NDS1520");
        Assert.DoesNotContain(result.Diagnostics, static item => item.Code == "NDS1522");
    }

    [Fact]
    public async Task NonPrefixOverlaySelectionsAreExplicitlyUnsupported()
    {
        Fixture fixture = await CreateAsync().ConfigureAwait(true);
        using NdsImage original = NdsImage.Load(fixture.Data);
        Write(fixture.Data, checked((int)original.Header.Arm9OverlayTable.Offset + 0x18), 1);
        using NdsImage image = NdsImage.Load(fixture.Data);
        NdsValidationResult result = image.Validate(Options(fixture));
        Assert.Contains(result.Diagnostics, static item => item.Code == "NDS1520");
        Assert.DoesNotContain(result.Diagnostics, static item => item.Code == "NDS1522");
    }

    [Fact]
    public async Task RejectsOutOfBoundsProgramBeforeAttemptingAuthenticationReads()
    {
        Fixture fixture = await CreateAsync().ConfigureAwait(true);
        Write(fixture.Data, 0x3C, uint.MaxValue);
        using NdsImage image = NdsImage.Load(fixture.Data);
        Assert.Contains(image.Validate(Options(fixture)).Diagnostics, static item => item.Code == "NDS1514");
    }

    [Fact]
    public async Task BannerOnlyDeclarationDoesNotInventProgramOrSignatureRequirements()
    {
        Fixture fixture = await CreateAsync().ConfigureAwait(true);
        fixture.Data[0x1BF] = 0x20;
        using NdsImage image = NdsImage.Load(fixture.Data);
        Assert.Empty(LateFindings(image.Validate(new NdsValidationOptions().SetDsBannerHmacKey(BannerKey()))));
    }

    [Fact]
    public async Task OptionsCopyCallerHmacBuffersAndRejectMissingCredentials()
    {
        Fixture fixture = await CreateAsync().ConfigureAwait(true);
        byte[] programKey = ProgramKey();
        byte[] bannerKey = BannerKey();
        NdsValidationOptions options = Options(fixture).SetDsProgramHmacKey(programKey).SetDsBannerHmacKey(bannerKey);
        Array.Clear(programKey);
        Array.Clear(bannerKey);
        using NdsImage image = NdsImage.Load(fixture.Data);
        Assert.Empty(LateFindings(image.Validate(options)));
        Assert.Throws<ArgumentException>(() => options.SetDsProgramHmacKey([]));
        Assert.Throws<ArgumentException>(() => options.SetDsBannerHmacKey([]));
        Assert.Throws<ArgumentNullException>(() => options.SetDsRsaPublicKey(null!));
    }

    private static IEnumerable<NdsDiagnostic> LateFindings(NdsValidationResult result) =>
        result.Diagnostics.Where(static item => item.Code.StartsWith("NDS15", StringComparison.Ordinal));

    private static NdsValidationOptions Options(Fixture fixture) => new NdsValidationOptions()
        .SetDsProgramHmacKey(ProgramKey()).SetDsBannerHmacKey(BannerKey())
        .SetSecureAreaKeyTable(fixture.SecureKey).SetDsRsaPublicKey(fixture.PublicKey);

    private static byte[] ProgramKey() => Enumerable.Range(0, 64).Select(static index => (byte)index).ToArray();

    private static byte[] BannerKey() => Enumerable.Range(0, 64).Select(static index => (byte)(255 - index)).ToArray();

    private static async Task<Fixture> CreateAsync(bool encrypted = false)
    {
        byte[] arm9 = Enumerable.Range(0, 0x4800).Select(static index => (byte)((index * 29) ^ (index >> 3))).ToArray();
        Write(arm9, 0, 0xE7FFDEFF);
        Write(arm9, 4, 0xE7FFDEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(arm9.AsSpan(0x0E), NdsChecksums.ComputeCrc16(arm9.AsSpan(0x10, 0x7F0)));
        var secureKey = new NdsKey1KeyTable(Enumerable.Range(0, NdsKey1KeyTable.ByteLength)
            .Select(static index => (byte)((index * 37) + 11)).ToArray());
        byte[] encryptedArea = NdsSecureArea.Encrypt(arm9.AsSpan(0, 0x4000), "TEST", secureKey);
        byte[] authenticationArm9 = (byte[])arm9.Clone();
        encryptedArea.CopyTo(authenticationArm9, 0);
        byte[] arm7 = Enumerable.Range(0, 0x300).Select(static index => (byte)(index * 31)).ToArray();
        var builder = new NdsImageBuilder
        {
            GameCode = "TEST",
            Arm9 = new(NdsProcessor.Arm9, encrypted ? authenticationArm9 : arm9, 0x02000000, 0x02000800),
            Arm7 = new(NdsProcessor.Arm7, arm7, 0x03800000, 0x03800000),
            Banner = new NdsBannerBuilder().SetTitle(NdsBannerLanguage.English, "Authentication test").Build(),
        };
        builder.AddOverlay(new(NdsProcessor.Arm9, 0, new byte[513], 0x02100000, 513));
        byte[] data = await builder.BuildAsync(new() { FileAlignment = 512 }, TestContext.Current.CancellationToken).ConfigureAwait(true);
        data[0x1BF] = 0x60;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x6C), NdsChecksums.ComputeCrc16(encryptedArea));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x15E), NdsChecksums.ComputeCrc16(data.AsSpan(0, 0x15E)));
        using (NdsImage image = NdsImage.Load(data))
        {
            NdsDsAuthentication.ComputeProgramsHmac(data.AsSpan(0, 0x160), authenticationArm9, arm7, ProgramKey()).CopyTo(data, 0x378);
            NdsDsAuthentication.ComputeOverlayHmac(image, ProgramKey()).CopyTo(data, 0x38C);
            NdsDsAuthentication.ComputeBannerHmac(image.Banner!, BannerKey()).CopyTo(data, 0x33C);
        }

        using RSA rsa = RSA.Create(1024);
        using var signer = new NdsDsiRsaSignatureProvider(rsa);
        signer.SignHeader(data.AsSpan(0, 0xE00), data.AsSpan(0xF80, 128));
        return new(data, secureKey, NdsDsiRsaPublicKey.FromRsa(rsa));
    }

    private static void Write(Span<byte> data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);

    private sealed record Fixture(byte[] Data, NdsKey1KeyTable SecureKey, NdsDsiRsaPublicKey PublicKey);
}
