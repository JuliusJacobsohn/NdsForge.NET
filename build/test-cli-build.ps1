param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string]$WorkspaceRoot
)

$ErrorActionPreference = 'Stop'
$buildWorkspace = Join-Path $WorkspaceRoot 'cli-build-workspace'
$buildOutput = Join-Path $WorkspaceRoot 'cli-built.nds'
$buildRepeated = Join-Path $WorkspaceRoot 'cli-built-again.nds'
$buildManifest = Join-Path $WorkspaceRoot 'cli-built.json'
function Invoke-BuildCheck([int]$expectedExit, [string[]]$arguments) {
    & $Executable @arguments
    if ($LASTEXITCODE -ne $expectedExit) { throw "CLI build exit was $LASTEXITCODE, expected $expectedExit." }
}
Invoke-BuildCheck 0 @('unpack', (Join-Path $WorkspaceRoot 'resize-source.nds'), $buildWorkspace)
$recipe = Get-Content -LiteralPath (Join-Path $buildWorkspace 'ndsforge-workspace.json') -Raw | ConvertFrom-Json
$payload = @($recipe.assets | Where-Object kind -eq 'allocation')
if ($payload.Count -ne 1) { throw 'Expected one private synthetic consumer allocation.' }
$edited = [Text.Encoding]::UTF8.GetBytes('changed CLI workspace')
$payloadPath = Join-Path $buildWorkspace $payload[0].path
[IO.File]::WriteAllBytes($payloadPath, $edited)
$buildOptions = @('--capacity', '0x80000', '--pad', '--padding-byte', 'A5')
Invoke-BuildCheck 0 (@('build', $buildWorkspace, $buildOutput) + $buildOptions)
Invoke-BuildCheck 0 (@('build', $buildWorkspace, $buildRepeated) + $buildOptions)
$expectedHash = (Get-FileHash -LiteralPath $buildOutput -Algorithm SHA256).Hash
if ($expectedHash -ne (Get-FileHash -LiteralPath $buildRepeated -Algorithm SHA256).Hash) { throw 'CLI build was not deterministic.' }
$bytes = [IO.File]::ReadAllBytes($buildOutput)
$usedLength = [BitConverter]::ToUInt32($bytes, 0x80)
if ($bytes.Length -ne 0x80000 -or $bytes[0x14] -ne 2 -or $usedLength -ge $bytes.Length) { throw 'CLI build capacity or used-size mismatch.' }
for ($offset = $usedLength; $offset -lt $bytes.Length; $offset++) {
    if ($bytes[$offset] -ne 0xA5) { throw 'CLI build padding mismatch.' }
}
Invoke-BuildCheck 0 @('manifest', $buildOutput, $buildManifest)
$manifest = Get-Content -LiteralPath $buildManifest -Raw | ConvertFrom-Json
$editedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($edited))
if ($manifest.files.Count -ne 1 -or $manifest.files[0].path -ne '/hello.txt' -or
    $manifest.files[0].sha256 -ne $editedHash) { throw 'CLI build did not import the edited payload.' }
Invoke-BuildCheck 1 @('build', $buildWorkspace, $buildOutput)
Invoke-BuildCheck 0 (@('build', $buildWorkspace, $buildOutput, '--overwrite') + $buildOptions)
Invoke-BuildCheck 1 @('build', $buildWorkspace, $payloadPath, '--overwrite')
Invoke-BuildCheck 1 @('pack', $buildWorkspace, $buildOutput, '--overwrite')
foreach ($invalid in @(@('--capacity'), @('--capacity', '131073'), @('--pad', '--pad'), @('--no-verify'),
    @('--dsi-integrity', 'preserve'), @('--ds-integrity', 'clear', '--dsi-integrity', 'clear'))) {
    Invoke-BuildCheck 2 (@('build', $buildWorkspace, $buildOutput, '--overwrite') + $invalid)
}
$savedPayload = "$payloadPath.original"
Move-Item -LiteralPath $payloadPath -Destination $savedPayload
Invoke-BuildCheck 1 @('build', $buildWorkspace, $buildOutput, '--overwrite')
Move-Item -LiteralPath $savedPayload -Destination $payloadPath
if ((Get-FileHash -LiteralPath $buildOutput -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Rejected CLI build changed existing output.' }

foreach ($kind in @('dsi', 'digital', 'authenticated')) {
    $sourceImage = Join-Path $WorkspaceRoot "$kind-source.nds"
    $workspace = Join-Path $WorkspaceRoot "cli-build-$kind"
    $output = Join-Path $WorkspaceRoot "cli-built-$kind.nds"
    Invoke-BuildCheck 0 @('unpack', $sourceImage, $workspace)
    Invoke-BuildCheck 1 @('build', $workspace, $output)
    if (Test-Path -LiteralPath $output) { throw 'Missing authentication choice published output.' }
    $flag = if ($kind -eq 'authenticated') { '--ds-integrity' } else { '--dsi-integrity' }
    Invoke-BuildCheck 0 @('build', $workspace, $output, $flag, 'clear')
    Invoke-BuildCheck 0 @('validate', $output)
}
Write-Output 'CLI_BUILD_CONSUMER_OK'
