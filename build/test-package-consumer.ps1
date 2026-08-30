param(
    [string]$PackageDirectory = "artifacts/packages",
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($Version)) {
    $output = & dotnet msbuild (Join-Path $repository "src/NdsForge/NdsForge.csproj") -nologo -getProperty:PackageVersion
    if ($LASTEXITCODE -ne 0) { throw "Package version resolution failed." }
    $Version = ($output | Select-Object -Last 1).Trim()
}
if ([string]::IsNullOrWhiteSpace($Version)) { throw "Package version is empty." }

$resolvedPackages = (Resolve-Path -LiteralPath $PackageDirectory).Path
$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("NdsForge-consumer-" + [guid]::NewGuid().ToString("N"))
$consumer = Join-Path $workspace "consumer"
$toolPath = Join-Path $workspace "tools"
$consumerPackages = Join-Path $workspace ".nuget/packages"
New-Item -ItemType Directory -Path $workspace | Out-Null

try {
    dotnet new console --framework net10.0 --output $consumer --force --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Clean consumer creation failed." }
    dotnet add $consumer package NdsForge --version $Version --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Adding NdsForge $Version failed." }
    dotnet add $consumer package NdsForge.Nitro --version $Version --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Adding NdsForge.Nitro $Version failed." }
    dotnet add $consumer package NdsForge.Graphics --version $Version --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Adding NdsForge.Graphics $Version failed." }

    $escapedPackages = [System.Security.SecurityElement]::Escape($resolvedPackages)
    $config = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="NdsForge local packages" value="$escapedPackages" />
  </packageSources>
</configuration>
"@
    $configPath = Join-Path $workspace "NuGet.Config"
    [System.IO.File]::WriteAllText($configPath, $config, [System.Text.UTF8Encoding]::new($false))

    dotnet restore $consumer --packages $consumerPackages --configfile $configPath --no-cache
    if ($LASTEXITCODE -ne 0) { throw "Isolated NdsForge package restore failed." }

    $program = @'
using NdsForge;
using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Fonts;
using NdsForge.Graphics.Maps;
using NdsForge.Graphics.Palettes;
using NdsForge.Graphics.Tiles;
using NdsForge.Graphics.Sprites;
using NdsForge.Nitro.Compression;
using System.Security.Cryptography;

var builder = new NdsImageBuilder
{
    Title = "CONSUMER",
    GameCode = "CS01",
    MakerCode = "HB",
    Arm9 = new(NdsProcessor.Arm9, [0xA9], 0x02000000, 0x02000000),
    Arm7 = new(NdsProcessor.Arm7, [0xA7], 0x02380000, 0x02380000),
};
builder.FileSystem.AddFile("/hello.txt", "hello"u8);
byte[] bytes = await builder.BuildAsync();
using NdsImage image = NdsImage.Load(bytes);
if (image.Header.GameCode != "CS01" || image.FileSystem.GetFile("/hello.txt").Data.Length != 5)
    throw new InvalidOperationException("Package consumer round trip failed.");
byte[] authenticationKey = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
if (NdsDsAuthentication.GetOverlayHashRegions(image).Count != 0 ||
    Convert.ToHexString(NdsDsAuthentication.ComputeOverlayHmac(image, authenticationKey)) != "60BF8C95C85CFA61279A2B9B079AA19D7FA5F31A")
    throw new InvalidOperationException("Late-DS aggregate package consumer failed.");
byte[] programDigest = NdsDsAuthentication.ComputeProgramsHmac(bytes.AsSpan(0, 0x160), [0xA9], [0xA7], authenticationKey);
NdsBanner authenticationBanner = new NdsBannerBuilder().Build();
if (programDigest.Length != 20 || NdsDsAuthentication.ComputeBannerHmac(authenticationBanner, authenticationKey).Length != 20)
    throw new InvalidOperationException("Late-DS component package consumer failed.");
using RSA rsa = RSA.Create(1024);
using var signer = new NdsDsiRsaSignatureProvider(rsa);
NdsDsiRsaPublicKey publicKey = NdsDsiRsaPublicKey.FromRsa(rsa);
byte[] signature = new byte[128];
signer.SignHeader(bytes.AsSpan(0, 0xE00), signature);
if (!publicKey.VerifyHeader(bytes.AsSpan(0, 0xE00), signature))
    throw new InvalidOperationException("Native RSA package consumer failed.");
_ = image.Validate(new NdsValidationOptions { ValidateDsAuthentication = true }
    .SetDsProgramHmacKey(authenticationKey).SetDsBannerHmacKey(authenticationKey).SetDsRsaPublicKey(publicKey));
var bannerPolicy = NdsDsIntegrityOptions.CreateHmacSha1([], authenticationKey);
builder.Banner = authenticationBanner;
builder.DsMetadata = new NdsDsBuildMetadata
{
    ProgramFeatures = NdsProgramFeatures.AuthenticatesBanner,
    Integrity = bannerPolicy,
};
using var authenticatedStream = new MemoryStream();
NdsImageBuildResult authenticatedBuild = await builder.WriteAsync(authenticatedStream);
using NdsImage authenticatedImage = NdsImage.Load(authenticatedStream.ToArray());
if (authenticatedBuild.Diagnostics.Count != 0 || !authenticatedImage.Validate(new NdsValidationOptions().SetDsBannerHmacKey(authenticationKey)).IsValid)
    throw new InvalidOperationException("Late-DS build package consumer failed.");
using var editedStream = new MemoryStream();
NdsSaveResult authenticatedEdit = await authenticatedImage.Edit().ReplaceBanner(new NdsBannerBuilder().SetTitle(NdsBannerLanguage.English, "Changed").Build())
    .SaveAsync(editedStream, new NdsWriteOptions { DsIntegrity = bannerPolicy });
using NdsImage editedImage = NdsImage.Load(editedStream.ToArray());
if (authenticatedEdit.Diagnostics.Count != 0 || !editedImage.Validate(new NdsValidationOptions().SetDsBannerHmacKey(authenticationKey)).IsValid)
    throw new InvalidOperationException("Late-DS editor package consumer failed.");
byte[] storedTrailer = new byte[NdsDownloadPlaySignature.ByteLength];
new byte[] { 0x61, 0x63, 1, 0 }.CopyTo(storedTrailer, 0);
builder.DownloadPlaySignature = NdsDownloadPlaySignature.Parse(storedTrailer);
using var trailerStream = new MemoryStream();
NdsImageBuildResult trailerResult = await builder.WriteAsync(trailerStream);
using NdsImage trailerImage = NdsImage.Load(trailerStream.ToArray());
if (!trailerImage.DownloadPlaySignature!.RawData.Span.SequenceEqual(storedTrailer) ||
    trailerImage.DownloadPlaySignatureRegion!.Value.Offset != trailerResult.UsedSize ||
    !trailerResult.Diagnostics.Any(diagnostic => diagnostic.Code == "NDS1550"))
    throw new InvalidOperationException("Download Play trailer package consumer failed.");
byte[] plain = Enumerable.Repeat((byte)0x41, 512).ToArray();
if (!BlzCodec.TryCompress(plain, out byte[] compressed) || !BlzCodec.Decompress(compressed).AsSpan().SequenceEqual(plain))
    throw new InvalidOperationException("Nitro package consumer round trip failed.");
NclrPalette palette = NclrPalette.Create(NitroColorDepth.Indexed4Bpp, [new NitroColor555(0), new NitroColor555(0x7FFF)]);
if (NclrPalette.Parse(palette.CreateBuilder().Build()).Colors[1].ToRgba32() != new RgbaColor32(255, 255, 255))
    throw new InvalidOperationException("Graphics package consumer round trip failed.");
NcgrCharacterGraphics tiles = NcgrCharacterGraphics.Create(8, 8, NitroColorDepth.Indexed4Bpp, new byte[64]);
NscrScreenMap map = NscrScreenMap.Create(8, 8, NitroPaletteSelection.SixteenBySixteen, NitroBackgroundKind.Text, [new NscrMapEntry()]);
if (map.Render(tiles, palette).Pixels.Count != 64)
    throw new InvalidOperationException("Graphics composition package consumer round trip failed.");
if (NitroObjectEntry.Create(0, 0, 8, 8, 0, NitroColorDepth.Indexed4Bpp).Width != 8)
    throw new InvalidOperationException("Graphics OAM package consumer round trip failed.");
if (new NftrGlyphMetrics(-1, 8, 9).AdvanceWidth != 9)
    throw new InvalidOperationException("Graphics font package consumer API failed.");
Console.WriteLine("PACKAGE_CONSUMER_OK");
'@
    [System.IO.File]::WriteAllText((Join-Path $consumer "Program.cs"), $program, [System.Text.UTF8Encoding]::new($false))
    dotnet run --project $consumer --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Clean NdsForge package consumer failed." }

    dotnet tool install NdsForge.Cli --version $Version --tool-path $toolPath `
        --configfile $configPath --no-cache
    if ($LASTEXITCODE -ne 0) { throw "Installing the local NdsForge CLI package failed." }
    $executableName = if ($IsWindows) { "ndsforge.exe" } else { "ndsforge" }
    $executable = Join-Path $toolPath $executableName
    & $executable --help
    if ($LASTEXITCODE -ne 0) { throw "The packaged ndsforge tool did not start successfully." }
    Write-Output "TOOL_CONSUMER_OK"
} finally {
    if ([System.IO.Directory]::Exists($workspace)) { [System.IO.Directory]::Delete($workspace, $true) }
}
