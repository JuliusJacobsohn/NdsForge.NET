param([switch]$NoRestore)

$ErrorActionPreference = "Stop"
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$doxygenVersion = "1.17.0"
$toolRoot = Join-Path $repository "artifacts/tools/doxygen-$doxygenVersion"
$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$runningOnLinux = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)

function Get-VerifiedDoxygen {
    if ($runningOnWindows) {
        $archiveName = "doxygen-$doxygenVersion.windows.x64.bin.zip"
        $downloadUri = "https://www.doxygen.nl/files/$archiveName"
        $expectedHash = "94594407c4cbca3049d76aacbb05d4a6f7d0f4e93c0de410b825d25ca5621c83"
        $executable = Join-Path $toolRoot "doxygen.exe"
        $archivePath = Join-Path $toolRoot $archiveName

        if (-not (Test-Path -LiteralPath $executable)) {
            New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
            Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath
            $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
            if ($actualHash -ne $expectedHash) {
                throw "Doxygen archive checksum mismatch. Expected $expectedHash, received $actualHash."
            }

            Expand-Archive -LiteralPath $archivePath -DestinationPath $toolRoot -Force
        }
    }
    elseif ($runningOnLinux) {
        $archiveName = "doxygen-$doxygenVersion.linux.bin.tar.gz"
        $downloadUri = "https://www.doxygen.nl/files/$archiveName"
        $expectedHash = "75419ef4f446fc1c24ef12514b574e66e898ee6f527c6ae2ad84f91a905823c2"
        $executable = Join-Path $toolRoot "doxygen-$doxygenVersion/bin/doxygen"
        $archivePath = Join-Path $toolRoot $archiveName

        if (-not (Test-Path -LiteralPath $executable)) {
            New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
            Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath
            $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
            if ($actualHash -ne $expectedHash) {
                throw "Doxygen archive checksum mismatch. Expected $expectedHash, received $actualHash."
            }

            & tar -xzf $archivePath -C $toolRoot
            if ($LASTEXITCODE -ne 0) { throw "Extracting the Doxygen archive failed." }
        }
    }
    else {
        $command = Get-Command doxygen -ErrorAction SilentlyContinue
        if (-not $command) {
            throw "Doxygen $doxygenVersion is required on this platform and was not found on PATH."
        }

        $executable = $command.Source
    }

    if (-not (Test-Path -LiteralPath $executable)) {
        throw "The Doxygen executable was not found at '$executable'."
    }

    $installedVersion = (& $executable --version).Trim()
    $reportedVersion = ($installedVersion -split '\s+', 2)[0]
    if ($LASTEXITCODE -ne 0 -or $reportedVersion -ne $doxygenVersion) {
        throw "Doxygen $doxygenVersion is required; found '$installedVersion'."
    }

    return $executable
}

Push-Location $repository
try {
    if (-not $NoRestore) {
        dotnet restore NdsForge.slnx --locked-mode
        if ($LASTEXITCODE -ne 0) { throw "Library restore failed." }
    }

    dotnet build src/NdsForge/NdsForge.csproj --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Library build for API documentation failed." }
    dotnet build src/NdsForge.Nitro/NdsForge.Nitro.csproj --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Nitro library build for API documentation failed." }
    dotnet build src/NdsForge.Graphics/NdsForge.Graphics.csproj --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Graphics library build for API documentation failed." }

    $versionOutput = & dotnet msbuild src/NdsForge/NdsForge.csproj -nologo -getProperty:PackageVersion
    if ($LASTEXITCODE -ne 0) { throw "Package version resolution failed." }
    $packageVersion = ($versionOutput | Select-Object -Last 1).Trim()
    if ([string]::IsNullOrWhiteSpace($packageVersion)) { throw "Package version resolution returned an empty value." }

    $doxygen = Get-VerifiedDoxygen
    $env:NDSFORGE_DOC_VERSION = $packageVersion
    $env:NDSFORGE_REPOSITORY_ROOT = $repository.Replace('\', '/')

    & $doxygen Doxyfile
    if ($LASTEXITCODE -ne 0) { throw "Doxygen reported a documentation error or warning." }

    $htmlRoot = Join-Path $repository "artifacts/docs/html"
    foreach ($relative in @("index.html", "annotated.html", "search/searchdata.js", "ndsforge-doxygen.css")) {
        $required = Join-Path $htmlRoot $relative
        if (-not (Test-Path -LiteralPath $required)) {
            throw "Doxygen output is incomplete; expected '$required'."
        }
    }

    $classIndex = Get-Content -Raw -LiteralPath (Join-Path $htmlRoot "annotated.html")
    foreach ($requiredType in @("NdsImage", "NdsImageBuilder", "NdsImageEditor", "NdsHeader", "NdsFileSystem", "NdsImageManifest", "NdsReadOptions", "NdsValidationResult")) {
        if ($classIndex -notmatch [regex]::Escape($requiredType)) {
            throw "Doxygen omitted public type '$requiredType'."
        }
    }

    $homePage = Get-Content -Raw -LiteralPath (Join-Path $htmlRoot "index.html")
    if ($homePage -notmatch 'ndsforge-doxygen\.css' -or $homePage -notmatch 'Quick start') {
        throw "Generated documentation is missing the project stylesheet or README landing content."
    }

    Write-Output "Doxygen $doxygenVersion generated NdsForge.NET $packageVersion documentation at '$htmlRoot'."
}
finally {
    Pop-Location
}
