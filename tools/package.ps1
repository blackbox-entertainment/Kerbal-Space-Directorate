# Kerbal Space Directorate: builds the mod in Release and zips it for sharing.
# Run from the repo root in PowerShell:   .\tools\package.ps1
# Output: dist\KerbalSpaceDirectorate-<version>.zip  (dist\ is git-ignored)
# The zip holds GameData\KSA (plugins, configs, templates, textures, KSA.version, the example settings file),
# LICENSE and README.md. Your own ksa_settings.cfg (with your API key) is never included.

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

# Version from Directory.Build.props; KSA.version must agree.
[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version }).Version
$avc = Get-Content (Join-Path $repo 'GameData\KSA\KSA.version') -Raw | ConvertFrom-Json
$avcVersion = "$($avc.VERSION.MAJOR).$($avc.VERSION.MINOR).$($avc.VERSION.PATCH)"
if ($version -ne $avcVersion) { throw "Version mismatch: Directory.Build.props says $version, KSA.version says $avcVersion. Fix one and run again." }

Write-Host "Building Kerbal Space Directorate v$version (Release)..."
dotnet build KSA.sln -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
dotnet test KSA.Core.Tests -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed; not packaging." }

$stage = Join-Path $repo 'dist\stage'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$ksa = Join-Path $stage 'GameData\KSA'
New-Item -ItemType Directory -Force (Join-Path $ksa 'Plugins'), (Join-Path $ksa 'PluginData') | Out-Null

$bin = Join-Path $repo 'KSA.KSP\bin\Release\net472'
Copy-Item (Join-Path $bin 'KSA.Core.dll'), (Join-Path $bin 'KSA.KSP.dll') (Join-Path $ksa 'Plugins')
foreach ($dir in 'Configs', 'Templates', 'Textures') {
    Copy-Item -Recurse (Join-Path $repo "GameData\KSA\$dir") $ksa
}
Get-ChildItem -Recurse -Force $ksa -Filter '.gitkeep' | Remove-Item -Force
Copy-Item (Join-Path $repo 'GameData\KSA\KSA.version') $ksa
Copy-Item (Join-Path $repo 'GameData\KSA\PluginData\ksa_settings.example.cfg') (Join-Path $ksa 'PluginData')
Copy-Item (Join-Path $repo 'LICENSE'), (Join-Path $repo 'README.md'), (Join-Path $repo 'CHANGELOG.md') $stage

# Never ship a real key.
if (Get-ChildItem -Recurse $stage -Filter 'ksa_settings.cfg') { throw "ksa_settings.cfg found in the package; stopping." }

$zip = Join-Path $repo "dist\KerbalSpaceDirectorate-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item -Recurse -Force $stage
Write-Host "Done: $zip"
