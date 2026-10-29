# Kerbal Space Directorate: builds the mod in Release and zips it for sharing.
# Run from the repo root in PowerShell:   .\tools\package.ps1
# Output: dist\KerbalSpaceDirectorate-<version>.zip  (dist\ is git-ignored)
# The zip holds GameData\KSA (plugins, configs, templates, textures, KSA.version, the example settings file),
# LICENSE and README.md. Your own ksa_settings.cfg (with your API key) is never included.

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

# The build needs KSPDIR (the KSP dev copy it references and deploys to). Fall back to E:\KSP_dev.
if (-not $env:KSPDIR) {
    if (Test-Path 'E:\KSP_dev\KSP_x64_Data\Managed\Assembly-CSharp.dll') { $env:KSPDIR = 'E:\KSP_dev' }
    else { throw "Set KSPDIR to your KSP dev install first, e.g.  `$env:KSPDIR = 'E:\KSP_dev'" }
}
Write-Host "Using KSP at $env:KSPDIR"

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
# Zip with forward slashes in the entry names (2026-10-29): Windows PowerShell's Compress-Archive writes backslashes,
# and CKAN (and anything outside Windows) then cannot find GameData/KSA inside the zip.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -Recurse -File $stage) {
        $entry = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $archive.Dispose() }
Remove-Item -Recurse -Force $stage
Write-Host "Done: $zip"
