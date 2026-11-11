param(
    [string]$GameDir = $env:PROBABLY_STOLEN_FULL_DIR
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($GameDir)) { throw 'Set PROBABLY_STOLEN_FULL_DIR or pass -GameDir.' }
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$workspace = Split-Path -Parent $PSScriptRoot
function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}
Push-Location -LiteralPath $workspace
try {
    $mod = Join-Path $workspace 'src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll'
    $layouts = Join-Path (Split-Path -Parent $mod) 'ProbablyStolenTools.StaticLayouts.dll'
    $fixtureProject = 'tests/CapabilityMigrationProbe/MonoFixture'
    $hostProject = 'tests/CapabilityMigrationProbe/MonoHost'
    $fixture = Join-Path $workspace 'tests\CapabilityMigrationProbe\MonoFixture\bin\Release\netstandard2.1\MonoFixture.dll'
    Invoke-Dotnet -Arguments @('build', 'src/ProbablyStolenTools/ProbablyStolenTools.csproj', '-c', 'Release', "-p:GameDir=$GameDir")
    Invoke-Dotnet -Arguments @('run', '-c', 'Release', '--project', 'tests/CheckpointTests/CheckpointTests.csproj')
    Invoke-Dotnet -Arguments @('run', '-c', 'Release', '--project', 'tests/ReleaseCompatibility', '--',
        $GameDir, $mod, 'release/manifest.xml')
    Invoke-Dotnet -Arguments @('build', $fixtureProject, '-c', 'Release')
    Invoke-Dotnet -Arguments @('build', $hostProject, '-c', 'Release')
    Invoke-Dotnet -Arguments @('run', '--no-build', '-c', 'Release', '--project', $hostProject, '--', $GameDir, $fixture, $layouts)
    Invoke-Dotnet -Arguments @('run', '--no-build', '-c', 'Release', '--project', $hostProject, '--', $GameDir, $fixture, $mod)

    $repro = Join-Path $workspace 'out\static-layouts-repro\ProbablyStolenTools.StaticLayouts.dll'
    Invoke-Dotnet -Arguments @('run', '--no-build', '-c', 'Release', '--project', 'tools/StaticSnapshotLayouts', '--', $GameDir, $repro)
    if ((Get-FileHash -LiteralPath $layouts).Hash -ne (Get-FileHash -LiteralPath $repro).Hash) {
        throw 'Generated target layouts are not reproducible.'
    }
    Write-Output 'PASS byte-identical generated target layouts'

    $negativeDir = Join-Path $workspace 'out\static-layouts-version-rejection'
    $negativeLayouts = Join-Path $negativeDir 'ProbablyStolenTools.StaticLayouts.dll'
    $negativeMod = Join-Path $negativeDir 'ProbablyStolenTools.dll'
    Invoke-Dotnet -Arguments @('run', '--no-build', '-c', 'Release', '--project', 'tools/StaticSnapshotLayouts', '--',
        $GameDir, $negativeLayouts, '--test-mismatched-version')
    Copy-Item -LiteralPath $mod -Destination $negativeMod -Force
    Invoke-Dotnet -Arguments @('run', '--no-build', '-c', 'Release', '--project', $hostProject, '--',
        $GameDir, $fixture, $negativeMod, '--expect-version-rejection')
    Write-Output 'PASS static-layout migration suite; Unity/gameplay UI still requires live acceptance'
}
finally { Pop-Location }
