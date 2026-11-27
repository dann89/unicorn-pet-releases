# Per-user install of Unicorn Pet. No admin rights needed; touches only %LOCALAPPDATA%\UnicornPet and HKCU Run.
# Run from the folder that contains UnicornPet.exe (the setup folder), or pass -Source.
param([string]$Source, [switch]$NoStart)
$ErrorActionPreference = 'Stop'
if (-not $Source) { $Source = $PSScriptRoot }
if (-not (Test-Path (Join-Path $Source 'UnicornPet.exe'))) {
    $alt = Join-Path $PSScriptRoot '..\src\UnicornPet.App\bin\Release\net48'
    if (Test-Path (Join-Path $alt 'UnicornPet.exe')) { $Source = (Resolve-Path $alt).Path } else { throw "UnicornPet.exe not found in $Source" }
}
$root = Join-Path $env:LOCALAPPDATA 'UnicornPet'
$ver = (Get-Item (Join-Path $Source 'UnicornPet.exe')).VersionInfo.FileVersion
if (-not $ver) { $ver = '0.0.0' }
$verDir = Join-Path $root "versions\$ver"

Get-Process UnicornPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$root*" } | Stop-Process -Force
Start-Sleep -Milliseconds 500

$files = Get-ChildItem $Source -File | Where-Object { $_.Extension -in '.exe', '.dll', '.config' }
New-Item -ItemType Directory -Force $root, $verDir | Out-Null
foreach ($f in $files) {
    Copy-Item $f.FullName (Join-Path $verDir $f.Name) -Force
    Copy-Item $f.FullName (Join-Path $root $f.Name) -Force   # stable launcher copy, target of the Run entry
}
$exe = Join-Path $root 'UnicornPet.exe'
# Reset the launcher's update state for this version (no pending update, no rollback trial). The app owns the state file.
$init = Start-Process $exe -ArgumentList '--init-state', $ver -Wait -PassThru
if ($init.ExitCode -ne 0) { throw "Could not initialise update state (exit code $($init.ExitCode)); is '$ver' a 3 or 4 part version?" }

$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-ItemProperty -Path $run -Name 'UnicornPet' -Value ('"{0}"' -f $exe) -PropertyType String -Force | Out-Null
Write-Host "Installed Unicorn Pet $ver to $root"
if (-not $NoStart) { Start-Process $exe }
