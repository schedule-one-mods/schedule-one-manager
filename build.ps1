param(
    [string]$GameDir = 'Q:\Games\Schedule I',
    [switch]$Deploy
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$sdk = Join-Path $projectRoot 'tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location (Join-Path $projectRoot 'src\LaunderingStatus')
try {
    & $sdk build . -c Release --nologo "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
} finally { Pop-Location }
$output = Join-Path $projectRoot 'artifacts\LaunderingStatus'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\LaunderingStatus\bin\Release\net6.0\LaunderingStatus.dll') -Destination $output
if ($Deploy) {
    if (Get-Process -Name 'Schedule I' -ErrorAction SilentlyContinue) {
        throw 'Close Schedule I before deploying the DLL.'
    }
    New-Item -ItemType Directory -Path (Join-Path $GameDir 'Mods') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $output 'LaunderingStatus.dll') -Destination (Join-Path $GameDir 'Mods\LaunderingStatus.dll')
}
Write-Host "Built: $output\LaunderingStatus.dll"
