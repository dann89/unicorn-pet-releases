# Build-time guard for the work-safe exe (not shipped).
# It reads the exe's real metadata - every external type and member it references, every referenced assembly, every declared
# P/Invoke - and fails if the exe goes beyond what a plain desktop pet needs. It also scans the raw bytes for a short list of strings.
# P/Invokes use an ALLOW-list: anything not listed fails, whatever it is called.
# Usage: .\tools\check-worksafe.ps1 [-Exe <path>]      Exit code 0 = clean, 1 = problems found.
param([string]$Exe)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Exe) { $Exe = Join-Path $repo 'src\UnicornPet.WorkSafe\bin\Release\net48\UnicornPet.exe' }
if (-not (Test-Path $Exe)) { throw "Not found: $Exe (build the work-safe project in Release first)" }
$Exe = (Resolve-Path $Exe).Path
$problems = New-Object System.Collections.Generic.List[string]
function Flag($msg) { $script:problems.Add($msg); Write-Host "  FAIL: $msg" }
Write-Host "Checking $Exe"

# types the exe must not reference (by namespace or name)
$deniedTypes = @('Microsoft.Win32.Registry', 'Microsoft.Win32.RegistryKey', 'System.Diagnostics.Process', 'System.Diagnostics.ProcessStartInfo',
    'System.Reflection.Emit', 'System.Management')
$deniedNamespaces = @('System.IO.Compression', 'System.Reflection.Emit', 'System.Management', 'Microsoft.CSharp.RuntimeBinder')
# members the exe must not call
$deniedMembers = @('System.Reflection.Assembly::Load', 'System.Reflection.Assembly::LoadFrom', 'System.Reflection.Assembly::LoadFile',
    'System.Reflection.Assembly::LoadWithPartialName', 'System.Reflection.Assembly::UnsafeLoadFrom', 'System.AppDomain::Load',
    'System.AppDomain::ExecuteAssembly', 'System.Activator::CreateInstance', 'System.Activator::CreateInstanceFrom',
    'System.Type::GetType', 'System.Runtime.InteropServices.Marshal::GetDelegateForFunctionPointer')
$deniedAssemblies = @('System.Management.Automation', 'System.Management', 'System.IO.Compression', 'System.IO.Compression.FileSystem')
# the only native functions the exe needs: window style flags (no focus stealing, no Alt-Tab entry), the display name of the user, and
# the time of the last input and whether the shell says the person is busy (idle sparkle poop, off by default)
$allowedImports = @('user32.dll|GetWindowLong', 'user32.dll|SetWindowLong', 'secur32.dll|GetUserNameEx', 'user32.dll|GetLastInputInfo', 'shell32.dll|SHQueryUserNotificationState')   # DLL name | effective entry point
# strings that must not appear anywhere in the file
$deniedStrings = @('ExecutionPolicy', 'Bypass', 'work PC', 'girlfriend', '<D>', '<P>', '<Q>', '<InverseQ>')
# personal names that must not ship either: one per line in tools\private-strings.txt, which is never committed
$privateList = Join-Path $PSScriptRoot 'private-strings.txt'
if (Test-Path -LiteralPath $privateList) { $deniedStrings += @(Get-Content -LiteralPath $privateList -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
else { Write-Host '  note: tools\private-strings.txt not found, so no personal names are checked' }

# ---------- managed references ----------
$typeRefs = New-Object System.Collections.Generic.HashSet[string]
$memberRefs = New-Object System.Collections.Generic.HashSet[string]
# a dependency is looked up by name first, then in the .NET Framework folder (some facades, e.g. System.Net.Http 4.2, are only there)
$handler = [System.ResolveEventHandler] { param($s, $e)
    try { [System.Reflection.Assembly]::ReflectionOnlyLoad($e.Name) }
    catch {
        try { [System.Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) ((New-Object System.Reflection.AssemblyName $e.Name).Name + '.dll'))) } catch { $null }
    } }
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($handler)
# loaded from bytes so the file is not locked afterwards
$asm = [System.Reflection.Assembly]::ReflectionOnlyLoad([System.IO.File]::ReadAllBytes($Exe))
$mod = $asm.ManifestModule

