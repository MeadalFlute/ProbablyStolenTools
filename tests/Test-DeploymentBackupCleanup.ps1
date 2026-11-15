$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$root = Join-Path $workspace ('out\backup-cleanup-test-' + [guid]::NewGuid().ToString('N'))
$toolDir = Join-Path $root 'scripts'
$outDir = Join-Path $root 'out'
New-Item -ItemType Directory -Path $toolDir, $outDir | Out-Null
$tool = Join-Path $toolDir 'Prune-DeploymentBackups.ps1'
Copy-Item -LiteralPath (Join-Path $workspace 'scripts\Prune-DeploymentBackups.ps1') -Destination $tool
function Add-Backup([string]$name, [string]$payload) {
    $folder = Join-Path $outDir $name
    $save = Join-Path $folder 'SaveData'
    New-Item -ItemType Directory -Path $save | Out-Null
    $file = Join-Path $save 'save_2.es3'
    Set-Content -LiteralPath $file -Value $payload
    @{
        Runtime = 'Mono'; ExistingSaveFilesUnchanged = $true
        SaveFilesBefore = @(@{ Path = 'save_2.es3'; SHA256 = (Get-FileHash -LiteralPath $file).Hash })
        ModsBefore = @(@{ Path = 'ProbablyStolenTools\ProbablyStolenTools.dll'; SHA256 = 'fixture' })
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $folder 'deployment.json')
    return $folder
}
function Check([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    Write-Output "PASS $message"
}
try {
    $latest = Add-Backup 'full-release-deploy-20261115-220000' 'save A'
    $redundant = Add-Backup 'full-release-deploy-20261115-210000' 'save A'
    $unique = Add-Backup 'full-release-deploy-20261115-200000' 'save B'
    $recovery = Join-Path $outDir 'save-recovery-day94-20261115-214109'
    New-Item -ItemType Directory -Path $recovery | Out-Null
    & $tool -KeepLatest 1 | Out-Null
    Check (Test-Path -LiteralPath $redundant) 'preview never removes redundant backups'
    $extra = Join-Path $redundant 'SaveData\extra.es3'
    Set-Content -LiteralPath $extra -Value 'new save'
    $failed = $false
    try { & $tool -KeepLatest 1 -Apply -Confirm:$false | Out-Null } catch { $failed = $true }
    Check ($failed -and (Test-Path -LiteralPath $redundant)) 'unmanifested saves block deletion'
    Remove-Item -LiteralPath $extra
    $original = Join-Path $redundant 'SaveData\save_2.es3'
    $bytes = [IO.File]::ReadAllBytes($original)
    Set-Content -LiteralPath $original -Value 'changed candidate'
    $failed = $false
    try { & $tool -KeepLatest 1 -Apply -Confirm:$false | Out-Null } catch { $failed = $true }
    Check ($failed -and (Test-Path -LiteralPath $redundant)) 'changed candidate content blocks deletion'
    [IO.File]::WriteAllBytes($original, $bytes)
    $retained = Join-Path $latest 'SaveData\save_2.es3'
    $retainedBytes = [IO.File]::ReadAllBytes($retained)
    Set-Content -LiteralPath $retained -Value 'changed retained'
    $failed = $false
    try { & $tool -KeepLatest 1 -Apply -Confirm:$false | Out-Null } catch { $failed = $true }
    Check ($failed -and (Test-Path -LiteralPath $redundant)) 'changed retained copy blocks deletion'
    [IO.File]::WriteAllBytes($retained, $retainedBytes)
    & $tool -KeepLatest 1 -Apply -Confirm:$false | Out-Null
    Check (!(Test-Path -LiteralPath $redundant) -and (Test-Path -LiteralPath $unique) -and
        (Test-Path -LiteralPath $latest)) 'apply removes only redundant copies, preserving latest and every unique save'
    Check (Test-Path -LiteralPath $recovery) 'independent save recovery directories are never touched'
}
finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::GetFullPath((Join-Path $workspace 'out')) -or
        [IO.Path]::GetFileName($resolved) -notlike 'backup-cleanup-test-*') { throw 'Invalid test cleanup target.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
