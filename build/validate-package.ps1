param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][ValidateSet("NdsForge", "NdsForge.Cli")][string]$PackageId,
    [string]$ExpectedVersion
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-SafeArchive([System.IO.Compression.ZipArchive]$archive, [string]$name) {
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
        $path = $entry.FullName.Replace([char]92, [char]47)
        if (-not $seen.Add($path)) { throw "$name contains duplicate entry '$path'." }
        if ([string]::IsNullOrWhiteSpace($path) -or $path.StartsWith('/') -or $path -match '^[A-Za-z]:') {
            throw "$name contains unsafe entry '$path'."
        }
        if (($path -split '/') -contains '..') { throw "$name contains parent traversal '$path'." }
    }
}

function Read-Text([System.IO.Compression.ZipArchiveEntry]$entry) {
    $stream = $entry.Open()
    $reader = [System.IO.StreamReader]::new($stream, [System.Text.UTF8Encoding]::new($false), $true)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Read-Nuspec([System.IO.Compression.ZipArchive]$archive, [string]$name) {
    $entries = @($archive.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' })
    if ($entries.Count -ne 1) { throw "$name must contain exactly one root nuspec." }
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $stringReader = [System.IO.StringReader]::new((Read-Text $entries[0]))
    $reader = [System.Xml.XmlReader]::Create($stringReader, $settings)
    try {
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return $document
    } finally {
        $reader.Dispose()
        $stringReader.Dispose()
    }
}

function Get-Metadata([System.Xml.XmlDocument]$document, [string]$name) {
    $namespaces = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespaces.AddNamespace("n", $document.DocumentElement.NamespaceURI)
    return $document.SelectSingleNode("/n:package/n:metadata/n:$name", $namespaces)
}

$resolved = (Resolve-Path -LiteralPath $PackagePath).Path
if ([System.IO.Path]::GetExtension($resolved) -ne ".nupkg") { throw "Package must be a .nupkg file." }

$archive = [System.IO.Compression.ZipFile]::OpenRead($resolved)
try {
    Assert-SafeArchive $archive "NuGet package"
    $entries = @{}
    foreach ($entry in $archive.Entries) { $entries[$entry.FullName] = $entry }

    $common = @("README.md", "CHANGELOG.md", "LICENSE", "THIRD_PARTY_NOTICES.md")
    $specific = if ($PackageId -eq "NdsForge") {
        @("lib/net10.0/NdsForge.dll", "lib/net10.0/NdsForge.xml")
    } else {
        @(
            "tools/net10.0/any/DotnetToolSettings.xml",
            "tools/net10.0/any/NdsForge.Cli.dll",
            "tools/net10.0/any/NdsForge.Cli.xml",
            "tools/net10.0/any/NdsForge.dll",
            "tools/net10.0/any/NdsForge.xml")
    }
    foreach ($required in @($common + $specific)) {
        if (-not $entries.ContainsKey($required) -or $entries[$required].Length -eq 0) {
            throw "$PackageId package is missing nonempty '$required'."
        }
    }

    $documentationPath = if ($PackageId -eq "NdsForge") {
        "lib/net10.0/NdsForge.xml"
    } else {
        "tools/net10.0/any/NdsForge.Cli.xml"
    }
    if ((Read-Text $entries[$documentationPath]) -notmatch '<members>') {
        throw "$PackageId XML documentation does not contain API members."
    }
    if ((Read-Text $entries["README.md"]) -notmatch 'juliusjacobsohn\.github\.io/NdsForge\.NET') {
        throw "$PackageId README does not link to the deployed API documentation."
    }

    $nuspec = Read-Nuspec $archive "NuGet package"
    $metadata = @{}
    foreach ($name in @("id", "version", "authors", "description", "releaseNotes", "tags", "readme", "projectUrl")) {
        $node = Get-Metadata $nuspec $name
        if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
            throw "$PackageId metadata '$name' is missing."
        }
        $metadata[$name] = $node.InnerText
    }
    if ($metadata.id -ne $PackageId) { throw "Package ID '$($metadata.id)' does not match '$PackageId'." }
    if ($metadata.authors -notmatch 'Julius Jacobsohn') { throw "$PackageId authors do not credit the maintainer." }
    if ($metadata.readme -ne "README.md") { throw "$PackageId readme metadata is inconsistent." }
    if ($metadata.projectUrl -ne "https://github.com/JuliusJacobsohn/NdsForge.NET") {
        throw "$PackageId project URL is incorrect."
    }
    if ($ExpectedVersion -and $metadata.version -ne $ExpectedVersion) {
        throw "$PackageId version '$($metadata.version)' does not match '$ExpectedVersion'."
    }

    $license = Get-Metadata $nuspec "license"
    if ($null -eq $license -or $license.GetAttribute("type") -ne "expression" -or $license.InnerText -ne "MIT") {
        throw "$PackageId must declare the MIT license expression."
    }
    $repository = Get-Metadata $nuspec "repository"
    if ($null -eq $repository -or $repository.GetAttribute("type") -ne "git" -or
        $repository.GetAttribute("url") -ne "https://github.com/JuliusJacobsohn/NdsForge.NET") {
        throw "$PackageId repository metadata is missing or incorrect."
    }

    $namespaces = [System.Xml.XmlNamespaceManager]::new($nuspec.NameTable)
    $namespaces.AddNamespace("n", $nuspec.DocumentElement.NamespaceURI)
    $dependencies = @($nuspec.SelectNodes("/n:package/n:metadata/n:dependencies//n:dependency", $namespaces))
    if ($dependencies.Count -ne 0) { throw "$PackageId unexpectedly exposes runtime NuGet dependencies." }
    $toolTypes = @($nuspec.SelectNodes("/n:package/n:metadata/n:packageTypes/n:packageType[@name='DotnetTool']", $namespaces))
    if (($PackageId -eq "NdsForge.Cli") -ne ($toolTypes.Count -eq 1)) {
        throw "$PackageId has incorrect dotnet-tool package metadata."
    }
} finally {
    $archive.Dispose()
}

$symbolPath = [System.IO.Path]::ChangeExtension($resolved, ".snupkg")
if (-not (Test-Path -LiteralPath $symbolPath -PathType Leaf)) { throw "Symbol package '$symbolPath' is missing." }
$symbolArchive = [System.IO.Compression.ZipFile]::OpenRead($symbolPath)
try {
    Assert-SafeArchive $symbolArchive "Symbol package"
    $pdbPath = if ($PackageId -eq "NdsForge") {
        "lib/net10.0/NdsForge.pdb"
    } else {
        "tools/net10.0/any/NdsForge.Cli.pdb"
    }
    $pdb = $symbolArchive.GetEntry($pdbPath)
    if ($null -eq $pdb -or $pdb.Length -eq 0) { throw "Symbol package is missing '$pdbPath'." }
    $symbolNuspec = Read-Nuspec $symbolArchive "Symbol package"
    $symbolVersion = (Get-Metadata $symbolNuspec "version").InnerText
    if ($ExpectedVersion -and $symbolVersion -ne $ExpectedVersion) {
        throw "$PackageId symbol version '$symbolVersion' does not match '$ExpectedVersion'."
    }
} finally {
    $symbolArchive.Dispose()
}

Write-Output "PACKAGE_CONTENT_OK $PackageId $($metadata.version)"
