# Builds a release locally: UnicornPet-<version>.zip + manifest.json in dist\. Uploads nothing.
# Upload both files as assets of a GitHub release tagged v<version> in the releases repository.
# Needs the signing key to exist already (create it once with: sign-release.ps1 -InitKey).
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$KeyDir,
    [string]$OutDir,
    [string]$BaseUrl = 'https://github.com/dann89/unicorn-pet-releases/releases/download'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Version must be exactly three numeric parts (x.y.z), got '$Version'" }
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot '..\dist' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ver = ([version]$Version).ToString()
$keyArgs = @{}
if ($KeyDir) { $keyArgs['KeyDir'] = $KeyDir }

# fail early, before building, if the key is missing or does not match what the app trusts
& (Join-Path $PSScriptRoot 'sign-release.ps1') @keyArgs -CheckOnly

dotnet build (Join-Path $repo 'src\UnicornPet.App\UnicornPet.App.csproj') -c Release "-p:Version=$ver" "-p:AssemblyVersion=$ver.0" "-p:FileVersion=$ver.0" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$bin = Join-Path $repo 'src\UnicornPet.App\bin\Release\net48'

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$stage = Join-Path $OutDir "stage-$ver"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
Get-ChildItem $bin -File | Where-Object { $_.Extension -in '.exe', '.dll', '.config' } | Copy-Item -Destination $stage

$zip = Join-Path $OutDir "UnicornPet-$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $stage -Recurse -Force

$sha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$url = "$BaseUrl/v$ver/UnicornPet-$ver.zip"
$manifest = Join-Path $OutDir 'manifest.json'
& (Join-Path $PSScriptRoot 'sign-release.ps1') @keyArgs -Version $ver -Url $url -Sha256 $sha -Out $manifest

# verify with the application's own verifier (embedded public key), not just our script's self-check
$exe = Join-Path $bin 'UnicornPet.exe'
$check = Start-Process $exe -ArgumentList '--verify-manifest', ('"' + $manifest + '"') -Wait -PassThru
if ($check.ExitCode -ne 0) { Remove-Item $manifest -Force; throw 'The built application rejected the new manifest (verify-manifest failed). Nothing to publish.' }
Write-Host 'Manifest verified by the application: OK'

Write-Host ''
Write-Host "Done. Upload these two files to a release tagged v$ver :"
Write-Host "  $zip"
Write-Host "  $manifest"
