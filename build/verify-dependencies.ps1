param([string[]]$Projects = @("NdsForge.slnx"))

$ErrorActionPreference = "Stop"
$vulnerabilities = [System.Collections.Generic.List[string]]::new()

foreach ($project in $Projects) {
    $json = & dotnet list $project package --vulnerable --include-transitive --format json --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Vulnerability audit failed for '$project'." }
    $report = ($json -join [Environment]::NewLine) | ConvertFrom-Json
    if ($report.version -ne 1 -or $null -eq $report.projects) {
        throw "Vulnerability audit returned an unsupported report for '$project'."
    }

    foreach ($reportedProject in $report.projects) {
        if ($null -eq $reportedProject.frameworks) { continue }
        foreach ($framework in @($reportedProject.frameworks)) {
            foreach ($packageGroup in @("topLevelPackages", "transitivePackages")) {
                $property = $framework.PSObject.Properties[$packageGroup]
                if ($null -eq $property -or $null -eq $property.Value) { continue }
                foreach ($package in $property.Value) {
                    foreach ($vulnerability in @($package.vulnerabilities)) {
                        if ($null -eq $vulnerability) { continue }
                        $vulnerabilities.Add(
                            "$($reportedProject.path): $($package.id) $($package.resolvedVersion) " +
                            "[$($vulnerability.severity)] $($vulnerability.advisoryUrl)")
                    }
                }
            }
        }
    }
}

if ($vulnerabilities.Count -gt 0) {
    throw "Vulnerable dependencies were found:$([Environment]::NewLine)$($vulnerabilities -join [Environment]::NewLine)"
}

Write-Output "DEPENDENCY_AUDIT_OK"
