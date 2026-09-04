param(
    [string]$Configuration = "Release",
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$ArtifactSubdirectory
)

$ErrorActionPreference = "Stop"
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repository "artifacts"))
if ($ArtifactSubdirectory) { $artifactRoot = Join-Path $artifactRoot $ArtifactSubdirectory }
$testResults = Join-Path $artifactRoot "test-results"
$coverageOutput = Join-Path $artifactRoot "coverage"
$packageOutput = Join-Path $artifactRoot "packages"

function Assert-NativeSuccess([string]$operation) {
    if ($LASTEXITCODE -ne 0) { throw "$operation failed with exit code $LASTEXITCODE." }
}

function Reset-GeneratedDirectory([string]$path) {
    $resolved = [System.IO.Path]::GetFullPath($path)
    $prefix = $artifactRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset generated directory outside '$artifactRoot': '$resolved'."
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}

Push-Location $repository
try {
    ./build/test-release-policy.ps1
    if (-not $?) { throw "Release policy tests failed." }
    dotnet restore NdsForge.slnx --locked-mode
    Assert-NativeSuccess "Solution restore"
    ./build/verify-dependencies.ps1
    if (-not $?) { throw "Dependency vulnerability audit failed." }
    dotnet format NdsForge.slnx --verify-no-changes --no-restore
    Assert-NativeSuccess "Formatting verification"
    ./build/verify-source-size.ps1
    if (-not $?) { throw "Source-size verification failed." }
    dotnet build NdsForge.slnx --configuration $Configuration --no-restore
    Assert-NativeSuccess "Solution build"

    Reset-GeneratedDirectory $testResults
    Reset-GeneratedDirectory $coverageOutput
    ./build/test-portable.ps1 -Configuration $Configuration -ResultsDirectory $testResults
    if (-not $?) { throw "Portable test run failed." }
    dotnet tool restore
    Assert-NativeSuccess "Local tool restore"
    dotnet tool run reportgenerator `
        "-reports:$testResults/**/coverage.cobertura.xml" `
        "-targetdir:$coverageOutput" `
        "-reporttypes:Cobertura;Html"
    Assert-NativeSuccess "Coverage report generation"
    ./build/verify-coverage.ps1 (Join-Path $coverageOutput "Cobertura.xml")
    if (-not $?) { throw "Coverage verification failed." }

    Reset-GeneratedDirectory $packageOutput
    foreach ($project in @(
        "src/NdsForge/NdsForge.csproj",
        "src/NdsForge.Nitro/NdsForge.Nitro.csproj",
        "src/NdsForge.Graphics/NdsForge.Graphics.csproj",
        "src/NdsForge.Audio.Wav/NdsForge.Audio.Wav.csproj",
        "src/NdsForge.Cli/NdsForge.Cli.csproj")) {
        dotnet pack $project --configuration $Configuration --no-build --output $packageOutput
        Assert-NativeSuccess "Packing $project"
    }
    $versionOutput = & dotnet msbuild src/NdsForge/NdsForge.csproj -nologo -getProperty:PackageVersion
    Assert-NativeSuccess "Package version resolution"
    $version = ($versionOutput | Select-Object -Last 1).Trim()
    if ([string]::IsNullOrWhiteSpace($version)) { throw "Package version resolution returned an empty value." }
    foreach ($packageId in @("NdsForge", "NdsForge.Nitro", "NdsForge.Graphics", "NdsForge.Audio.Wav", "NdsForge.Cli")) {
        $path = Join-Path $packageOutput "$packageId.$version.nupkg"
        ./build/validate-package.ps1 -PackagePath $path -PackageId $packageId -ExpectedVersion $version
        if (-not $?) { throw "$packageId package validation failed." }
    }
    ./build/test-package-consumer.ps1 -PackageDirectory $packageOutput -Version $version
    if (-not $?) { throw "Clean package-consumer tests failed." }

    ./build/build-docs.ps1 -NoRestore
    if (-not $?) { throw "Documentation build failed." }
} finally {
    Pop-Location
}
