param(
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $workspace 'release\manifest.xml'
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$version = [string]$manifest.Mod.ModVersion
if ($version -notmatch '^\d+\.\d+\.\d+-beta\.\d+$') { throw 'This script packages beta versions only.' }
$sourceDll = Join-Path $workspace 'src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll'
if (!(Test-Path -LiteralPath $sourceDll)) { throw 'Build the Mono Release target first.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $workspace ('out\beta-packages\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Output directory already exists; choose a new directory.' }
$bundle = Join-Path $OutputDirectory 'package'
$content = Join-Path $bundle 'ProbablyStolenTools'
New-Item -ItemType Directory -Path $content | Out-Null
Copy-Item -LiteralPath $sourceDll -Destination (Join-Path $content 'ProbablyStolenTools.dll')
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $content 'manifest.xml')
Copy-Item -LiteralPath (Join-Path $workspace 'docs\beta\README.zh-CN.md') -Destination (Join-Path $bundle 'README.zh-CN.md')
Copy-Item -LiteralPath (Join-Path $workspace 'docs\beta\RELEASE_NOTES.md') -Destination (Join-Path $bundle 'RELEASE_NOTES.md')
$zip = Join-Path $OutputDirectory ('QuickSL-' + $version + '-Mono.zip')
Compress-Archive -Path (Join-Path $bundle '*') -DestinationPath $zip
$hashes = @(Get-ChildItem -LiteralPath $bundle -Recurse -File | ForEach-Object {
    [ordered]@{ File = $_.FullName.Substring($bundle.Length + 1); SHA256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
$commit = & git -C $workspace rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot read source commit.' }
[ordered]@{
    Version = $version
    SourceCommit = $commit
    Runtime = 'Mono full release'
    Files = $hashes
    Archive = [ordered]@{ File = (Split-Path -Leaf $zip); SHA256 = (Get-FileHash -LiteralPath $zip).Hash }
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'checksums.json') -Encoding UTF8
Write-Output "Package=$zip"
Write-Output "Checksums=$(Join-Path $OutputDirectory 'checksums.json')"
Write-Output 'GameDeployment=False'
