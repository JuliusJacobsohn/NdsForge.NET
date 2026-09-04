param([Parameter(Mandatory = $true)][string]$BaseRef)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-policy.ps1')
[xml]$current = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../Version.props')
$baseText = & git show "${BaseRef}:Version.props"
if ($LASTEXITCODE -ne 0) { throw 'Unable to read the base version.' }
[xml]$baseline = $baseText -join "`n"
$changed = @(& git diff --name-only $BaseRef HEAD -- src assets/branding Directory.Build.props Directory.Build.targets Directory.Packages.props)
if ($LASTEXITCODE -ne 0) { throw 'Unable to compare package sources.' }
Assert-ReleaseVersion $current.Project.PropertyGroup.Version $baseline.Project.PropertyGroup.Version ($changed.Count -gt 0)
if ($current.Project.PropertyGroup.Version -ne $baseline.Project.PropertyGroup.Version) {
    $changelog = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../CHANGELOG.md')
    [void](Get-ReleaseNotes $changelog $current.Project.PropertyGroup.Version)
    foreach ($name in @('NdsForge', 'NdsForge.Nitro', 'NdsForge.Graphics', 'NdsForge.Audio.Wav')) {
        $pending = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot "../src/$name/PublicAPI.Unshipped.txt") |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne '#nullable enable' })
        if ($pending.Count -gt 0) { throw "$name has unshipped APIs. Review and promote the release baseline." }
    }
}
Write-Output "RELEASE_VERSION_OK version=$($current.Project.PropertyGroup.Version) base=$($baseline.Project.PropertyGroup.Version)"
