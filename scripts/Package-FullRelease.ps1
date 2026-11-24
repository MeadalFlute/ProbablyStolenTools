[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [string]$OutputRoot,
    [string[]]$AdditionalLayoutPaths = @()
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$manifest = (Resolve-Path -LiteralPath $ManifestPath).Path
$revision = & git -C $workspace rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source revision.' }
$status = @(& git -C $workspace status --porcelain)
if ($LASTEXITCODE -ne 0 -or $status.Count -gt 0) { throw 'Commit source changes before packaging a release.' }
if (!$OutputRoot) { $OutputRoot = Join-Path $workspace ('out\release-candidate-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'Output directory already exists.' }
$additional = @($AdditionalLayoutPaths | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
$additionalHashes = @($additional | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash })
& dotnet build (Join-Path $workspace 'src\ProbablyStolenTools\ProbablyStolenTools.csproj') -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Clean-source release build failed.' }
$built = Join-Path $workspace 'src\ProbablyStolenTools\bin\Release\Mono'
$helper = Join-Path $built 'ProbablyStolenTools.StaticLayouts.dll'
$main = Join-Path $built 'ProbablyStolenTools.dll'
[xml]$definition = Get-Content -LiteralPath $manifest -Raw
if (@($definition.Mod.GameVersions.Version).Count -ne 1 + $additional.Count) {
    throw 'Declare exactly one game version per supplied static layout profile.'
}
$names = @($definition.Mod.Assemblies.Assembly)
if ($names.Count -ne 2 -or 'ProbablyStolenTools.dll' -notin $names -or 'ProbablyStolenTools.StaticLayouts.dll' -notin $names) {
    throw 'The candidate must contain exactly the main DLL and one static layout helper.'
}
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
$verified = Join-Path $OutputRoot 'verify\ProbablyStolenTools.StaticLayouts.dll'
& dotnet run -c Release --project (Join-Path $workspace 'tools\StaticSnapshotLayouts') -- $GameDir $verified
if ($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $verified).Hash -ne (Get-FileHash -LiteralPath $helper).Hash) {
    throw 'Built helper is not reproducible for the selected game branch.'
}
$package = Join-Path $OutputRoot 'ProbablyStolenTools'
New-Item -ItemType Directory -Path $package | Out-Null
foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $built $name) -Destination $package }
if ($additional.Count -gt 0) {
    $merged = Join-Path $package 'ProbablyStolenTools.StaticLayouts.dll'
    & dotnet run -c Release --project (Join-Path $workspace 'tools\MergeStaticLayouts') -- $merged $helper @additional
    if ($LASTEXITCODE -ne 0) { throw 'Offline static layout merge failed.' }
    # Provenance belongs outside the mod directory, not in the shipped package.
    Move-Item -LiteralPath ($merged + '.profiles.json') -Destination (Join-Path $OutputRoot 'layout-profiles.json')
}
for ($i = 0; $i -lt $additional.Count; $i++) {
    if ((Get-FileHash -LiteralPath $additional[$i]).Hash -ne $additionalHashes[$i]) {
        throw 'An additional static layout input changed during packaging.'
    }
}
# Scan the actual packaged DLLs, including all inactive version methods.
& dotnet run -c Release --project (Join-Path $workspace 'tests\ReleaseCompatibility') -- $GameDir (Join-Path $package 'ProbablyStolenTools.dll') $manifest
if ($LASTEXITCODE -ne 0) { throw 'Release contracts or capability scan failed.' }
Copy-Item -LiteralPath $manifest -Destination (Join-Path $package 'manifest.xml')
Copy-Item -LiteralPath (Join-Path $workspace 'release\preview.png') -Destination $package
$finalRevision = & git -C $workspace rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $finalRevision -ne $revision) { throw 'Source revision changed during packaging.' }
$status = @(& git -C $workspace status --porcelain)
if ($LASTEXITCODE -ne 0 -or $status.Count -gt 0) { throw 'Source changed during packaging.' }
$zip = Join-Path $OutputRoot 'ProbablyStolenTools.zip'
Compress-Archive -LiteralPath $package -DestinationPath $zip
$files = @(Get-ChildItem -LiteralPath $package -File | ForEach-Object {
    [ordered]@{ Name = $_.Name; Bytes = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
[ordered]@{
    BuildCommit = $revision
    BuildDirty = $false
    Version = [string]$definition.Mod.ModVersion
    GameVersions = @($definition.Mod.GameVersions.Version)
    GameAssemblySHA256 = (Get-FileHash -LiteralPath (Join-Path $GameDir 'Probably Stolen_Data\Managed\Assembly-CSharp.dll')).Hash
    AdditionalLayouts = @($additional | ForEach-Object {
        [ordered]@{ File = [IO.Path]::GetFileName($_); SHA256 = (Get-FileHash -LiteralPath $_).Hash }
    })
    Files = $files
    ZipBytes = (Get-Item -LiteralPath $zip).Length
    ZipSHA256 = (Get-FileHash -LiteralPath $zip).Hash
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputRoot 'provenance.json') -Encoding UTF8
Write-Output "Package=$zip"
Write-Output "Bytes=$((Get-Item -LiteralPath $zip).Length)"
Write-Output 'Exactly one helper (single or merged profiles); no game assemblies, generator or runtime bundled.'
