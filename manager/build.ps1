param(
    [string]$GameDir = 'Q:\Games\Schedule I',
    [switch]$SkipModBuild,
    [switch]$Installer
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $repo 'tools\dotnet\dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
if (!$SkipModBuild) {
    & (Join-Path $repo 'package.ps1') -GameDir $GameDir
    if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
}
$package = Get-ChildItem (Join-Path $repo 'artifacts\releases\Businesses-*-IL2CPP.zip') | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (!$package) { throw 'Build the Businesses package first using package.ps1.' }
$seeds = Join-Path $PSScriptRoot 'SeedPackages'
New-Item -ItemType Directory -Path $seeds -Force | Out-Null
Get-ChildItem -LiteralPath $seeds -Filter '*.zip' | Remove-Item
Copy-Item -LiteralPath $package.FullName -Destination $seeds
$output = Join-Path $repo 'artifacts\manager\publish'
if (Test-Path $output) {
    $resolved = [IO.Path]::GetFullPath($output)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath((Join-Path $repo 'artifacts\manager\')), [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish path escaped artifacts/manager.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
& $dotnet run --project (Join-Path $PSScriptRoot 'tests\Manager.Checks\Manager.Checks.csproj') -c Release -- $package.FullName $GameDir (Join-Path $repo 'tools\MelonLoader.x64.0.7.3.zip')
if ($LASTEXITCODE -ne 0) { throw 'Manager checks failed.' }
& $dotnet publish (Join-Path $PSScriptRoot 'src\Manager.Desktop\Manager.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TESTING.md') -Destination $output
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$runtimePack = Get-ChildItem (Join-Path $nugetRoot 'microsoft.netcore.app.runtime.win-x64') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$desktopPack = Get-ChildItem (Join-Path $nugetRoot 'microsoft.windowsdesktop.app.runtime.win-x64') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$notices = Join-Path $output 'Notices'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $runtimePack.FullName 'LICENSE.TXT') -Destination (Join-Path $notices 'dotnet-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $runtimePack.FullName 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $notices 'dotnet-THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $desktopPack.FullName 'LICENSE') -Destination (Join-Path $notices 'windowsdesktop-LICENSE.txt')
$releaseDir = Join-Path $repo 'artifacts\manager\releases'
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
$zip = Join-Path $releaseDir 'ScheduleIModManager-0.1.0-win-x64.zip'
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -Force
if ($Installer) {
    $toolPath = Join-Path $repo 'tools\wix'
    $wix = Join-Path $toolPath 'wix.exe'
    if (!(Test-Path $wix)) {
        & $dotnet tool install wix --version 6.0.2 --tool-path $toolPath
        if ($LASTEXITCODE -ne 0) { throw 'WiX tool install failed.' }
    }
    & $wix build (Join-Path $PSScriptRoot 'installer\Manager.wxs') -arch x64 -d "PublishDir=$output" -o (Join-Path $releaseDir 'ScheduleIModManager-0.1.0-x64.msi')
    if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
}
Get-ChildItem -LiteralPath $releaseDir -File | Where-Object { $_.Extension -in '.zip','.msi' } | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($_.FullName + '.sha256', "$hash  $($_.Name)`n")
}
Write-Host "Manager published: $output"
Write-Host "Release packages: $releaseDir"
