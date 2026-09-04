param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Destination = 'artifacts/published',
    [ValidateRange(1, 60)][int]$TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-policy.ps1')
Assert-StableVersion $Version
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$pending = [System.Collections.Generic.List[string]]::new()
foreach ($id in @('NdsForge', 'NdsForge.Nitro', 'NdsForge.Graphics', 'NdsForge.Audio.Wav', 'NdsForge.Cli')) { $pending.Add($id) }
$deadline = [DateTimeOffset]::UtcNow.AddMinutes($TimeoutMinutes)
while ($pending.Count -gt 0) {
    foreach ($id in @($pending)) {
        $lower = $id.ToLowerInvariant()
        try {
            $index = Invoke-RestMethod -Uri "https://api.nuget.org/v3-flatcontainer/$lower/index.json"
            if (@($index.versions) -notcontains $Version) { continue }
            Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$lower/$Version/$lower.$Version.nupkg" `
                -OutFile (Join-Path $Destination "$id.$Version.nupkg")
            [void]$pending.Remove($id)
            Write-Output "NUGET_AVAILABLE package=$id version=$Version"
        } catch {
            if ($null -eq $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404) { throw }
        }
    }
    if ($pending.Count -eq 0) { break }
    if ([DateTimeOffset]::UtcNow -ge $deadline) { throw "NuGet indexing timed out for: $($pending -join ', '). Retry the same release commit." }
    Start-Sleep -Seconds 15
}
& (Join-Path $PSScriptRoot 'test-package-consumer.ps1') -PackageDirectory $Destination -Version $Version
if (-not $?) { throw 'Published-package consumer verification failed.' }
