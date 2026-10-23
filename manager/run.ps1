param([switch]$Maintainer)
$ErrorActionPreference = 'Stop'
$relative = if ($Maintainer) { 'artifacts\manager\maintainer\publish\ScheduleIModManager.Maintainer.exe' } else { 'artifacts\manager\publish\ScheduleIModManager.exe' }
$app = Join-Path (Split-Path $PSScriptRoot -Parent) $relative
if (!(Test-Path $app)) { throw 'Run manager/build.ps1 first.' }
Start-Process -FilePath $app -WorkingDirectory (Split-Path $app -Parent)
