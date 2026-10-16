param([string]$GameDir = 'Q:\Games\Schedule I')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -GameDir $GameDir
$dll = Join-Path $PSScriptRoot 'artifacts\LaunderingStatus\LaunderingStatus.dll'
Add-Type -Path (Join-Path $GameDir 'MelonLoader\net6\Mono.Cecil.dll')
$definition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try {
    $modInfo = $definition.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'MelonLoader.MelonInfoAttribute' }
    if (!$modInfo) { throw 'Built DLL has no MelonInfo attribute.' }
    $modName = [string]$modInfo.ConstructorArguments[1].Value
    $version = [string]$modInfo.ConstructorArguments[2].Value
} finally { $definition.Dispose() }
if ($modName -ne 'Businesses' -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Unexpected package identity: $modName $version"
}
$packageSource = Join-Path $PSScriptRoot 'packaging'
$compatibility = Get-Content -LiteralPath (Join-Path $packageSource 'compatibility.json') -Raw | ConvertFrom-Json
$stage = Join-Path $PSScriptRoot ('artifacts\package-staging\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $stage 'Mods') -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $stage 'Mods\LaunderingStatus.dll')
foreach ($file in @('README.md', 'CHANGELOG.md', 'compatibility.json')) {
    Copy-Item -LiteralPath (Join-Path $packageSource $file) -Destination (Join-Path $stage $file)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $stage
$manifest = [ordered]@{
    name = $modName
    version_number = $version
    website_url = ''
    description = 'View business laundering timelines, deposits and countdowns on your phone. IL2CPP; tested on Schedule I 0.4.7f5.'
    dependencies = @($compatibility.thunderstore_dependencies)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding utf8

# Original geometric package artwork; no game assets are copied.
Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new(256, 256)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$dark = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#20364B'))
$light = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#DFEAF2'))
$teal = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#67D0AF'))
try {
    $graphics.Clear($dark.Color)
    $graphics.FillRectangle($teal, 42, 82, 48, 106)
    $graphics.FillRectangle($light, 98, 48, 60, 140)
    $graphics.FillRectangle($teal, 166, 106, 48, 82)
    foreach ($building in @(@(42,82,2,4), @(98,48,3,6), @(166,106,2,3))) {
        for ($row=0; $row -lt $building[3]; $row++) {
            for ($column=0; $column -lt $building[2]; $column++) {
                $graphics.FillRectangle($dark, ($building[0]+9+$column*15), ($building[1]+12+$row*20), 8, 10)
            }
        }
    }
    $graphics.FillRectangle($light, 38, 204, 180, 7)
    $graphics.FillEllipse($teal, 167, 195, 24, 24)
    $bitmap.Save((Join-Path $stage 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $graphics.Dispose(); $bitmap.Dispose(); $dark.Dispose(); $light.Dispose(); $teal.Dispose()
}
$releaseDir = Join-Path $PSScriptRoot 'artifacts\releases'
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
$zip = Join-Path $releaseDir ("Businesses-$version-IL2CPP.zip")
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([System.IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
Write-Host "Package: $zip"
Write-Host "Version read from the built mod: $version"
