[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [Parameter(Mandatory = $true)][string]$BrewingModAssembly,
    [string]$ModAssembly
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$brewing = (Resolve-Path -LiteralPath $BrewingModAssembly).Path
if (!$ModAssembly) { $ModAssembly = Join-Path $workspace 'src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll' }
$ModAssembly = (Resolve-Path -LiteralPath $ModAssembly).Path
$previous = $env:PSTOOLS_TEST_BREWING_DLL
Push-Location -LiteralPath $workspace
try {
    & dotnet build tests/CapabilityMigrationProbe/MonoFixture -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Offline fixture build failed.' }
    $env:PSTOOLS_TEST_BREWING_DLL = $brewing
    & dotnet run -c Release --project tests/CapabilityMigrationProbe/MonoHost -- $GameDir `
        tests/CapabilityMigrationProbe/MonoFixture/bin/Release/netstandard2.1/MonoFixture.dll $ModAssembly
    if ($LASTEXITCODE -ne 0) { throw 'Optional-mod game Mono fixture failed.' }
}
finally {
    $env:PSTOOLS_TEST_BREWING_DLL = $previous
    Pop-Location
}