foreach ($ref in $asm.GetReferencedAssemblies()) { if ($deniedAssemblies -contains $ref.Name) { Flag "references assembly $($ref.Name)" } }
$unresolved = 0
$genericContext = [Type[]]@([object], [object], [object], [object])   # members of generic types (async state machines, collections) need a generic context to resolve
for ($rid = 1; $rid -lt 20000; $rid++) {
    try { $t = $mod.ResolveType(0x01000000 -bor $rid) } catch [System.ArgumentOutOfRangeException] { break } catch { $unresolved++; continue }
    if ($t -and $t.FullName) { [void]$typeRefs.Add($t.FullName) }
}
for ($rid = 1; $rid -lt 40000; $rid++) {
    try { $m = $mod.ResolveMember(0x0A000000 -bor $rid) } catch [System.ArgumentOutOfRangeException] { break }
    catch { try { $m = $mod.ResolveMember(0x0A000000 -bor $rid, $genericContext, $genericContext) } catch { $unresolved++; continue } }
    if ($m -and $m.DeclaringType) { [void]$memberRefs.Add($m.DeclaringType.FullName + '::' + $m.Name) }
}
Write-Host ("  external types referenced: {0}; external members referenced: {1}" -f $typeRefs.Count, $memberRefs.Count)
if ($unresolved -gt 0) { Flag "$unresolved metadata reference(s) could not be resolved, so they could not be checked" }
foreach ($t in $typeRefs) {
    if ($deniedTypes -contains $t) { Flag "references type $t"; continue }
    foreach ($n in $deniedNamespaces) { if ($t.StartsWith($n + '.')) { Flag "references type $t"; break } }
}


# ---------- declared P/Invokes ----------
$types = try { $asm.GetTypes() } catch [System.Reflection.ReflectionTypeLoadException] { Flag 'some types could not be loaded, so they could not be checked'; $_.Exception.Types | Where-Object { $_ } }
Write-Host ''
Write-Host ('  Assembly references (' + @($asm.GetReferencedAssemblies()).Count + '): ' + ((@($asm.GetReferencedAssemblies()) | ForEach-Object { $_.Name } | Sort-Object) -join ', '))

