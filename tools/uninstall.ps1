# Removes Unicorn Pet for the current user. Add -Purge to also delete settings and messages in %APPDATA%\UnicornPet.
param([switch]$Purge)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'UnicornPet'
Get-Process UnicornPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$root*" } | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'UnicornPet' -ErrorAction SilentlyContinue
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
if ($Purge) { $r = Join-Path $env:APPDATA 'UnicornPet'; if (Test-Path $r) { Remove-Item $r -Recurse -Force } }
Write-Host 'Unicorn Pet removed.'
