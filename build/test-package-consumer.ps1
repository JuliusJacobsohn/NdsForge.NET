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
