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
if (image.SizeInfo.PhysicalSize != bytes.Length || image.SizeInfo.DeviceCapacityBytes != 131072 ||
    image.SizeInfo.DeclaredContentEnd > bytes.Length || image.SizeInfo.Diagnostics.Count != 0)
    throw new InvalidOperationException("Size inspection package consumer failed.");
using (var resized = new MemoryStream())
{
    NdsImageResizeResult resize = await NdsImageResizer.WriteAsync(image, resized,
        new() { Mode = NdsImageResizeMode.Trim, TrailingDataPolicy = NdsTrailingDataPolicy.RequirePadding });
    if (resize.OutputLength != image.SizeInfo.DeclaredContentEnd || resize.AddedData is not null)
        throw new InvalidOperationException("Resize policy package consumer failed.");
}
byte[] capacityBytes = await builder.BuildAsync(new()
{
    RequestedDeviceCapacityBytes = 0x40000,
    PadToDeviceCapacity = true,
    PaddingByte = 0xA5,
});
using (NdsImage capacityImage = NdsImage.Load(capacityBytes))
{
    if (capacityBytes.Length != 0x40000 || capacityImage.Header.DeviceCapacityBytes != 0x40000 ||
        capacityImage.Header.UsedImageSize != image.Header.UsedImageSize || capacityBytes[^1] != 0xA5 ||
        !capacityImage.Validate().IsValid)
        throw new InvalidOperationException("Capacity policy package consumer failed.");
}
byte[] authenticationKey = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
builder.NandRomEndUnits = 2;
builder.NandWritableStartUnits = 3;
using (NdsImage nandImage = NdsImage.Load(await builder.BuildAsync()))
{
    NdsImageManifest nandManifest = await nandImage.CreateManifestAsync();
    if (nandImage.Header.NandRomEndOffset != 262144 || nandImage.Header.NandWritableStartOffset != 393216 ||
        nandImage.Header.DeviceCapacityBytes != 524288 || nandImage.Length != bytes.Length ||
        nandManifest.Header.NandRomEndUnits != 2 || nandManifest.Header.NandWritableStartUnits != 3)
        throw new InvalidOperationException("NAND boundary package consumer failed.");
}
builder.NandRomEndUnits = builder.NandWritableStartUnits = 0;
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
var digitalBuilder = new NdsImageBuilder
{
    Title = "DIGITAL", GameCode = "DGTL", MakerCode = "HB",
    Carrier = NdsImageCarrier.DigitalSrl, Kind = NdsImageKind.NintendoDsiExclusive,
    Arm9 = new(NdsProcessor.Arm9, [1], 0x02004000, 0x02004000),
    Arm7 = new(NdsProcessor.Arm7, [2], 0x02380000, 0x02380000),
    Arm9i = new(NdsProcessor.Arm9i, [], 0x02400000, 0x02400000),
    Arm7i = new(NdsProcessor.Arm7i, [], 0x02E80000, 0x02E80000),
    DsiMetadata = new() { TitleId = 0x0003000454455354 },
};
byte[] carrierBytes = Enumerable.Repeat((byte)0x73, 0x3000).ToArray();
digitalBuilder.SetPostHeaderData(carrierBytes);
using NdsImage digitalImage = NdsImage.Load(await digitalBuilder.BuildAsync());
if (digitalImage.CarrierLayout is not NdsDigitalSrlLayout ||
    !digitalImage.CarrierLayout.PostHeaderData.Span.SequenceEqual(carrierBytes) ||
    NdsSecureArea.Inspect(digitalImage).State != NdsSecureAreaState.Absent)
    throw new InvalidOperationException("Digital carrier package consumer failed.");