# who calls a denied member? Scan every method body for call / callvirt / newobj instructions and resolve their targets.
# Exact (caller, member) pairs that are known to be harmless are listed here with the reason; anything else fails.
$allowedCallers = @(
    @{ Member = 'System.Activator::CreateInstance'; Caller = 'XamlGeneratedNamespace.GeneratedInternalTypeHelper::CreateInstance'; Reason = 'WPF-generated helper that creates the window and control classes named in the markup' }
)
$tokenCache = @{}
function Resolve-Target($tok) {
    if ($script:tokenCache.ContainsKey($tok)) { return $script:tokenCache[$tok] }
    $name = $null
    try { $mm = $mod.ResolveMethod($tok); if ($mm -and $mm.DeclaringType) { $name = $mm.DeclaringType.FullName + '::' + $mm.Name } }
    catch { try { $mm = $mod.ResolveMethod($tok, $genericContext, $genericContext); if ($mm -and $mm.DeclaringType) { $name = $mm.DeclaringType.FullName + '::' + $mm.Name } } catch { } }
    $script:tokenCache[$tok] = $name
    return $name
}
$found = New-Object System.Collections.Generic.HashSet[string]
$callersSeen = New-Object System.Collections.Generic.HashSet[string]
$flags = [System.Reflection.BindingFlags]'Static,Instance,Public,NonPublic,DeclaredOnly'
foreach ($t0 in $types) {
    foreach ($m0 in (@($t0.GetMethods($flags)) + @($t0.GetConstructors($flags)))) {
        $body = $null; try { $body = $m0.GetMethodBody() } catch { Flag ("method body of $($t0.FullName)::$($m0.Name) could not be read"); continue }
        if (-not $body) { continue }
        $il = $body.GetILAsByteArray(); if (-not $il) { continue }
        for ($i = 0; $i -lt $il.Length - 4; $i++) {
            $op = $il[$i]
            if ($op -ne 0x28 -and $op -ne 0x6F -and $op -ne 0x73) { continue }
            $hi = $il[$i + 4]
            if ($hi -ne 0x0A -and $hi -ne 0x2B -and $hi -ne 0x06) { continue }
            $target = Resolve-Target ([BitConverter]::ToInt32($il, $i + 1))
            if (-not $target) { continue }
            foreach ($d in $deniedMembers) {
                if ($target -eq $d) {
                    $caller = $t0.FullName + '::' + $m0.Name
                    [void]$found.Add("$target <- $caller")
                    [void]$callersSeen.Add($d)
                    break
                }
            }
        }
    }
}
foreach ($f in ($found | Sort-Object)) {
    $parts = $f -split ' <- '
    $ok = $allowedCallers | Where-Object { $parts[0].StartsWith($_.Member) -and $parts[1] -eq $_.Caller }
    if ($ok) { Write-Host ("  allowed: {0} calls {1} ({2})" -f $parts[1], $parts[0], $ok.Reason) }
    else { Flag "$($parts[1]) calls $($parts[0])" }
}
foreach ($m in $memberRefs) {
    foreach ($d in $deniedMembers) {
        if (($m -eq $d) -and -not $callersSeen.Contains($d) -and -not ($allowedCallers | Where-Object { $m.StartsWith($_.Member) })) { Flag "references $m (caller not identified)"; break }
    }
}
$pinvokes = New-Object System.Collections.Generic.List[object]
foreach ($t in $types) {
    foreach ($m in $t.GetMethods([System.Reflection.BindingFlags]'Static,Instance,Public,NonPublic,DeclaredOnly')) {
        if (($m.Attributes -band [System.Reflection.MethodAttributes]::PinvokeImpl) -ne 0) {
            $dll = '?'; $entry = $m.Name
            try {
                foreach ($a in $m.GetCustomAttributesData()) {
                    if ($a.AttributeType.Name -ne 'DllImportAttribute') { continue }
                    $dll = ([string]$a.ConstructorArguments[0].Value).ToLowerInvariant()
                    if (-not $dll.EndsWith('.dll')) { $dll += '.dll' }
                    foreach ($na in $a.NamedArguments) { if ($na.MemberName -eq 'EntryPoint') { $entry = [string]$na.TypedValue.Value } }
                }
            } catch { }
            $pinvokes.Add([pscustomobject]@{ Dll = $dll; Function = $entry; DeclaredIn = $t.FullName })
        }
    }
}
Write-Host ''
Write-Host "  Declared P/Invokes ($($pinvokes.Count)) - for review:"
if ($pinvokes.Count -eq 0) { Write-Host '    (none)' }
foreach ($p in $pinvokes | Sort-Object Dll, Function) {
    Write-Host ("    {0,-14} {1,-22} in {2}" -f $p.Dll, $p.Function, $p.DeclaredIn)
    if ($allowedImports -notcontains ($p.Dll + '|' + $p.Function)) { Flag "P/Invoke $($p.Dll)!$($p.Function) is not on the allow-list" }
}

# ---------- raw bytes ----------
Write-Host ''
$bytes = [System.IO.File]::ReadAllBytes($Exe)
$ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
$utf16 = [System.Text.Encoding]::Unicode.GetString($bytes)
$utf16b = [System.Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
foreach ($s in $deniedStrings) {
    if ($ascii.Contains($s) -or $utf16.Contains($s) -or $utf16b.Contains($s)) { Flag "contains the string '$s'" }
}
if ($ascii.Contains('.pdb')) { Flag 'refers to a .pdb file (debug information should not ship)' }

# ---------- looks ordinary ----------
$vi = (Get-Item $Exe).VersionInfo
foreach ($pair in @(@('CompanyName', $vi.CompanyName), @('ProductName', $vi.ProductName), @('FileDescription', $vi.FileDescription), @('LegalCopyright', $vi.LegalCopyright), @('FileVersion', $vi.FileVersion))) {
    if ([string]::IsNullOrWhiteSpace($pair[1])) { Flag "version resource field $($pair[0]) is empty" }
}
if (-not $ascii.Contains('asInvoker')) { Flag 'embedded manifest does not request asInvoker' }
if ($ascii.Contains('requireAdministrator') -or $ascii.Contains('highestAvailable')) { Flag 'manifest asks for elevation' }
Add-Type -AssemblyName System.Drawing
try { [void][System.Drawing.Icon]::ExtractAssociatedIcon($Exe) } catch { Flag 'the exe has no usable icon' }

Write-Host ''
if ($problems.Count -eq 0) { Write-Host 'WORK-SAFE CHECK: PASS'; exit 0 }
Write-Host ("WORK-SAFE CHECK: FAIL ({0} problem(s))" -f $problems.Count)
exit 1
