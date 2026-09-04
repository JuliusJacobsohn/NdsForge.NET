$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-policy.ps1')
$publish = Test-ReleaseTrigger -EventName $env:RELEASE_EVENT -WorkflowRef $env:GITHUB_REF `
    -RequestedPublish ($env:REQUESTED_PUBLISH -eq 'true') -Repository $env:GITHUB_REPOSITORY `
    -TriggerRepository $env:TRIGGER_REPOSITORY -TriggerBranch $env:TRIGGER_BRANCH `
    -TriggerEvent $env:TRIGGER_EVENT -TriggerConclusion $env:TRIGGER_CONCLUSION
$commit = (& git rev-parse --verify HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne $env:RELEASE_COMMIT) { throw 'Release checkout does not match the requested commit.' }
git merge-base --is-ancestor $commit origin/main
if ($LASTEXITCODE -ne 0) { throw 'The release commit is not part of main.' }
[xml]$properties = Get-Content -Raw -LiteralPath Version.props
$version = $properties.Project.PropertyGroup.Version
Assert-StableVersion $version
$tag = "v$version"
$tagCommit = ''
git show-ref --verify --quiet "refs/tags/$tag"
$lookup = $LASTEXITCODE
if ($lookup -notin @(0, 1)) { throw 'Release tag lookup failed.' }
$tagIsAncestor = $false
if ($lookup -eq 0) {
    $tagCommit = (& git rev-parse --verify "refs/tags/$tag^{commit}").Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Release tag resolution failed.' }
    git merge-base --is-ancestor $tagCommit $commit
    if ($LASTEXITCODE -notin @(0, 1)) { throw 'Release ancestry check failed.' }
    $tagIsAncestor = $LASTEXITCODE -eq 0
}
$global:LASTEXITCODE = 0
$published = 0
foreach ($id in @('ndsforge', 'ndsforge.nitro', 'ndsforge.graphics', 'ndsforge.audio.wav', 'ndsforge.cli')) {
    try {
        $index = Invoke-RestMethod -Uri "https://api.nuget.org/v3-flatcontainer/$id/index.json"
        if (@($index.versions) -contains $version) { $published++ }
    } catch {
        if ($null -eq $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404) { throw }
    }
}
$releaseExists = $false
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$env:GITHUB_REPOSITORY/releases/tags/$tag" `
        -Headers @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json' }
    $releaseExists = -not $release.draft -and -not $release.prerelease
} catch {
    if ($null -eq $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404) { throw }
}
$packageChanged = $false
if ($tagCommit -and $tagCommit -ne $commit) {
    $changed = @(& git diff --name-only $tagCommit $commit -- src assets/branding Directory.Build.props Directory.Build.targets Directory.Packages.props)
    if ($LASTEXITCODE -ne 0) { throw 'Comparing release package sources failed.' }
    $packageChanged = $changed.Count -gt 0
}
$disposition = Get-ReleaseDisposition $version $commit $tagCommit $tagIsAncestor $published $releaseExists $packageChanged
$prepare = $disposition -ne 'already-published'
"PACKAGE_VERSION=$version" >> $env:GITHUB_ENV
"RELEASE_TAG=$tag" >> $env:GITHUB_ENV
"version=$version" >> $env:GITHUB_OUTPUT
"prepare=$($prepare.ToString().ToLowerInvariant())" >> $env:GITHUB_OUTPUT
"publish=$(($prepare -and $publish).ToString().ToLowerInvariant())" >> $env:GITHUB_OUTPUT
"tag_existed=$((-not [string]::IsNullOrEmpty($tagCommit)).ToString().ToLowerInvariant())" >> $env:GITHUB_OUTPUT
Write-Output "RELEASE_IDENTITY version=$version commit=$commit disposition=$disposition publish=$($prepare -and $publish)"
