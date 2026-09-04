$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-policy.ps1')
$script:checks = 0

function Assert-Equal($Actual, $Expected) {
    if ($Actual -cne $Expected) { throw "Expected '$Expected', received '$Actual'." }
    $script:checks++
}

function Assert-Rejected([scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw 'An unsafe release policy input was accepted.' }
    $script:checks++
}

foreach ($invalid in @('', 'v1.1.0', '01.1.0', '1.1', '1.1.0-preview', '1.1.0+build')) {
    Assert-Rejected { Assert-StableVersion $invalid }
}
Assert-ReleaseVersion '1.1.0' '1.0.1' $true
Assert-ReleaseVersion '1.1.0' '1.1.0' $false
Assert-Rejected { Assert-ReleaseVersion '1.1.0' '1.1.0' $true }
Assert-Rejected { Assert-ReleaseVersion '1.0.1' '1.1.0' $false }

$trigger = @{
    EventName = 'workflow_run'; WorkflowRef = 'refs/heads/main'; RequestedPublish = $false
    Repository = 'owner/repository'; TriggerRepository = 'owner/repository'
    TriggerBranch = 'main'; TriggerEvent = 'push'; TriggerConclusion = 'success'
}
Assert-Equal (Test-ReleaseTrigger @trigger) $true
foreach ($change in @(
    @{ EventName = 'pull_request' }, @{ WorkflowRef = 'refs/heads/feature' },
    @{ TriggerRepository = 'other/repository' }, @{ TriggerBranch = 'feature' },
    @{ TriggerEvent = 'pull_request' }, @{ TriggerConclusion = 'failure' },
    @{ TriggerConclusion = 'cancelled' }, @{ TriggerConclusion = 'skipped' }
)) {
    $invalidTrigger = $trigger.Clone()
    foreach ($key in $change.Keys) { $invalidTrigger[$key] = $change[$key] }
    Assert-Rejected { Test-ReleaseTrigger @invalidTrigger }
}
$trigger.EventName = 'workflow_dispatch'
Assert-Equal (Test-ReleaseTrigger @trigger) $false
$trigger.RequestedPublish = $true
Assert-Equal (Test-ReleaseTrigger @trigger) $true
$trigger.WorkflowRef = 'refs/heads/feature'
Assert-Rejected { Test-ReleaseTrigger @trigger }

$commit = 'a' * 40
$older = 'b' * 40
Assert-Equal (Get-ReleaseDisposition '1.1.0' $commit '' $false 0 $false) 'new'
foreach ($count in 0..5) {
    Assert-Equal (Get-ReleaseDisposition '1.1.0' $commit $commit $true $count $false) 'retry'
}
Assert-Equal (Get-ReleaseDisposition '1.1.0' $commit $commit $true 5 $true) 'already-published'
Assert-Equal (Get-ReleaseDisposition '1.1.0' $commit $older $true 5 $true) 'already-published'
Assert-Rejected { Get-ReleaseDisposition '1.1.0' $commit '' $false 1 $false }
Assert-Rejected { Get-ReleaseDisposition '1.1.0' $commit '' $false 0 $true }
Assert-Rejected { Get-ReleaseDisposition '1.1.0' $commit $older $false 5 $true }
Assert-Rejected { Get-ReleaseDisposition '1.1.0' $commit $older $true 4 $true }
Assert-Rejected { Get-ReleaseDisposition '1.1.0' $commit $older $true 5 $false }
Assert-Rejected { Get-ReleaseDisposition '1.1.0' 'main' '' $false 0 $false }
Assert-Rejected { Get-ReleaseDisposition '1.1.0' $commit 'bad' $true 5 $true }
Assert-Rejected { Get-ReleaseDisposition '1.1.0' $commit '' $false 6 $false }
Assert-Rejected { Get-ReleaseDisposition '1.1.0' $commit $older $true 5 $true $true }
$notes = "## Unreleased`n`n## 1.1.0 - 2026-09-04`n`n### Added`n`n- Feature.`n`n## 1.0.1`nOlder."
Assert-Equal (Get-ReleaseNotes $notes '1.1.0') "### Added`n`n- Feature."
Assert-Equal (Get-ReleaseNotes ($notes.Replace("`n", "`r`n")) '1.1.0') "### Added`r`n`r`n- Feature."
Assert-Rejected { Get-ReleaseNotes $notes '1.1.1' }
Assert-Rejected { Get-ReleaseNotes "## 1.1.0`n`n## 1.0.1`nOlder." '1.1.0' }
Write-Output "RELEASE_POLICY_OK checks=$script:checks"
