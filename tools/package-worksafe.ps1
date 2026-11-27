# Builds the work-safe package: dist\UnicornPet-WorkSafe-<x.y.z>.zip containing ONLY UnicornPet.exe and README.txt.
# Steps: build (Release) -> check-worksafe.ps1 (stops on any problem) -> optional signing hook (sign.ps1) -> zip -> list.
# Build-time only; nothing here is shipped, and nothing is uploaded.
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$OutDir
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Version must be exactly three numeric parts (x.y.z), got '$Version'" }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutDir) { $OutDir = Join-Path $repo 'dist' }
$ver = ([version]$Version).ToString()

dotnet build (Join-Path $repo 'src\UnicornPet.WorkSafe\UnicornPet.WorkSafe.csproj') -c Release "-p:AssemblyVersion=$ver.0" "-p:FileVersion=$ver.0" "-p:InformationalVersion=$ver" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$builtExe = Join-Path $repo 'src\UnicornPet.WorkSafe\bin\Release\net48\UnicornPet.exe'

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$stage = Join-Path $OutDir "worksafe-stage-$ver"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
$exe = Join-Path $stage 'UnicornPet.exe'
Copy-Item $builtExe $exe

$readme = @"
Unicorn Pet $ver

A small animated unicorn for your desktop. It wanders along the bottom of the screen and says friendly things during the working day.

TO RUN
  Double-click UnicornPet.exe. It runs from wherever you put it. It does not install anything and does not change any system setting.

TO QUIT
  Right-click the unicorn (or its small icon near the clock) and choose Quit.

TO START IT WITH WINDOWS (optional, you do this yourself)
  Press Win+R, type shell:startup, press Enter, and put a shortcut to UnicornPet.exe in the folder that opens.
  To stop it starting, delete that shortcut. The right-click menu item "How to start with Windows" shows these steps too.

SETTINGS
  Right-click the unicorn and choose Settings: your name (or use your Windows first name), a name for the unicorn, her size,
  sparkles, cheeky mode, scheduled messages, "Fetch new messages", sparkle poop when the PC is left alone, zoomies now and then,
  and where her messages come from.
  Size (Small, Medium or Large; Large is the original size) and Sparkles (a little glitter from her horn) are also in the
  right-click menu. She is called Fifi; change or clear the name in Settings. A new name makes her introduce herself once.

MINI GAME
  Right-click the unicorn and choose Mini game. Space, Up or a left click jumps; hold Down or the right mouse button to duck
  (in the air it drops faster). Esc hides the game at once, and so does clicking anywhere else; the run waits, and the menu then
  says Resume mini game. The x closes it. The best score is one number (GameHighScore) in settings.json; the game writes nothing else.

WHERE THINGS ARE KEPT
  Settings:  %APPDATA%\UnicornPet\settings.json
  "Fetch new messages" is on unless you untick it in Settings. While on, the program may fetch a small list of messages (plain text
  only, digitally signed) at most once a day and keep it as %APPDATA%\UnicornPet\messages.remote.json, with a tiny note beside it
  called messages.remote.json.version (the list's version number).
  If you choose "A web address" under Messages, the list at that address is kept as %APPDATA%\UnicornPet\messages.custom.json.
  Apart from settings.json and these three files nothing is written anywhere. Only text is ever downloaded, never a program.

SECTIONS
  Five extra sets of lines are built in: Office banter, Pep talks, Unicorn facts, Random facts and Office roasts. Each has a tick
  box under Settings, Messages. They are all on to begin with; untick the ones you do not want and they stay off.

YOUR OWN MESSAGES
  Settings, Messages: Built-in, A file, or A web address.
  A file: type, paste or drop the full path of a .json file. It is read when the program starts and when you press Reload now.
  A web address: https only, at most 64 KB, read at most once a day. It is not signed: whoever controls that address controls what
  she says, so only use an address you trust. If it cannot be read, the last good copy is used.
  The file may leave parts out; those come from the built-in lines. It may also hold up to 8 named sections of up to 100 lines;
  each section has a tick box in Settings and is on until you untick it. A section with the same id as a built-in one replaces it. Every line is checked (140 characters at most, plain text,
  {name} is the only placeholder). A web list can never change the name she calls you.
  A file called messages.json next to UnicornPet.exe still works as before; a source chosen in Settings is laid over it.
  To stay completely offline: choose Built-in and untick "Fetch new messages".

WHEN THE PC IS LEFT ALONE (optional, off unless you tick it)
  "Sparkle poop when idle": after 3 minutes without keyboard or mouse she trots about and leaves little sparkly poops. Any key or
  mouse movement clears them at once. Clicks go straight through them and they never take the keyboard. To know when the PC is left
  alone the program asks Windows for the time of the last key press or mouse movement, and whether a full-screen app or a
  presentation is running. It never sees what was typed. Windows does not report screen sharing or a video in a window, so leave
  this off if that matters to you.

ZOOMIES
  Right-click, Zoomies! and she races across all your screens and back. "Zoomies now and then" in Settings lets her do it by
  herself every hour or so (off unless you tick it).

YOUR NAME
  With "Use my Windows first name" ticked (and no name typed in), the program asks Windows for your display name once at start.
  On a computer joined to a work domain, Windows itself may contact the domain controller to answer that. To prevent the question
  being asked at all, untick "Use my Windows first name" in Settings, or type your name there.

TO REMOVE IT
  Quit it, delete UnicornPet.exe, delete any shortcut you made, and delete the folder %APPDATA%\UnicornPet.
"@
[IO.File]::WriteAllText((Join-Path $stage 'README.txt'), ($readme -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding($false)))

# the guard: stop on any problem
& (Join-Path $PSScriptRoot 'check-worksafe.ps1') -Exe $exe
if ($LASTEXITCODE -ne 0) { throw 'check-worksafe.ps1 failed; the package was not created.' }

# optional signing (prints a loud UNSIGNED warning when no certificate is supplied)
$signScript = Join-Path $PSScriptRoot 'sign.ps1'
$signed = $false
if (Test-Path $signScript) { $signed = [bool](& $signScript -Exe $exe) }

$zip = Join-Path $OutDir "UnicornPet-WorkSafe-$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $stage -Recurse -Force

# the zip must hold exactly two files
$za = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($za.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    if (($names -join '|') -ne 'README.txt|UnicornPet.exe') { throw "Unexpected zip contents: $($names -join ', ')" }
    Write-Host ''
    Write-Host "Package: $zip"
    foreach ($e in $za.Entries) { Write-Host ("  {0,-16} {1,10:N0} bytes" -f $e.FullName, $e.Length) }
}
finally { $za.Dispose() }
Write-Host ("Zip size: {0:N0} bytes" -f (Get-Item $zip).Length)
Write-Host ("SHA-256: " + (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())
if ($signed) { Write-Host 'Signed: yes' } else { Write-Host 'UNSIGNED PACKAGE: UnicornPet.exe is not code-signed. Decide on a certificate before relying on this build.' -ForegroundColor Yellow }
