param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [Parameter(Mandatory = $true)][string]$OtherLayouts,
    [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$OtherLayouts = (Resolve-Path -LiteralPath $OtherLayouts).Path
if (!$OutputRoot) { $OutputRoot = Join-Path $workspace ('out\merged-layout-tests-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
if (Test-Path -LiteralPath $OutputRoot) { throw 'Use a new output directory.' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
function Run([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($Arguments -join ' ')" }
}
function Refuse([string[]]$Arguments, [string]$Message) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $text = & dotnet @Arguments 2>&1 | Out-String
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $saved }
    if ($code -eq 0 -or $text -notmatch $Message) { throw "Expected refusal missing: $text" }
    Write-Output "PASS refusal: $Message"
}
Push-Location -LiteralPath $workspace
try {
    $hashBefore = (Get-FileHash -LiteralPath $OtherLayouts).Hash
    Run -Arguments @('build', 'src/ProbablyStolenTools/ProbablyStolenTools.csproj', '-c', 'Release', "-p:GameDir=$GameDir")
    Run -Arguments @('build', 'tests/CapabilityMigrationProbe/MonoFixture', '-c', 'Release')
    Run -Arguments @('build', 'tests/CapabilityMigrationProbe/MonoHost', '-c', 'Release')
    Run -Arguments @('build', 'tests/MergedLayoutFixture', '-c', 'Release')
    Run -Arguments @('build', 'tools/MergeStaticLayouts', '-c', 'Release')
    $current = Join-Path $workspace 'src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.StaticLayouts.dll'
    $main = Join-Path $workspace 'src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll'
    $fixture = Join-Path $workspace 'tests\CapabilityMigrationProbe\MonoFixture\bin\Release\netstandard2.1\MonoFixture.dll'
    $merge = @('run', '--no-build', '-c', 'Release', '--project', 'tools/MergeStaticLayouts', '--')
    $mutate = @('run', '--no-build', '-c', 'Release', '--project', 'tests/MergedLayoutFixture', '--')
    $hostArgs = @('run', '--no-build', '-c', 'Release', '--project', 'tests/CapabilityMigrationProbe/MonoHost', '--', $GameDir, $fixture)
    foreach ($order in @('current-first', 'current-last')) {
        $dir = Join-Path $OutputRoot $order
        $helper = Join-Path $dir 'ProbablyStolenTools.StaticLayouts.dll'
        $inputs = if ($order -eq 'current-first') { @($current, $OtherLayouts) } else { @($OtherLayouts, $current) }
        Run -Arguments ($merge + @($helper) + $inputs)
        Copy-Item -LiteralPath $main -Destination $dir
        Run -Arguments ($hostArgs + @($helper))
        Run -Arguments ($hostArgs + @((Join-Path $dir 'ProbablyStolenTools.dll')))
        $repro = Join-Path $OutputRoot ($order + '-repro.dll')
        Run -Arguments ($merge + @($repro) + $inputs)
        if ((Get-FileHash -LiteralPath $helper).Hash -ne (Get-FileHash -LiteralPath $repro).Hash) {
            throw 'Merged output is not byte-reproducible.'
        }
        Write-Output "PASS byte-identical merge: $order"
    }
    $merged = Join-Path $OutputRoot 'current-first\ProbablyStolenTools.StaticLayouts.dll'
    Run -Arguments @('run', '-c', 'Release', '--project', 'tests/ReleaseCompatibility', '--',
        $GameDir, (Join-Path $OutputRoot 'current-first\ProbablyStolenTools.dll'), 'release/universal/manifest.xml')
    $duplicate = Join-Path $OutputRoot 'duplicate.dll'
    Refuse -Arguments ($merge + @($duplicate, $current, $current)) -Message 'ambiguous game fingerprint'
    if (Test-Path -LiteralPath $duplicate) { throw 'Duplicate input wrote an output.' }
    $altered = Join-Path $OutputRoot 'altered.dll'
    Run -Arguments ($mutate + @($current, $altered, 'alter'))
    Refuse -Arguments ($merge + @('--verify-profile', $merged, $altered)) -Message 'changed registry IL or field factory IL'
    $poison = Join-Path $OutputRoot 'poison.dll'
    Run -Arguments ($mutate + @($OtherLayouts, $poison, 'poison'))
    $isolated = Join-Path $OutputRoot 'poisoned-inactive\ProbablyStolenTools.StaticLayouts.dll'
    Run -Arguments ($merge + @($isolated, $poison, $current))
    Run -Arguments ($hostArgs + @($isolated))
    Write-Output 'PASS inactive missing type tokens are not resolved by game Mono'
    $unknown1 = Join-Path $OutputRoot 'unknown1.dll'
    $unknown2 = Join-Path $OutputRoot 'unknown2.dll'
    Run -Arguments ($mutate + @($current, $unknown1, 'unknown'))
    Run -Arguments ($mutate + @($poison, $unknown2, 'unknown'))
    $unknown = Join-Path $OutputRoot 'unknown\ProbablyStolenTools.StaticLayouts.dll'
    Run -Arguments ($merge + @($unknown, $unknown1, $unknown2))
    $unknownMain = Join-Path (Split-Path -Parent $unknown) 'ProbablyStolenTools.dll'
    Copy-Item -LiteralPath $main -Destination $unknownMain
    Run -Arguments ($hostArgs + @($unknownMain, '--expect-version-rejection'))
    if ((Get-FileHash -LiteralPath $OtherLayouts).Hash -ne $hashBefore) { throw 'Source helper changed.' }
    Write-Output 'PASS merged profiles: both selector positions, exact preservation, reproducibility, missing-token isolation, unknown-version rejection and input immutability'
    Write-Output 'Only the installed game branch was executed; the other branch still needs its own real assemblies/live acceptance.'
}
finally { Pop-Location }
