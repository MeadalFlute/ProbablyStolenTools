param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [Parameter(Mandatory = $true)][string]$ToolDir
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$parent = [IO.Path]::GetFullPath((Join-Path $workspace 'out'))
$root = Join-Path $parent ('player-layout-fixture-' + [guid]::NewGuid().ToString('N'))
$tool = Join-Path $root 'tool'
$fakeGame = Join-Path $root 'game'
$managed = Join-Path $fakeGame 'Probably Stolen_Data\Managed'
$mod = Join-Path $fakeGame 'Mods\ProbablyStolenTools'
function Check([bool]$value, [string]$name) {
    if (!$value) { throw $name }
    Write-Output "PASS $name"
}
try {
    New-Item -ItemType Directory -Path $managed, $mod | Out-Null
    Copy-Item -LiteralPath $ToolDir -Destination $tool -Recurse
    foreach ($name in @('Assembly-CSharp.dll', 'mscorlib.dll', 'System.Core.dll', 'UnityEngine.CoreModule.dll')) {
        Copy-Item -LiteralPath (Join-Path $GameDir "Probably Stolen_Data\Managed\$name") -Destination $managed
    }
    Copy-Item -LiteralPath (Join-Path $workspace 'src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll') -Destination $mod
    $helper = Join-Path $mod 'ProbablyStolenTools.StaticLayouts.dll'
    Copy-Item -LiteralPath (Join-Path $mod 'ProbablyStolenTools.dll') -Destination $helper
    $oldHash = (Get-FileHash -LiteralPath $helper).Hash
    $inputHashes = @(Get-ChildItem -LiteralPath $managed -File | Get-FileHash)
    $wrapper = Join-Path $tool 'Generate-StaticLayouts.ps1'
    & $wrapper -GameDir $fakeGame | Out-Null
    Check ((Get-FileHash -LiteralPath $helper).Hash -eq $oldHash) 'default generation never installs or modifies an existing helper'
    & $wrapper -GameDir $fakeGame -Install | Out-Null
    $expected = (Get-FileHash -LiteralPath (Join-Path $workspace 'src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.StaticLayouts.dll')).Hash
    Check ((Get-FileHash -LiteralPath $helper).Hash -eq $expected) 'self-contained player generator is byte-identical to the production target table'
    Check (@(Get-ChildItem -LiteralPath $tool -Filter previous-helper.dll -Recurse |
        Where-Object { (Get-FileHash -LiteralPath $_.FullName).Hash -eq $oldHash }).Count -eq 1) 'install preserves the old helper outside Mods'
    Check ((Get-FileHash -LiteralPath (Join-Path $mod 'ProbablyStolenTools.dll')).Hash -eq $oldHash) 'main mod DLL is never modified'
    foreach ($entry in $inputHashes) {
        Check ((Get-FileHash -LiteralPath $entry.Path).Hash -eq $entry.Hash) 'game input module remains unchanged'
    }
    $failed = $false
    try { & $wrapper -GameDir $tool | Out-Null } catch { $failed = $true }
    Check $failed 'invalid game directory fails without installation'
    $exe = Join-Path $tool 'StaticSnapshotLayouts.exe'
    $forbidden = Join-Path $managed 'forbidden-helper.dll'
    # The generator deliberately exits non-zero after printing its refusal. Native stderr
    # becomes a terminating NativeCommandError under Stop; capture it as text instead.
    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $refusalOutput = & $exe $fakeGame $forbidden 2>&1 | Out-String
        $refusalCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $savedPreference }
    $refused = $refusalCode -ne 0 -and $refusalOutput -match 'Refusing to write' -and !(Test-Path -LiteralPath $forbidden)
    Check $refused 'generator refuses output in Managed'
}
finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $parent -or
        [IO.Path]::GetFileName($resolved) -notmatch '^player-layout-fixture-[0-9a-f]{32}$') {
        throw 'Invalid cleanup target.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
