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
New-Item -ItemType Directory -Path $resolvedResults -Force | Out-Null

Push-Location $repository
try {
    $suites = @(
        @{ Project = "tests/NdsForge.Tests/NdsForge.Tests.csproj"; Name = "core" },
        @{ Project = "tests/NdsForge.Nitro.Tests/NdsForge.Nitro.Tests.csproj"; Name = "nitro" },
        @{ Project = "tests/NdsForge.Graphics.Tests/NdsForge.Graphics.Tests.csproj"; Name = "graphics" }
    )
    foreach ($suite in $suites) {
        $trxPath = Join-Path $resolvedResults "$($suite.Name).trx"
        if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath -Force }
        dotnet test $suite.Project `
            --configuration $Configuration `
            --no-build `
            --no-restore `
            --collect:"XPlat Code Coverage" `
            --settings coverlet.runsettings `
            --results-directory $resolvedResults `
            --logger "trx;LogFileName=$($suite.Name).trx"
        if ($LASTEXITCODE -ne 0) { throw "Portable $($suite.Name) tests failed with exit code $LASTEXITCODE." }

        ./build/verify-test-result.ps1 -TrxPath $trxPath -SuiteName "portable $($suite.Name)"
        if (-not $?) { throw "Portable $($suite.Name) test-result verification failed." }
    }
} finally {
    Pop-Location
}
