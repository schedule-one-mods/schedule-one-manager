$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -eq 'true') { throw 'Maintainer builds are local-only; public CI must use build.ps1 -Installer.' }
$repo = Split-Path $PSScriptRoot -Parent
$dotnet = if (Test-Path (Join-Path $repo 'tools\dotnet\dotnet.exe')) { Join-Path $repo 'tools\dotnet\dotnet.exe' } else { (Get-Command dotnet -ErrorAction Stop).Source }
$output = Join-Path $repo 'artifacts\manager\maintainer\publish'
Push-Location $PSScriptRoot
try {
    & $dotnet run --project 'tests\Manager.Checks\Manager.Checks.csproj' -c Maintainer -p:MaintainerBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Maintainer checks failed.' }
    & $dotnet run --project 'tests\Manager.Desktop.Checks\Manager.Desktop.Checks.csproj' -c Maintainer -p:MaintainerBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Maintainer desktop checks failed.' }
    & $dotnet publish 'src\Manager.Desktop\Manager.Desktop.csproj' -c Maintainer -r win-x64 --self-contained true -p:MaintainerBuild=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Maintainer publish failed.' }
} finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MAINTAINER.md') -Destination $output
Write-Host "Local maintainer tool: $(Join-Path $output 'ScheduleIModManager.Maintainer.exe')"
