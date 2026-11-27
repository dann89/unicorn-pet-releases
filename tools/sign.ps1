# Optional code-signing hook for the work-safe exe (build-time only, not shipped).
# Signs UnicornPet.exe with signtool plus an RFC 3161 timestamp when a certificate is supplied through the environment:
#   UNICORNPET_SIGN_THUMBPRINT   thumbprint of a code-signing certificate in the current user's certificate store, or
#   UNICORNPET_SIGN_PFX          path to a .pfx file kept OUTSIDE this repository (its password goes in UNICORNPET_SIGN_PFX_PASSWORD)
# If neither is set it signs nothing and prints a loud UNSIGNED warning. It never creates, fetches or stores a certificate.
# Returns $true when the file was signed and verified, $false otherwise.
param(
    [Parameter(Mandatory)][string]$Exe,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Exe = (Resolve-Path $Exe).Path
$thumb = $env:UNICORNPET_SIGN_THUMBPRINT
$pfx = $env:UNICORNPET_SIGN_PFX

if ([string]::IsNullOrWhiteSpace($thumb) -and [string]::IsNullOrWhiteSpace($pfx)) {
    Write-Host ''
    Write-Host '*****************************************************************' -ForegroundColor Yellow
    Write-Host '*  UNSIGNED: no certificate supplied, so UnicornPet.exe is NOT   *' -ForegroundColor Yellow
    Write-Host '*  signed. Security software is more likely to block an         *' -ForegroundColor Yellow
    Write-Host '*  unsigned file from an unknown publisher.                      *' -ForegroundColor Yellow
    Write-Host '*****************************************************************' -ForegroundColor Yellow
    Write-Host ''
    return $false
}

if ($pfx) {
    $full = [IO.Path]::GetFullPath($pfx)
    if (-not (Test-Path $full)) { throw "The PFX file does not exist: $full" }
    $rootWithSep = $repo.TrimEnd('\') + '\'
    if (($full + '\').StartsWith($rootWithSep, [StringComparison]::OrdinalIgnoreCase)) { throw 'The PFX file must be kept outside the repository.' }
}

# find signtool (Windows SDK)
$signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)
if (-not $signtool) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kits) {
        $signtool = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
}
if (-not $signtool) { throw 'signtool.exe was not found (install the Windows SDK signing tools). The file was NOT signed.' }

$args = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256')
if ($thumb) { $args += @('/sha1', $thumb) } else { $args += @('/f', $full); if ($env:UNICORNPET_SIGN_PFX_PASSWORD) { $args += @('/p', $env:UNICORNPET_SIGN_PFX_PASSWORD) } }
$args += $Exe
& $signtool @args | Out-Host
if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE. The file was NOT signed." }

$sig = Get-AuthenticodeSignature $Exe
if ($sig.Status -ne 'Valid') { throw "The signature did not verify (status: $($sig.Status)). Do not ship this file." }
Write-Host "Signed by: $($sig.SignerCertificate.Subject)"
Write-Host "Timestamped: $($null -ne $sig.TimeStamperCertificate)"
return $true