var cartridgeBuilder = new NdsImageBuilder
{
    Kind = NdsImageKind.NintendoDsiEnhanced,
    Arm9 = new(NdsProcessor.Arm9, [1], 0x02000000, 0x02000000),
    Arm7 = new(NdsProcessor.Arm7, [2], 0x02380000, 0x02380000),
    Arm9i = new(NdsProcessor.Arm9i, [3], 0x02400000, 0x02400000),
    Arm7i = new(NdsProcessor.Arm7i, [4], 0x02E80000, 0x02E80000),
    DsiMetadata = new(),
};
cartridgeBuilder.SetTwlReservedData(carrierBytes);
using NdsImage cartridgeImage = NdsImage.Load(await cartridgeBuilder.BuildAsync());
if (cartridgeImage.CarrierLayout is not NdsCartridgeLayout cartridgeLayout ||
    cartridgeLayout.TwlRegionStart != 0x80000 || cartridgeLayout.TwlReservedRegion?.Length != 0x3000 ||
    !cartridgeLayout.TwlReservedData.Span.SequenceEqual(carrierBytes) || cartridgeImage.Header.Arm7i!.Data.Offset < 0x87000)
    throw new InvalidOperationException("Cartridge reservation package consumer failed.");
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
string apiWorkspace = Path.Combine(args[0], "api-workspace");
string apiPacked = Path.Combine(args[0], "api-packed.nds");
using (NdsImage workspaceImage = NdsImage.Load(capacityBytes))
{
    NdsWorkspaceRecipe recipe = await NdsImageWorkspace.ExportAsync(workspaceImage, apiWorkspace);
    if (recipe.SchemaVersion != 1 || recipe.Assets.Count == 0 ||
        NdsWorkspaceRecipe.ParseJson(recipe.ToJson()).SourceInventory.ImageSha256 != recipe.SourceInventory.ImageSha256)
        throw new InvalidOperationException("Workspace package recipe consumer failed.");
    NdsWorkspaceRecipe packed = await NdsImageWorkspace.PackFileAsync(apiWorkspace, apiPacked);
    if (packed.SourceInventory.ImageSha256 != recipe.SourceInventory.ImageSha256 ||
        !(await File.ReadAllBytesAsync(apiPacked)).AsSpan().SequenceEqual(capacityBytes))
        throw new InvalidOperationException("Workspace package exact-packing consumer failed.");
    NdsWorkspaceAsset payload = recipe.Assets.Single(asset => asset.Kind == NdsWorkspaceAssetKind.Allocation);
    await File.WriteAllBytesAsync(Path.Combine(apiWorkspace, payload.Path), "changed workspace"u8.ToArray());
    NdsImageBuilder imported = await NdsImageWorkspace.ImportAsync(apiWorkspace, NdsWorkspaceImportOptions.Default);
    imported.Title = "WORKSPACE";
    using NdsImage rebuiltWorkspace = NdsImage.Load(await imported.BuildAsync());
    if (rebuiltWorkspace.Header.Title != "WORKSPACE" ||
        !(await rebuiltWorkspace.FileSystem.GetFile("/hello.txt").ReadAllBytesAsync()).AsSpan().SequenceEqual("changed workspace"u8))
        throw new InvalidOperationException("Workspace package structural-import consumer failed.");
}
Console.WriteLine("PACKAGE_CONSUMER_OK");
await File.WriteAllBytesAsync(Path.Combine(args[0], "authenticated-source.nds"), authenticatedStream.ToArray());
await File.WriteAllBytesAsync(Path.Combine(args[0], "dsi-source.nds"), await cartridgeBuilder.BuildAsync());
await File.WriteAllBytesAsync(Path.Combine(args[0], "digital-source.nds"), await digitalBuilder.BuildAsync());
await File.WriteAllBytesAsync(Path.Combine(args[0], "resize-source.nds"), capacityBytes);
capacityBytes[^1] = 37;
await File.WriteAllBytesAsync(Path.Combine(args[0], "resize-unclassified.nds"), capacityBytes);
'@
    [System.IO.File]::WriteAllText((Join-Path $consumer "Program.cs"), $program, [System.Text.UTF8Encoding]::new($false))
    dotnet run --project $consumer --configuration Release --no-restore -- $workspace
    if ($LASTEXITCODE -ne 0) { throw "Clean NdsForge package consumer failed." }

    dotnet tool install NdsForge.Cli --version $Version --tool-path $toolPath `
        --configfile $configPath --no-cache
    if ($LASTEXITCODE -ne 0) { throw "Installing the local NdsForge CLI package failed." }
    $executableName = if ($IsWindows) { "ndsforge.exe" } else { "ndsforge" }
    $executable = Join-Path $toolPath $executableName
    & $executable --help
    if ($LASTEXITCODE -ne 0) { throw "The packaged ndsforge tool did not start successfully." }
    $resizeSource = Join-Path $workspace 'resize-source.nds'
    $resizeUnclassified = Join-Path $workspace 'resize-unclassified.nds'
    $resizeCopy = Join-Path $workspace 'resize-copy.nds'
    $resizeTrimmed = Join-Path $workspace 'resize-trimmed.nds'
    $resizeExpanded = Join-Path $workspace 'resize-expanded.nds'
    $resizeExact = Join-Path $workspace 'resize-exact.nds'
    $resizeRejected = Join-Path $workspace 'resize-rejected.nds'
    $resizeDiscarded = Join-Path $workspace 'resize-discarded.nds'
    function Invoke-ResizeCheck([int]$expectedExit, [string[]]$arguments) {
        & $executable @arguments
        if ($LASTEXITCODE -ne $expectedExit) { throw "CLI resize exit was $LASTEXITCODE, expected $expectedExit." }
    }
    Invoke-ResizeCheck 0 @('resize', $resizeSource, $resizeCopy, 'preserve')
    Invoke-ResizeCheck 1 @('resize', $resizeSource, $resizeCopy, 'preserve')
    Invoke-ResizeCheck 0 @('resize', $resizeSource, $resizeCopy, 'preserve', '--overwrite')
    Invoke-ResizeCheck 0 @('resize', $resizeSource, $resizeTrimmed, 'trim', '--padding-byte', 'A5')
    Invoke-ResizeCheck 0 @('resize', $resizeTrimmed, $resizeExpanded, 'pad', '--padding-byte', 'A5')
    Invoke-ResizeCheck 0 @('resize', $resizeSource, $resizeExact, 'exact', '--length', '0x30000', '--padding-byte', 'A5')
    Invoke-ResizeCheck 1 @('resize', $resizeSource, $resizeRejected, 'exact', '--length', '1')
    Invoke-ResizeCheck 1 @('resize', $resizeUnclassified, $resizeRejected, 'trim', '--padding-byte', 'A5')
    Invoke-ResizeCheck 0 @('resize', $resizeUnclassified, $resizeDiscarded, 'trim', '--discard-trailing')
    Invoke-ResizeCheck 1 @('resize', $resizeSource, $resizeSource, 'preserve', '--overwrite')
    foreach ($invalid in @(
        @('exact'), @('unknown'), @('trim', '--length', '1000'),
        @('trim', '--padding-byte', 'GG'), @('trim', '--padding-byte'),
        @('trim', '--overwrite', '--overwrite'), @('pad', '--discard-trailing'),
        @('exact', '--length', '0x100000001'), @('exact', '--length', '-1'))) {
        Invoke-ResizeCheck 2 (@('resize', $resizeSource, $resizeRejected) + $invalid)
    }
    if (Test-Path -LiteralPath $resizeRejected) { throw 'Rejected CLI resize created output.' }
    $sourceDigest = (Get-FileHash -LiteralPath $resizeSource -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $resizeCopy -Algorithm SHA256).Hash -ne $sourceDigest -or
        (Get-FileHash -LiteralPath $resizeExpanded -Algorithm SHA256).Hash -ne $sourceDigest) {
        throw 'CLI preserve/trim/expand did not retain exact source bytes.'
    }
    $usedLength = [BitConverter]::ToUInt32([IO.File]::ReadAllBytes($resizeSource), 0x80)
    if ((Get-Item -LiteralPath $resizeTrimmed).Length -ne $usedLength -or
        (Get-Item -LiteralPath $resizeDiscarded).Length -ne $usedLength -or
        (Get-Item -LiteralPath $resizeExact).Length -ne 0x30000) { throw 'CLI resize length mismatch.' }
    Write-Output 'CLI_RESIZE_CONSUMER_OK'
    $imageWorkspace = Join-Path $workspace 'cli-workspace'
    $workspacePacked = Join-Path $workspace 'workspace-packed.nds'
    function Invoke-WorkspaceCheck([int]$expectedExit, [string[]]$arguments) {
        & $executable @arguments
        if ($LASTEXITCODE -ne $expectedExit) { throw "CLI workspace exit was $LASTEXITCODE, expected $expectedExit." }
    }
    Invoke-WorkspaceCheck 0 @('unpack', $resizeSource, $imageWorkspace)
    Invoke-WorkspaceCheck 1 @('unpack', $resizeSource, $imageWorkspace)
    Invoke-WorkspaceCheck 2 @('unpack', $resizeSource, $imageWorkspace, '--overwrite')
    Invoke-WorkspaceCheck 0 @('pack', $imageWorkspace, $workspacePacked)
    Invoke-WorkspaceCheck 1 @('pack', $imageWorkspace, $workspacePacked)
    Invoke-WorkspaceCheck 0 @('pack', $imageWorkspace, $workspacePacked, '--overwrite')
    Invoke-WorkspaceCheck 2 @('pack', $imageWorkspace, $workspacePacked, '--unknown')
    Invoke-WorkspaceCheck 2 @('pack', $imageWorkspace)
    Invoke-WorkspaceCheck 2 @('pack', $imageWorkspace, $workspacePacked, '--overwrite', '--overwrite')
    Invoke-WorkspaceCheck 1 @('pack', $imageWorkspace, (Join-Path $imageWorkspace 'output.nds'))
    if ((Get-FileHash -LiteralPath $workspacePacked -Algorithm SHA256).Hash -ne $sourceDigest) {
        throw 'CLI workspace output differs from complete source identity.'
    }
    $workspaceRecipePath = Join-Path $imageWorkspace 'ndsforge-workspace.json'
    $workspaceRecipe = Get-Content -LiteralPath $workspaceRecipePath -Raw | ConvertFrom-Json
    $workspaceAsset = Join-Path $imageWorkspace $workspaceRecipe.assets[0].path
    $workspaceSavedAsset = "$workspaceAsset.original"
    Move-Item -LiteralPath $workspaceAsset -Destination $workspaceSavedAsset
    Invoke-WorkspaceCheck 1 @('pack', $imageWorkspace, $workspacePacked, '--overwrite')
    Move-Item -LiteralPath $workspaceSavedAsset -Destination $workspaceAsset
    $workspaceSavedRecipe = [IO.File]::ReadAllText($workspaceRecipePath)
    [IO.File]::WriteAllText($workspaceRecipePath, '{')
    Invoke-WorkspaceCheck 1 @('pack', $imageWorkspace, $workspacePacked, '--overwrite')
    [IO.File]::WriteAllText($workspaceRecipePath, $workspaceSavedRecipe)
    if ((Get-FileHash -LiteralPath $workspacePacked -Algorithm SHA256).Hash -ne $sourceDigest) {
        throw 'Failed workspace verification changed an existing CLI output.'
    }
    Write-Output 'CLI_WORKSPACE_CONSUMER_OK'
    & (Join-Path $PSScriptRoot 'test-cli-build.ps1') -Executable $executable -WorkspaceRoot $workspace
    Write-Output "TOOL_CONSUMER_OK"
} finally {
    if ([System.IO.Directory]::Exists($workspace)) { [System.IO.Directory]::Delete($workspace, $true) }
}
