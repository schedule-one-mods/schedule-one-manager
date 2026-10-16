param([switch]$Installer)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dotnet = if ($env:GITHUB_ACTIONS -eq 'true') { (Get-Command dotnet -ErrorAction Stop).Source }
          elseif (Test-Path (Join-Path $repo 'tools\dotnet\dotnet.exe')) { Join-Path $repo 'tools\dotnet\dotnet.exe' }
          else { (Get-Command dotnet -ErrorAction Stop).Source }
[xml]$properties = Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Manager version must be major.minor.patch.' }
$parts = $version.Split('.') | ForEach-Object { [int]$_ }
if ($parts[0] -gt 255 -or $parts[1] -gt 255 -or $parts[2] -gt 65535) { throw 'Version exceeds Windows Installer limits.' }
$output = Join-Path $repo 'artifacts\manager\publish'
$releaseDir = Join-Path $repo "artifacts\manager\releases\$version"
foreach ($target in @($output, $releaseDir)) {
    $resolved = [IO.Path]::GetFullPath($target)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath((Join-Path $repo 'artifacts\manager\')), [StringComparison]::OrdinalIgnoreCase)) { throw 'Output escaped artifacts/manager.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
Push-Location $PSScriptRoot
try {
    & $dotnet run --project 'tests\Manager.Checks\Manager.Checks.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Manager checks failed.' }
    & $dotnet publish 'src\Manager.Desktop\Manager.Desktop.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
} finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TESTING.md') -Destination $output
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$assets = Get-Content (Join-Path $PSScriptRoot 'src\Manager.Desktop\obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$runtimePacks = @{}
foreach ($framework in $assets.project.frameworks.Values) {
    foreach ($dependency in $framework.downloadDependencies) {
        $packVersion = $dependency.version.Trim('[',']').Split(',')[0].Trim()
        if ($dependency.name -eq 'Microsoft.NETCore.App.Runtime.win-x64') { $runtimePacks.Core = Join-Path $nugetRoot "microsoft.netcore.app.runtime.win-x64\$packVersion" }
        if ($dependency.name -eq 'Microsoft.WindowsDesktop.App.Runtime.win-x64') { $runtimePacks.Desktop = Join-Path $nugetRoot "microsoft.windowsdesktop.app.runtime.win-x64\$packVersion" }
    }
}
if (!$runtimePacks.Core -or !$runtimePacks.Desktop) { throw 'Could not resolve published runtime license packs.' }
$notices = Join-Path $output 'Notices'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $runtimePacks.Core 'LICENSE.TXT') -Destination (Join-Path $notices 'dotnet-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $runtimePacks.Core 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $notices 'dotnet-THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $runtimePacks.Desktop 'LICENSE') -Destination (Join-Path $notices 'windowsdesktop-LICENSE.txt')
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
$zip = Join-Path $releaseDir "ScheduleIModManager-$version-win-x64.zip"
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip
if ($Installer) {
    $toolPath = Join-Path $repo 'tools\wix'
    $wix = Join-Path $toolPath 'wix.exe'
    if (!(Test-Path $wix)) {
        & $dotnet tool install wix --version 6.0.2 --tool-path $toolPath --allow-roll-forward
        if ($LASTEXITCODE -ne 0) { throw 'WiX tool install failed.' }
    }
    & $wix build (Join-Path $PSScriptRoot 'installer\Manager.wxs') -arch x64 -d "PublishDir=$output" -d "AppVersion=$version" -o (Join-Path $releaseDir "ScheduleIModManager-$version-x64.msi")
    if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
    $symbols = Join-Path $releaseDir "ScheduleIModManager-$version-x64.wixpdb"
    if (Test-Path -LiteralPath $symbols) { Move-Item -LiteralPath $symbols -Destination (Join-Path (Split-Path $output -Parent) 'manager.wixpdb') -Force }
}
Get-ChildItem -LiteralPath $releaseDir -File | Where-Object { $_.Extension -in '.zip','.msi' } | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($_.FullName + '.sha256', "$hash  $($_.Name)`n")
}
& (Join-Path $PSScriptRoot 'verify-release.ps1') -ReleaseDir $releaseDir -RequireInstaller:$Installer
if ($env:GITHUB_OUTPUT) {
    "version=$version" >> $env:GITHUB_OUTPUT
    "release_dir=$releaseDir" >> $env:GITHUB_OUTPUT
}
Write-Host "Manager $version published: $output"
Write-Host "Release packages: $releaseDir"
