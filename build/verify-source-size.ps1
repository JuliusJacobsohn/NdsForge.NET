param([int]$MaximumLines = 500)

$ErrorActionPreference = "Stop"
if ($MaximumLines -le 0) { throw "MaximumLines must be positive." }

$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$oversized = [System.Collections.Generic.List[string]]::new()
$checked = 0

foreach ($sourceRoot in @("src", "tests", "tools")) {
    $path = Join-Path $repository $sourceRoot
    foreach ($file in Get-ChildItem -LiteralPath $path -Recurse -File -Filter "*.cs") {
        if ($file.FullName -match "[\\/]bin[\\/]" -or $file.FullName -match "[\\/]obj[\\/]") { continue }
        $checked++
        $lineCount = @(Get-Content -LiteralPath $file.FullName).Count
        if ($lineCount -gt $MaximumLines) {
            $relative = [System.IO.Path]::GetRelativePath($repository, $file.FullName)
            $oversized.Add("$relative ($lineCount lines)")
        }
    }
}

if ($oversized.Count -gt 0) {
    throw "C# files exceed the $MaximumLines-line maintainability limit:$([Environment]::NewLine)$($oversized -join [Environment]::NewLine)"
}

Write-Output "SOURCE_SIZE_OK files=$checked maximumLines=$MaximumLines"
