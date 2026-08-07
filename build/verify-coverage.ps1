param(
    [Parameter(Mandatory = $true)][string]$CoberturaPath,
    [double]$MinimumLinePercent = 85,
    [double]$MinimumBranchPercent = 70
)

$ErrorActionPreference = "Stop"
[xml]$report = Get-Content -Raw -LiteralPath $CoberturaPath
$line = [double]$report.coverage.'line-rate' * 100
$branch = [double]$report.coverage.'branch-rate' * 100
Write-Output ("Line coverage: {0:N2}% (minimum {1:N2}%)" -f $line, $MinimumLinePercent)
Write-Output ("Branch coverage: {0:N2}% (minimum {1:N2}%)" -f $branch, $MinimumBranchPercent)
if ($line -lt $MinimumLinePercent -or $branch -lt $MinimumBranchPercent) {
    throw "Coverage threshold failed."
}
