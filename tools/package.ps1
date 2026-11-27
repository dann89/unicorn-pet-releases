# Builds the install package: dist\UnicornPet-Setup (exe, libraries, install scripts) and dist\UnicornPet-portable-<version>.zip.
# Uploads nothing and signs nothing; use publish.ps1 for update releases.
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$FullBuild,
    [string]$OutDir
)
$ErrorActionPreference = 'Stop'
if (-not $FullBuild) {
    throw 'This script builds the FULL (frozen) package with the self-updating launcher, install scripts and extra files. It is NOT the work-safe package. Use tools\package-worksafe.ps1 instead, or pass -FullBuild if you really want the full build.'
}
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Version must be exactly three numeric parts (x.y.z), got '$Version'" }
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot '..\dist' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ver = ([version]$Version).ToString()

dotnet build (Join-Path $repo 'src\UnicornPet.App\UnicornPet.App.csproj') -c Release "-p:Version=$ver" "-p:AssemblyVersion=$ver.0" "-p:FileVersion=$ver.0" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$bin = Join-Path $repo 'src\UnicornPet.App\bin\Release\net48'

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$setup = Join-Path $OutDir 'UnicornPet-Setup'
if (Test-Path $setup) { Remove-Item $setup -Recurse -Force }
New-Item -ItemType Directory $setup | Out-Null

Get-ChildItem $bin -File | Where-Object { $_.Extension -in '.exe', '.dll', '.config' } | Copy-Item -Destination $setup
foreach ($f in 'install.ps1', 'install.cmd', 'uninstall.ps1') { Copy-Item (Join-Path $PSScriptRoot $f) $setup }
@"
Unicorn Pet $ver

To install: double-click install.cmd. No administrator rights are needed.
To remove: run uninstall.ps1 (add -Purge to also delete settings).
"@ | Set-Content (Join-Path $setup 'README.txt') -Encoding ASCII

$portable = Join-Path $OutDir "UnicornPet-portable-$ver.zip"
if (Test-Path $portable) { Remove-Item $portable -Force }
$pstage = Join-Path $OutDir "portable-stage-$ver"
if (Test-Path $pstage) { Remove-Item $pstage -Recurse -Force }
New-Item -ItemType Directory $pstage | Out-Null
Get-ChildItem $bin -File | Where-Object { $_.Extension -in '.exe', '.dll', '.config' } | Copy-Item -Destination $pstage
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($pstage, $portable, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $pstage -Recurse -Force

Write-Host ''
Write-Host "Setup folder: $setup"
Write-Host "Portable zip: $portable"

