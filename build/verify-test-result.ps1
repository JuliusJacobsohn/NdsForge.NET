param(
    [Parameter(Mandatory = $true)][string]$TrxPath,
    [Parameter(Mandatory = $true)][string]$SuiteName
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) {
    throw "The $SuiteName test suite did not produce the expected TRX result '$TrxPath'."
}

[xml]$result = Get-Content -Raw -LiteralPath $TrxPath
$counters = $result.TestRun.ResultSummary.Counters
$total = [int]$counters.total
$executed = [int]$counters.executed
if ($total -le 0 -or $executed -le 0) {
    throw "The $SuiteName test suite discovered or executed no tests."
}
if ([int]$counters.failed -ne 0) {
    throw "The $SuiteName test suite recorded $($counters.failed) failed tests."
}
if ($executed -ne $total -or [int]$counters.passed -ne $total) {
    throw "The $SuiteName test suite contains skipped, aborted, or otherwise non-passing tests."
}

Write-Output ("TEST_DISCOVERY_OK suite={0} total={1} executed={2} passed={3}" -f `
    $SuiteName, $total, $executed, $counters.passed)
