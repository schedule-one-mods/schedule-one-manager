param(
    [string]$GameDir = 'Q:\Games\Schedule I',
    [string]$SourceSave = "$env:USERPROFILE\AppData\LocalLow\TVGS\Schedule I\Saves\76561199077072515\SaveGame_5"
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name 'Schedule I' -ErrorAction SilentlyContinue) {
    throw 'Close Schedule I before starting the isolated playtest.'
}
if (!(Test-Path -LiteralPath (Join-Path $SourceSave 'Game.json'))) {
    throw "Not a save directory: $SourceSave"
}
& (Join-Path $PSScriptRoot 'build.ps1') -GameDir $GameDir -Deploy
$testRoot = Join-Path $PSScriptRoot ('artifacts\playtest\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$testSave = Join-Path $testRoot (Split-Path $SourceSave -Leaf)
Copy-Item -LiteralPath $SourceSave -Destination $testSave -Recurse
Start-Process -FilePath (Join-Path $GameDir 'Schedule I.exe') -WorkingDirectory $GameDir -WindowStyle Normal `
    -ArgumentList '--quitfix','--laundry-diagnostics','--laundry-test-save',('"' + $testSave + '"')
Write-Host "Testing a separate save copy: $testSave"
