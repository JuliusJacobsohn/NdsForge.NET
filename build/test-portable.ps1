param(
    [string]$Configuration = "Release",
    [string]$ResultsDirectory = "artifacts/test-results"
)

$ErrorActionPreference = "Stop"
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$resolvedResults = if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    [System.IO.Path]::GetFullPath($ResultsDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repository $ResultsDirectory))
}
$trxPath = Join-Path $resolvedResults "unit.trx"

New-Item -ItemType Directory -Path $resolvedResults -Force | Out-Null
if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath -Force }

Push-Location $repository
try {
    dotnet test tests/NdsForge.Tests/NdsForge.Tests.csproj `
        --configuration $Configuration `
        --no-build `
        --no-restore `
        --collect:"XPlat Code Coverage" `
        --settings coverlet.runsettings `
        --results-directory $resolvedResults `
        --logger "trx;LogFileName=unit.trx"
    if ($LASTEXITCODE -ne 0) { throw "Portable unit tests failed with exit code $LASTEXITCODE." }

    ./build/verify-test-result.ps1 -TrxPath $trxPath -SuiteName "portable unit"
    if (-not $?) { throw "Portable test-result verification failed." }
} finally {
    Pop-Location
}
