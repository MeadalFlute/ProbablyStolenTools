[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [ValidateRange(1, 1000)][int]$KeepLatest = 10,
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$outRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'out'))
if (!(Test-Path -LiteralPath $outRoot)) { return }
if ((Get-Item -LiteralPath $outRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Backup root is a link; cleanup refused.'
}
$backups = @(foreach ($dir in Get-ChildItem -LiteralPath $outRoot -Directory) {
    if ($dir.Name -notmatch '^full-release-deploy-\d{8}-\d{6}$' -or
        ($dir.Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
    $recordPath = Join-Path $dir.FullName 'deployment.json'
    if (!(Test-Path -LiteralPath $recordPath)) { continue }
    try { $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json }
    catch { Write-Warning "Invalid deployment record; preserved: $($dir.Name)"; continue }
    if ($record.Runtime -ne 'Mono' -or $record.ExistingSaveFilesUnchanged -ne $true -or
        !$record.SaveFilesBefore -or !$record.ModsBefore) {
        Write-Warning "Unrecognized deployment record; preserved: $($dir.Name)"
        continue
    }
    # Only declarations produced and verified by Deploy-FullRelease are accepted.
    $saveFiles = @($record.SaveFilesBefore | Where-Object { $_.Path -match '\.es3(?:$|\.)' })
    if (!$saveFiles -or @($saveFiles | Where-Object {
        $_.SHA256 -notmatch '^[0-9A-Fa-f]{64}$' -or
        [IO.Path]::IsPathRooted($_.Path) -or $_.Path -split '[\\/]' -contains '..'
    }).Count) {
        Write-Warning "Invalid save manifest; preserved: $($dir.Name)"
        continue
    }
    [pscustomobject]@{ Directory = $dir; Saves = $saveFiles }
})
$backups = @($backups | Sort-Object { $_.Directory.Name } -Descending)
$known = @{}
$retained = @()
$candidates = @()
for ($i = 0; $i -lt $backups.Count; $i++) {
    $backup = $backups[$i]
    $unique = @($backup.Saves | Where-Object { !$known.ContainsKey($_.SHA256) })
    if ($i -lt $KeepLatest -or $unique.Count -gt 0) {
        $retained += $backup
        foreach ($entry in $backup.Saves) {
            if (!$known.ContainsKey($entry.SHA256)) {
                $known[$entry.SHA256] = Join-Path (Join-Path $backup.Directory.FullName 'SaveData') $entry.Path
            }
        }
    }
    else { $candidates += $backup }
}
Write-Output "Recognized=$($backups.Count) Retained=$($retained.Count) RedundantCandidates=$($candidates.Count)"
$bytes = 0L
foreach ($backup in $candidates) {
    $path = [IO.Path]::GetFullPath($backup.Directory.FullName)
    if ([IO.Path]::GetDirectoryName($path) -ne $outRoot -or
        (Get-Item -LiteralPath $path).Name -notmatch '^full-release-deploy-\d{8}-\d{6}$') {
        throw "Unexpected target: $path"
    }
    $children = @(Get-ChildItem -LiteralPath $path -Recurse -Force)
    if (@($children | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
        Write-Warning "Linked content; preserved: $path"
        continue
    }
    $size = ($children | Where-Object { !$_.PSIsContainer } | Measure-Object Length -Sum).Sum
    $bytes += $size
    Write-Output ("{0} {1:N1} MiB" -f $backup.Directory.Name, ($size / 1MB))
    if (!$Apply) { continue }
    $saveRoot = Join-Path $path 'SaveData'
    $actual = @(Get-ChildItem -LiteralPath $saveRoot -File -Recurse | Where-Object { $_.Name -match '\.es3(?:$|\.)' })
    if ($actual.Count -ne $backup.Saves.Count) { throw "Save manifest changed: $saveRoot" }
    foreach ($entry in $backup.Saves) {
        $original = Join-Path $saveRoot $entry.Path
        if (!(Test-Path -LiteralPath $original) -or
            (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ne $entry.SHA256) {
            throw "Candidate save changed; deletion refused: $original"
        }
    }
    # Verify retained content before deleting any redundant directory. Recovery
    # folders and backups with any unique ES3 payload are never candidates.
    foreach ($entry in $backup.Saves) {
        $copy = $known[$entry.SHA256]
        if (!(Test-Path -LiteralPath $copy) -or
            (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $entry.SHA256) {
            throw "Retained save missing or changed: $copy"
        }
    }
    if ($PSCmdlet.ShouldProcess($path, 'Delete redundant deployment backup')) {
        # Recheck the absolute target and links immediately before recursive deletion.
        if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path)) -ne $outRoot -or
            ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            @(Get-ChildItem -LiteralPath $path -Recurse -Force | Where-Object {
                $_.Attributes -band [IO.FileAttributes]::ReparsePoint
            }).Count) { throw 'Backup target changed; deletion refused.' }
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
Write-Output ("RedundantBytes={0:N1} GiB Mode={1}" -f ($bytes / 1GB), $(if ($Apply) { 'Apply' } else { 'PreviewOnly' }))
