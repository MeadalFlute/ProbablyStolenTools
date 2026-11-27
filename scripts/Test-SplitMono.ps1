param(
    [string]$GameDir = $env:PROBABLY_STOLEN_FULL_DIR,
    [string]$ToolboxDll,
    [switch]$QuickOnly
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$GameDir) { throw 'Pass -GameDir or set PROBABLY_STOLEN_FULL_DIR.' }
if (!$QuickOnly -and (!$ToolboxDll -or !(Test-Path -LiteralPath $ToolboxDll))) { throw 'Pass -ToolboxDll or use -QuickOnly.' }
$previous = $env:SHIFT_TOOLBOX_TEST_DLL
try {
    if ($QuickOnly) { $env:SHIFT_TOOLBOX_TEST_DLL = $null }
    else { $env:SHIFT_TOOLBOX_TEST_DLL = (Resolve-Path -LiteralPath $ToolboxDll).Path }
    & dotnet (Join-Path $root 'tests\CapabilityMigrationProbe\MonoHost\bin\Release\net8.0\MonoHost.dll') $GameDir `
        (Join-Path $root 'tests\CapabilityMigrationProbe\MonoFixture\bin\Release\netstandard2.1\MonoFixture.dll') `
        (Join-Path $root 'src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Game Mono fixture failed.' }
} finally { $env:SHIFT_TOOLBOX_TEST_DLL = $previous }
