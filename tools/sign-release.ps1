# Release signing for Unicorn Pet.
#   -InitKey      Creates the signing key pair ONCE (refuses if a key already exists) and embeds the public key in
#                 src/UnicornPet.Core/Updater/ReleaseKey.cs. Commit that change. This is the only way a key is ever created.
#   -CheckOnly    Verifies the private key exists, is outside the repo, and matches the public key embedded in the app.
#   (default)     Signs a manifest. Fails if the key is missing or does not match the embedded public key.
# The PRIVATE key lives in -KeyDir, which must be OUTSIDE this repository. Never commit it.
param(
    [string]$KeyDir,
    [string]$Version, [string]$Url, [string]$Sha256, [string]$Out,
    [switch]$InitKey,
    [switch]$Rotate,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path.TrimEnd('\')
if (-not $KeyDir) { $KeyDir = Join-Path $env:USERPROFILE 'UnicornPet-signing' }

# resolve to an absolute path even when it does not exist yet, against the CURRENT directory
if (-not [IO.Path]::IsPathRooted($KeyDir)) { $KeyDir = Join-Path (Get-Location).Path $KeyDir }
$KeyDir = [IO.Path]::GetFullPath($KeyDir).TrimEnd('\')
# resolve links, 8.3 names and subst drives for the part that exists, then compare again
$existing = $KeyDir
while ($existing -and -not (Test-Path -LiteralPath $existing)) { $existing = Split-Path $existing -Parent }
if ($existing) {
    $resolvedExisting = (Resolve-Path -LiteralPath $existing).ProviderPath.TrimEnd('\')
    $rest = $KeyDir.Substring($existing.TrimEnd('\').Length)
    $KeyDir = $resolvedExisting + $rest
    # ask git as well: if the nearest existing folder belongs to THIS repository, refuse
    try {
        $top = (& git -C $existing rev-parse --show-toplevel 2>$null)
        $mine = (& git -C $repo rev-parse --show-toplevel 2>$null)
        if ($top -and $mine -and ([IO.Path]::GetFullPath($top).TrimEnd('\') -ieq [IO.Path]::GetFullPath($mine).TrimEnd('\'))) { throw "KeyDir must be outside the repository (git says $existing belongs to it)." }
    } catch [System.Management.Automation.RuntimeException] { if ($_.Exception.Message -like 'KeyDir must be outside*') { throw } }
}
$keyWithSep = $KeyDir + '\'
$repoWithSep = $repo + '\'
if ($keyWithSep.StartsWith($repoWithSep, [StringComparison]::OrdinalIgnoreCase) -or $repoWithSep.StartsWith($keyWithSep, [StringComparison]::OrdinalIgnoreCase)) {
    throw "KeyDir must be outside the repository: $KeyDir"
}

$priv = Join-Path $KeyDir 'private.xml'
$pub  = Join-Path $KeyDir 'public.xml'
$cfg  = Join-Path $repo 'src\UnicornPet.Core\Updater\ReleaseKey.cs'

function Get-EmbeddedModulus {
    $m = [regex]::Match((Get-Content $cfg -Raw), 'PublicKeyXml = "[^"]*<Modulus>([^<]+)</Modulus>')
    if ($m.Success) { $m.Groups[1].Value } else { $null }
}
function Get-PrivateModulus {
    $x = New-Object System.Xml.XmlDocument
    $x.LoadXml([IO.File]::ReadAllText($priv))
    $x.RSAKeyValue.Modulus
}

if ($InitKey) {
    if (Test-Path $priv) { throw "A key already exists at $priv. Refusing to overwrite it. Move it away on purpose if you really mean to rotate the key." }
    if ((Get-EmbeddedModulus) -and -not $Rotate) {
        throw 'A public key is already embedded in ReleaseKey.cs. Creating a different key would make every installed copy reject every future release or message list. Pass -Rotate only if you really mean to replace it.'
    }
    New-Item -ItemType Directory -Force $KeyDir | Out-Null
    $csp = New-Object System.Security.Cryptography.RSACryptoServiceProvider 3072
    $csp.PersistKeyInCsp = $false
    [IO.File]::WriteAllText($priv, $csp.ToXmlString($true), (New-Object Text.UTF8Encoding $false))
    $p = $csp.ExportParameters($false)
    $pubXml = '<RSAKeyValue><Modulus>' + [Convert]::ToBase64String($p.Modulus) + '</Modulus><Exponent>' + [Convert]::ToBase64String($p.Exponent) + '</Exponent></RSAKeyValue>'
    [IO.File]::WriteAllText($pub, $pubXml, (New-Object Text.UTF8Encoding $false))
    $before = Get-Content $cfg -Raw
    $text = [regex]::Replace($before, 'PublicKeyXml = "[^"]*";', 'PublicKeyXml = "' + $pubXml + '";')
    if ($text -ceq $before) { throw 'The public key was not written into ReleaseKey.cs (the pattern did not match). The key files were created in the key folder; nothing was embedded.' }
    [IO.File]::WriteAllText($cfg, $text, (New-Object Text.UTF8Encoding $false))
    Write-Host "Created the signing key in $KeyDir and embedded the public key in ReleaseKey.cs. Commit that change."
    Write-Host 'Back up private.xml somewhere safe and private. If it is lost, installed copies can never be updated.'
    return
}

if (-not (Test-Path $priv)) { throw "No signing key at $priv. Create it once with: sign-release.ps1 -InitKey" }
$embedded = Get-EmbeddedModulus
if (-not $embedded) { throw 'No public key is embedded in ReleaseKey.cs. Run sign-release.ps1 -InitKey first.' }
if ((Get-PrivateModulus) -ne $embedded) { throw "The private key in $KeyDir does not match the public key embedded in ReleaseKey.cs. Refusing to sign." }
if ($CheckOnly) { Write-Host "Signing key OK ($KeyDir)."; return }

if (-not ($Version -and $Url -and $Sha256 -and $Out)) { throw 'Need -Version -Url -Sha256 -Out' }
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Version must be exactly three numeric parts (x.y.z), got '$Version'" }
$ver = ([version]$Version).ToString()
$payload = "$ver|$Url|$Sha256"
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
$rsa.FromXmlString([IO.File]::ReadAllText($priv))
$bytes = [Text.Encoding]::UTF8.GetBytes($payload)
$sig = $rsa.SignData($bytes, 'SHA256')
if (-not $rsa.VerifyData($bytes, 'SHA256', $sig)) { throw 'Self-check of the new signature failed' }
$manifest = [ordered]@{ version = $ver; url = $Url; sha256 = $Sha256; signature = [Convert]::ToBase64String($sig) }
[IO.File]::WriteAllText($Out, ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
Write-Host "Wrote signed manifest $Out"
