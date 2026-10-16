$ErrorActionPreference = 'Stop'
$app = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\manager\publish\ScheduleIModManager.exe'
if (!(Test-Path $app)) { throw 'Run manager/build.ps1 first.' }
Start-Process -FilePath $app -WorkingDirectory (Split-Path $app -Parent)
