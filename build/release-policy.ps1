# Shared, side-effect-free policy used by CI and the publishing workflow.
function Assert-StableVersion([string]$Version) {
    if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw "Version '$Version' must be a stable normalized Semantic Version."
    }
}

function Assert-ReleaseVersion([string]$Version, [string]$BaseVersion, [bool]$PackageChanged) {
    Assert-StableVersion $Version
    Assert-StableVersion $BaseVersion
    if ([version]$Version -lt [version]$BaseVersion) { throw "The package version cannot decrease." }
    if ($PackageChanged -and [version]$Version -le [version]$BaseVersion) {
        throw "Package changes require a version increment in Version.props."
    }
}

function Test-ReleaseTrigger(
    [string]$EventName, [string]$WorkflowRef, [bool]$RequestedPublish,
    [string]$Repository, [string]$TriggerRepository, [string]$TriggerBranch,
    [string]$TriggerEvent, [string]$TriggerConclusion
) {
    if ($WorkflowRef -ne 'refs/heads/main') { throw "Releases must run from main." }
    if ($EventName -eq 'workflow_dispatch') { return $RequestedPublish }
    if ($EventName -ne 'workflow_run' -or $TriggerRepository -ne $Repository -or
        $TriggerBranch -ne 'main' -or $TriggerConclusion -ne 'success' -or
        $TriggerEvent -notin @('push', 'workflow_dispatch')) {
        throw "Automatic publication requires successful CI for this repository's main branch."
    }
    return $true
}

function Get-ReleaseDisposition(
    [string]$Version, [string]$Commit, [string]$TagCommit,
    [bool]$TagIsAncestor, [int]$PublishedPackages, [bool]$GitHubReleaseExists,
    [bool]$PackageChangedSinceTag = $false
) {
    Assert-StableVersion $Version
    if ($Commit -notmatch '^[0-9a-f]{40}$') { throw "The release must identify an exact commit." }
    if ($PublishedPackages -lt 0 -or $PublishedPackages -gt 5) { throw "Invalid package count." }
    if ([string]::IsNullOrEmpty($TagCommit)) {
        if ($PublishedPackages -ne 0 -or $GitHubReleaseExists) {
            throw "Published release identities exist without their matching Git tag."
        }
        return 'new'
    }
    if ($TagCommit -notmatch '^[0-9a-f]{40}$') { throw "Invalid release tag commit." }
    if ($PackageChangedSinceTag) { throw 'Package changes since the release tag require a new version.' }
    if (($TagCommit -eq $Commit -or $TagIsAncestor) -and
        $PublishedPackages -eq 5 -and $GitHubReleaseExists) { return 'already-published' }
    if ($TagCommit -ne $Commit) {
        throw "An incomplete or unrelated release uses this version. Retry its exact tagged commit, or increment the version."
    }
    return 'retry'
}

function Get-ReleaseNotes([string]$Changelog, [string]$Version) {
    Assert-StableVersion $Version
    $escaped = [regex]::Escape($Version)
    $section = [regex]::Match($Changelog, "(?ms)^## $escaped(?: - [^\r\n]+)?\r?\n(.*?)(?=^## |\z)")
    if (-not $section.Success -or [string]::IsNullOrWhiteSpace($section.Groups[1].Value)) {
        throw "Missing release notes for $Version."
    }
    return $section.Groups[1].Value.Trim()
}
