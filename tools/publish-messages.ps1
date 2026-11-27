# Signs a new message list for the work-safe build. Build-time only; not shipped. Uploads nothing.
# Produces dist\messages\messages.json and dist\messages\messages.manifest.json; attach both to a GitHub release tagged v<Version>
# in the releases repository (the app reads .../releases/latest/download/messages.manifest.json).
#
# It never creates or changes a key. The key is created once, on purpose, with: tools\sign-release.ps1 -InitKey
# It refuses to run if the private key is missing or does not match the public key built into the app.
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$MessagesFile,
    [string]$KeyDir,
    [string]$OutDir,
    [string]$BaseUrl = 'https://github.com/dann89/unicorn-pet-releases/releases/download'
)
$ErrorActionPreference = 'Stop'
# Publish with Windows PowerShell 5.1 (.NET Framework), the runtime the app uses, so what is accepted here is what clients accept.
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this script with Windows PowerShell 5.1 (powershell.exe), not PowerShell 7 (pwsh).' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutDir) { $OutDir = Join-Path $repo 'dist\messages' }

# the same pattern the app uses (MessageVersion.Pattern): 1, 1.2 or 1.2.3, each 0-999999, no leading zeros
$versionPattern = '^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){0,2}\z'
if ($Version -cnotmatch $versionPattern) { throw "Version must be 1, 1.2 or 1.2.3 with each number 0-999999 and no leading zeros, got '$Version'" }
if (-not (Test-Path -LiteralPath $MessagesFile)) { throw "Messages file not found: $MessagesFile" }
# .NET file calls resolve relative paths against the process folder, not the PowerShell location, so resolve once, here
$MessagesFile = (Resolve-Path -LiteralPath $MessagesFile).ProviderPath

# key checks: present, outside the repository, and matching the key the app trusts
if ($KeyDir) {
    if (-not (Test-Path -LiteralPath $KeyDir)) { throw "Key folder not found: $KeyDir" }
    $KeyDir = (Resolve-Path -LiteralPath $KeyDir).ProviderPath
}
$keyArgs = @{}
if ($KeyDir) { $keyArgs['KeyDir'] = $KeyDir }
& (Join-Path $PSScriptRoot 'sign-release.ps1') @keyArgs -CheckOnly
if (-not $KeyDir) { $KeyDir = Join-Path $env:USERPROFILE 'UnicornPet-signing' }
$priv = Join-Path $KeyDir 'private.xml'

# use the app's own validator and verifier (built from the same sources)
dotnet build (Join-Path $repo 'src\UnicornPet.Core\UnicornPet.Core.csproj') -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not build UnicornPet.Core' }
Add-Type -Path (Join-Path $repo 'src\UnicornPet.Core\bin\Release\netstandard2.0\UnicornPet.Core.dll')

$bytes = [IO.File]::ReadAllBytes($MessagesFile)
if ($bytes.Length -eq 0 -or $bytes.Length -gt 65536) { throw "The messages file must be between 1 byte and 64 KiB (is $($bytes.Length))" }
$text = (New-Object Text.UTF8Encoding($false, $true)).GetString($bytes)   # throws on invalid UTF-8
$bank = $null
$problem = [UnicornPet.Core.MessageValidator]::Validate($text, [ref]$bank)
if ($problem) { throw "The messages file would be rejected by the app: $problem" }

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$listPath = Join-Path $OutDir 'messages.json'
[IO.File]::WriteAllBytes($listPath, $bytes)

$sha = (Get-FileHash $listPath -Algorithm SHA256).Hash.ToLowerInvariant()
$url = "$BaseUrl/v$Version/messages.json"
# the signed text starts with a fixed domain prefix so a code-release signature can never be used as a message signature
$signingDomain = 'unicornpet-messages-v1'
$payload = "$signingDomain|$Version|$url|$sha"

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
$rsa.FromXmlString([IO.File]::ReadAllText($priv))
$sig = [Convert]::ToBase64String($rsa.SignData([Text.Encoding]::UTF8.GetBytes($payload), 'SHA256'))
$rsa.Dispose()

# check the result with the app's own verifier before anything is offered for upload
$verifier = New-Object UnicornPet.Core.Updater.ManifestVerifier([UnicornPet.Core.Updater.ReleaseKey]::PublicKeyXml)
if (-not $verifier.VerifyPayload($payload, $sig)) { throw 'The app verifier rejected the new signature. Nothing to publish.' }

$manifest = [ordered]@{ version = $Version; url = $url; sha256 = $sha; signature = $sig }
$manifestPath = Join-Path $OutDir 'messages.manifest.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

Write-Host "Messages version ${Version}: $($bank.Random.Count) random, $($bank.Cheeky.Count) cheeky, $($bank.Scheduled.Count) scheduled slots."
Write-Host 'Verified by the app verifier: OK'
Write-Host "Upload these two files to a release tagged v${Version}:"
Write-Host "  $listPath"
Write-Host "  $manifestPath"
