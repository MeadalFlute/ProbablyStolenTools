param(
    [switch]$Apply,
    [string]$GameDir = 'E:\SteamLibrary\steamapps\common\Probably Stolen',
    [string]$SaveDir = "$env:USERPROFILE\AppData\LocalLow\Questing Goose Studio\Probably Stolen"
)
$ErrorActionPreference = 'Stop'
if (Get-Process -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $GameDir 'Probably Stolen.exe')) {
    throw 'Close the game before recovering the save.'
}
$workspace = Split-Path -Parent $PSScriptRoot
$source = Join-Path $SaveDir 'PSTools_Checkpoints\20261115_205353_7465490_开业前.es3'
$active = Join-Path $SaveDir 'save_2.es3'
$indexPath = Join-Path $SaveDir 'saves_index.es3'
$types = Add-Type -TypeDefinition (Get-Content -LiteralPath (Join-Path $workspace 'src\ProbablyStolenTools\SaveMetadata.cs') -Raw) -PassThru
$reader = ($types | Where-Object FullName -eq 'ProbablyStolenTools.SaveMetadata').GetMethod(
    'Read', [Reflection.BindingFlags]'NonPublic,Static')
function Read-Metadata([string]$path) {
    $reader.Invoke($null, @([IO.File]::ReadAllText($path).TrimStart([char]0xFEFF)))
}
$wanted = Read-Metadata $source
$before = Read-Metadata $active
if ($wanted.Day -ne 94 -or $wanted.Slot -ne 2 -or $wanted.State -ne 2 -or
    $wanted.RunId -ne 'e3b43117-4aa2-4f88-b11d-32ac6d52f5a4' -or $before.RunId -ne $wanted.RunId -or $before.Slot -ne 2) {
    throw 'Recovery identity mismatch; no files changed.'
}
$index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
if ($index.preview_2.value.slotId -ne 2) { throw 'Preview slot mismatch.' }
if (!$Apply) {
    Write-Output "Preview only: slot=$($wanted.Slot) day=$($before.Day)->$($wanted.Day) run=$($wanted.RunId). Pass -Apply only for this explicitly requested recovery."
    return
}
if ($before.Day -eq 94) { throw 'Day 94 is already active; no recovery needed.' }
$backup = Join-Path $workspace ('out\save-recovery-day94-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item -LiteralPath $active -Destination (Join-Path $backup 'save_2.before.es3')
Copy-Item -LiteralPath $indexPath -Destination (Join-Path $backup 'saves_index.before.es3')
Copy-Item -LiteralPath $source -Destination (Join-Path $backup 'day94.source.es3')
$index.preview_2.value.dayCount = $wanted.Day
$index.preview_2.value.credit = $wanted.Cash
$index.preview_2.value.lastSaveDateTime.ticks = (Get-Date).Ticks
$slotTemp = Join-Path $SaveDir 'pstools_day94_recovery.tmp'
$indexTemp = Join-Path $SaveDir 'pstools_day94_index.tmp'
if ((Test-Path -LiteralPath $slotTemp) -or (Test-Path -LiteralPath $indexTemp)) { throw 'Recovery staging file already exists.' }
Copy-Item -LiteralPath $source -Destination $slotTemp
[IO.File]::WriteAllText($indexTemp, ($index | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
[IO.File]::Move($slotTemp, $active, $true)
try { [IO.File]::Move($indexTemp, $indexPath, $true) }
catch {
    Copy-Item -LiteralPath (Join-Path $backup 'save_2.before.es3') -Destination $active -Force
    throw
}
if ((Get-FileHash -LiteralPath $active).Hash -ne (Get-FileHash -LiteralPath $source).Hash) { throw 'Recovery hash mismatch.' }
$after = Read-Metadata $active
[ordered]@{
    BeforeDay = $before.Day; RestoredDay = $after.Day; Slot = $after.Slot; RunId = $after.RunId
    Cash = $after.Cash; Source = $source; ActiveSHA256 = (Get-FileHash -LiteralPath $active).Hash
    IndexChange = 'Only preview_2 dayCount, credit and lastSaveDateTime; history and other slots preserved'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'recovery.json') -Encoding utf8
Write-Output "Recovered day=$($after.Day) slot=$($after.Slot) cash=$($after.Cash) Backup=$backup"
