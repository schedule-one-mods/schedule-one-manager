param([Parameter(Mandatory)][string]$ReleaseDir, [switch]$RequireInstaller)
$ErrorActionPreference = 'Stop'
[xml]$properties = Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
$zipName = "ScheduleIModManager-$version-win-x64.zip"
$expected = @($zipName, "$zipName.sha256")
if ($RequireInstaller) { $expected += "ScheduleIModManager-$version-x64.msi", "ScheduleIModManager-$version-x64.msi.sha256" }
$actual = @(Get-ChildItem -LiteralPath $ReleaseDir -File | Select-Object -ExpandProperty Name)
if (Compare-Object $expected $actual) { throw 'Unexpected or missing release assets.' }
foreach ($name in $expected | Where-Object { $_ -notlike '*.sha256' }) {
    $file = Join-Path $ReleaseDir $name
    $hash = (Get-Content -LiteralPath "$file.sha256" -Raw).Split(' ')[0]
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $hash) { throw "Checksum mismatch: $name" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $ReleaseDir $zipName))
try {
    $names = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
    foreach ($name in $names) {
        if ($name -notin @('ScheduleIModManager.exe','README.md','TESTING.md','Notices/dotnet-LICENSE.txt','Notices/dotnet-THIRD-PARTY-NOTICES.txt','Notices/windowsdesktop-LICENSE.txt')) { throw "Unexpected portable content: $name" }
    }
    if ($names.Count -ne 6 -or 'ScheduleIModManager.exe' -notin $names) { throw 'Incomplete portable package.' }
    foreach ($entry in $zip.Entries) { $stream = $entry.Open(); try { $stream.CopyTo([IO.Stream]::Null) } finally { $stream.Dispose() } }
} finally { $zip.Dispose() }
Write-Host "PASS: $version release assets, checksums and manager-only contents."
