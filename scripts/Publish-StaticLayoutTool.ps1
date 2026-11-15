[CmdletBinding()]
param([string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
if (!$OutputRoot) { $OutputRoot = Join-Path $workspace ('out\static-layout-tool-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'Output directory already exists; refusing to overwrite a previous package.' }
$package = Join-Path $OutputRoot 'ProbablyStolenTools-StaticLayoutGenerator'
New-Item -ItemType Directory -Path $package | Out-Null
& dotnet publish (Join-Path $workspace 'tools\StaticSnapshotLayouts\StaticSnapshotLayouts.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:PlayerLayoutTool=true -o $package
if ($LASTEXITCODE -ne 0) { throw "Tool publish failed ($LASTEXITCODE)." }
foreach ($name in @('Generate-StaticLayouts.ps1', 'PLAYER-README.md', 'LICENSE.Cecil.txt')) {
    Copy-Item -LiteralPath (Join-Path $workspace "tools\StaticSnapshotLayouts\$name") -Destination $package
}
# Include matching source, not copyrighted game assemblies or player data.
$source = Join-Path $package 'source'
New-Item -ItemType Directory -Path $source | Out-Null
foreach ($name in @('Program.cs', 'StaticSnapshotLayouts.csproj')) {
    Copy-Item -LiteralPath (Join-Path $workspace "tools\StaticSnapshotLayouts\$name") -Destination $source
}
Copy-Item -LiteralPath (Join-Path $workspace 'src\ProbablyStolenTools\SnapshotTraversalBoundary.cs') -Destination $source
$assets = Get-Content -LiteralPath (Join-Path $workspace 'tools\StaticSnapshotLayouts\obj\project.assets.json') -Raw | ConvertFrom-Json
[xml]$project = Get-Content -LiteralPath (Join-Path $workspace 'tools\StaticSnapshotLayouts\StaticSnapshotLayouts.csproj') -Raw
$framework = [string]$project.Project.PropertyGroup.TargetFramework
$deps = Get-Content -LiteralPath (Join-Path $workspace "tools\StaticSnapshotLayouts\obj\Release\$framework\win-x64\StaticSnapshotLayouts.deps.json") -Raw | ConvertFrom-Json
$runtime = @($deps.libraries.PSObject.Properties | Where-Object { $_.Name -match '^runtimepack.Microsoft.NETCore.App.Runtime.win-x64/' })
if ($runtime.Count -ne 1) { throw 'Cannot identify the bundled runtime for license packaging.' }
$runtimePath = $runtime[0].Name.Substring('runtimepack.'.Length).ToLowerInvariant()
$runtimeRoot = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
    $candidate = Join-Path $_ $runtimePath
    if (Test-Path -LiteralPath (Join-Path $candidate 'LICENSE.TXT')) { $candidate }
}) | Select-Object -First 1
if (!$runtimeRoot) { throw 'Bundled runtime license is missing; package not released.' }
Copy-Item -LiteralPath (Join-Path $runtimeRoot 'LICENSE.TXT') -Destination (Join-Path $package 'LICENSE.Runtime.txt')
Copy-Item -LiteralPath (Join-Path $runtimeRoot 'THIRD-PARTY-NOTICES.TXT') -Destination $package
$hashes = @(Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object {
    [ordered]@{ Path = $_.FullName.Substring($package.Length + 1); SHA256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
$revision = & git -C $workspace rev-parse HEAD
$dirty = @(& git -C $workspace status --porcelain).Count -gt 0
[ordered]@{ BuildCommit = $revision; BuildDirty = $dirty; Files = $hashes } | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $package 'package.json') -Encoding UTF8
$zip = Join-Path $OutputRoot 'ProbablyStolenTools-StaticLayoutGenerator-win-x64.zip'
Compress-Archive -LiteralPath $package -DestinationPath $zip
Write-Output "Package=$zip"
Write-Output "SHA256=$((Get-FileHash -LiteralPath $zip).Hash)"
