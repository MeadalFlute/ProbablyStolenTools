[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GameDir).Path
$managed = Join-Path $game 'Probably Stolen_Data\Managed'
if (!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll')) -or
    !(Test-Path -LiteralPath (Join-Path $managed 'mscorlib.dll'))) {
    throw 'Select the Mono game directory containing Probably Stolen.exe, not Mods or Managed.'
}
foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
    if ($process.Path -eq (Join-Path $game 'Probably Stolen.exe')) {
        throw 'Close the game first. No processes were stopped and no files were changed.'
    }
}
$exe = Join-Path $PSScriptRoot 'StaticSnapshotLayouts.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Extract the entire generator archive first; StaticSnapshotLayouts.exe is missing.' }
$outputDir = Join-Path $PSScriptRoot ('generated-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
New-Item -ItemType Directory -Path $outputDir | Out-Null
$output = Join-Path $outputDir 'ProbablyStolenTools.StaticLayouts.dll'
$inputs = @(Get-ChildItem -LiteralPath $managed -File | Where-Object Name -In @(
    'Assembly-CSharp.dll', 'mscorlib.dll', 'System.Core.dll', 'UnityEngine.CoreModule.dll'))
if ($inputs.Count -ne 4) { throw 'Required Mono modules are missing.' }
$before = @{}
foreach ($file in $inputs) { $before[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash }
& $exe $game $output
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $output)) {
    throw "Generation failed ($LASTEXITCODE). Existing mod/game files were not changed."
}
foreach ($path in $before.Keys) {
    if ((Get-FileHash -LiteralPath $path).Hash -ne $before[$path]) { throw "Game module changed during generation: $path" }
}
$generatedHash = (Get-FileHash -LiteralPath $output).Hash
if ($Install) {
    $mod = Join-Path $game 'Mods\ProbablyStolenTools'
    $target = Join-Path $mod 'ProbablyStolenTools.StaticLayouts.dll'
    if (!(Test-Path -LiteralPath (Join-Path $mod 'ProbablyStolenTools.dll'))) {
        throw "Install Probably Stolen Tools first, or copy the generated helper manually to your actual mod folder: $output"
    }
    foreach ($folder in @($game, (Join-Path $game 'Mods'), $mod)) {
        if ((Get-Item -LiteralPath $folder).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Linked mod directory; automatic installation refused. Generated file was preserved.'
        }
    }
    if (Test-Path -LiteralPath $target) {
        if ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Linked helper file; automatic installation refused.'
        }
        Copy-Item -LiteralPath $target -Destination (Join-Path $outputDir 'previous-helper.dll')
    }
    # Keep a rollback copy outside Mods; the loader scans every DLL in a mod folder.
    Copy-Item -LiteralPath $output -Destination $target -Force
    if ((Get-FileHash -LiteralPath $target).Hash -ne $generatedHash) { throw 'Installed helper hash mismatch.' }
    Write-Output "Installed=$target"
}
Write-Output "Generated=$output"
Write-Output "SHA256=$generatedHash"
Write-Output 'No saves, main mod DLL, game assemblies, manifest, trust or approval records were changed.'
Write-Output 'Restart and re-enable the mod if requested. Generation does not prove compatibility with a new game API.'
