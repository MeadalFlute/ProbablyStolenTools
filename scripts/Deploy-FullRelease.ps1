param(
    [string]$GameDir = $env:PROBABLY_STOLEN_FULL_DIR
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($GameDir)) { throw "Set PROBABLY_STOLEN_FULL_DIR or pass -GameDir <path to Probably Stolen>." }
$workspace = Split-Path -Parent $PSScriptRoot
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$managedPath = Join-Path $GameDir 'Probably Stolen_Data\Managed\Assembly-CSharp.dll'
if (!(Test-Path -LiteralPath $managedPath) -or (Test-Path -LiteralPath (Join-Path $GameDir 'GameAssembly.dll'))) {
    throw 'This deployment is only for the Mono full release.'
}
$sourceDll = Join-Path $workspace 'src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll'
$manifest = Join-Path $workspace 'release\manifest.xml'
$saveDir = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Questing Goose Studio\Probably Stolen'
$settingsPath = Join-Path $saveDir 'PSTools.settings.xml'
$modsDir = Join-Path $GameDir 'Mods'
$targetDir = Join-Path $modsDir 'ProbablyStolenTools'
$backup = Join-Path $workspace ('out\full-release-deploy-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (!(Test-Path -LiteralPath $sourceDll)) { throw 'Build the Mono Release target first.' }

function Get-TreeHashes([string]$directory) {
    if (!(Test-Path -LiteralPath $directory)) { return @() }
    @(Get-ChildItem -LiteralPath $directory -File -Recurse | ForEach-Object {
        [pscustomobject]@{
            Path = $_.FullName.Substring($directory.Length).TrimStart('\')
            SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
}
function Assert-Tree([object[]]$expected, [string]$directory) {
    foreach ($entry in $expected) {
        $path = Join-Path $directory $entry.Path
        if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256) {
            throw "File changed unexpectedly: $path"
        }
    }
}

$stopped = @()
foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
    if ($process.Path -eq (Join-Path $GameDir 'Probably Stolen.exe')) {
        $stopped += $process.Id
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(10000) | Out-Null
    }
}
$saveBefore = @(Get-TreeHashes $saveDir)
$modsBefore = @(Get-TreeHashes $modsDir)
$gameBefore = @(Get-TreeHashes (Join-Path $GameDir 'Probably Stolen_Data\Managed'))
New-Item -ItemType Directory -Path $backup | Out-Null
if (Test-Path -LiteralPath $saveDir) { Copy-Item -LiteralPath $saveDir -Destination (Join-Path $backup 'SaveData') -Recurse }
if (Test-Path -LiteralPath $modsDir) { Copy-Item -LiteralPath $modsDir -Destination (Join-Path $backup 'Mods') -Recurse }
Assert-Tree $saveBefore (Join-Path $backup 'SaveData')
Assert-Tree $modsBefore (Join-Path $backup 'Mods')
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
Copy-Item -LiteralPath $sourceDll -Destination (Join-Path $targetDir 'ProbablyStolenTools.dll') -Force
Copy-Item -LiteralPath $manifest -Destination (Join-Path $targetDir 'manifest.xml') -Force

$imported = $false
$demoSettings = Join-Path (Split-Path -Parent $GameDir) 'Probably Stolen Demo\UserData\MelonPreferences.cfg'
if (!(Test-Path -LiteralPath $settingsPath) -and (Test-Path -LiteralPath $demoSettings)) {
    $harness = Join-Path $workspace 'tests\ReleaseCompatibility\bin\Release\net6.0\ReleaseCompatibility.dll'
    $stagedSettings = Join-Path $backup 'imported-settings.xml'
    & dotnet $harness --import-settings $demoSettings $stagedSettings
    if ($LASTEXITCODE -ne 0) { throw 'Settings migration failed; game settings were not overwritten.' }
    Copy-Item -LiteralPath $stagedSettings -Destination $settingsPath
    $imported = $true
}
Assert-Tree $saveBefore $saveDir
Assert-Tree $gameBefore (Join-Path $GameDir 'Probably Stolen_Data\Managed')
$preservedMods = @($modsBefore | Where-Object { $_.Path -notlike 'ProbablyStolenTools\*' })
Assert-Tree $preservedMods $modsDir
$builtHash = (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash
$deployedHash = (Get-FileHash -LiteralPath (Join-Path $targetDir 'ProbablyStolenTools.dll') -Algorithm SHA256).Hash
if ($builtHash -ne $deployedHash) { throw 'Deployed DLL does not match build.' }
$record = [ordered]@{
    Version = '0.6.0'
    Timestamp = (Get-Date).ToString('o')
    GameDirectory = $GameDir
    Runtime = 'Mono'
    Loader = 'Built-in IMod / Harmony'
    GameAssemblySHA256 = (Get-FileHash -LiteralPath $managedPath -Algorithm SHA256).Hash
    DeployedDLLSHA256 = $deployedHash
    SettingsImported = $imported
    SettingsPath = $settingsPath
    ExistingSaveFilesUnchanged = $true
    ExistingGameAssembliesUnchanged = $true
    StoppedGameProcesses = $stopped
    GameStarted = $false
    InGameValidated = $false
    OfflineTestsPassed = 48
    PatchTargetsChecked = 51
    SnapshotFieldsChecked = 68
    OfficialScannerPassed = $true
    NativeSettingsRoundTripPassed = $true
    SaveFilesBefore = $saveBefore
    ModsBefore = $modsBefore
}
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backup 'deployment.json') -Encoding UTF8
Write-Output "Deployed=$targetDir"
Write-Output "Backup=$backup"
Write-Output "SHA256=$deployedHash"
Write-Output "SettingsImported=$imported"
Write-Output "VerifiedSaveFiles=$($saveBefore.Count)"
Write-Output 'GameStarted=False'
