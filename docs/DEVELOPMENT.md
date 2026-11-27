# Development notes

Technical detail for building and maintaining Unicorn Pet. For what it does, see the [README](../README.md).

## Work-safe build

- **Package:** `.\tools\package-worksafe.ps1 -Version x.y.z` writes `dist\UnicornPet-WorkSafe-x.y.z.zip` holding exactly `UnicornPet.exe` and `README.txt`.
  It builds in Release, runs `tools\check-worksafe.ps1` (which reads the exe's real metadata and fails on any denylisted reference, any P/Invoke
  outside the five it needs (window style get/set, the user's display name, the time of the last input, the shell's busy state), or any forbidden string), runs the optional signing hook, and checks the zip contents.
- **Signing:** `tools\sign.ps1` signs the exe when `UNICORNPET_SIGN_THUMBPRINT` (or a `.pfx` kept outside the repository, with
  `UNICORNPET_SIGN_PFX_PASSWORD`) is set; otherwise it says loudly that the package is UNSIGNED. It never creates or fetches a certificate.
- **Mini game (2.0.0):** right-click the unicorn (or the tray icon) and choose "Mini game": a small side-view runner in its own window above
  the pet. Esc, or clicking anywhere else, hides it at once with the run paused ("Resume mini game" brings it back); the x closes it. The
  only thing it writes is the best score, `GameHighScore` in `settings.json`, when beaten. Rules live in `src/UnicornPet.Core/MiniGame`
  (deterministic, unit-tested).
- **Pet options (2.0.0):** Size (Small / Medium / Large; Large is the original size), a name for the unicorn herself (Fifi unless changed), and a little
  glitter from her horn (Sparkles, on by default). Size and Sparkles are in the right-click menu and Settings, the name in Settings. They are stored as `PetSize`, `PetName`
  and `Sparkles` in `settings.json`.
- **Running it:** double-click `UnicornPet.exe` from anywhere. To start it with Windows the person puts a shortcut in the folder opened by
  Win+R, `shell:startup` (the menu item "How to start with Windows" shows this); no code does it.
- **Files it writes:** only `%APPDATA%\UnicornPet\settings.json`; if "Fetch new messages" is turned on, `messages.remote.json` plus
  `messages.remote.json.version`; and, only when Settings > Messages is set to "A web address", `messages.custom.json` (the last good copy of
  that list). All are written through a temporary file that is replaced in one step; leftovers of an interrupted save are removed at the next
  start, only files with the exact temporary names. A `messages.json` placed next to the exe is the base that a source chosen in Settings is
  laid over.
- **Network:** **Fetch new messages** (on by default; untick it in Settings to stop it; the first check is about two minutes after
  start), and, only if the person chooses "A web address" under Messages, one GET a day to that address (https only, every redirect too,
  64 KiB, 30 second limit; it is NOT signed, so every line is validated by the same rules as the signed list and a failure keeps the last
  good copy). To stay fully offline: Messages "Built-in" and "Fetch new messages" unticked. When on, at most once a day it fetches `messages.manifest.json` and `messages.json` (64 KiB each, https only, 30 second limit,
  digitally signed with the key in `src/UnicornPet.Core/Updater/ReleaseKey.cs`; text only, validated line by line: at most 140 characters,
  no control or invisible characters, `{name}` the only brace token). A newer version number is required, remembered both in settings.json
  and beside the list. A downloaded list takes effect at the next start. Failures are silent and change nothing.
- **Custom messages (2.1.0):** Settings > Messages chooses Built-in, "A file" (a full local or `\\server\share` path to a `.json` file, typed,
  pasted or dropped; read at start, on Save and on "Reload now") or "A web address" (above). The file may leave pools out (they come from
  the base) and may hold up to 8 named `sections` of up to 100 lines; five sections ship built in (`src/UnicornPet.Core/Resources/sections.default.json`)
  and every section has a tick box, on until unticked. Keys `MessageSource`, `MessageFile`, `MessageUrl`, `MessageUrlCached`, `LastCustomFetch`,
  `SectionsOff`. Start from `docs/examples/messages.sample.json`. Signed lists never carry `sections` (2.0.0 copies refuse unknown keys).
  Code: `src/UnicornPet.Core/CustomSource.cs`, `CustomMessages.cs`.
- **Idle sparkle poop (2.1.0):** off by default (`IdlePoop`). After 3 minutes without keyboard or mouse she trots about her monitor leaving
  small click-through pieces (40 at most, then she lies down and nothing moves); any input clears them at once. Uses `GetLastInputInfo`
  (the time of the last input only) and `SHQueryUserNotificationState` (no party unless the shell accepts notifications). Rules in
  `src/UnicornPet.Core/IdleParty.cs`.
- **Zoomies (2.1.0):** "Zoomies!" in the right-click menu: she races along every monitor and back, changing monitor by a hidden hop.
  "Zoomies now and then" in Settings (`RandomZoomies`, off by default) does it every 45 to 90 minutes when the shell says she may.
  Rules in `src/UnicornPet.Core/Zoomies.cs`.
- **Publishing messages:** `.\tools\publish-messages.ps1 -Version 3 -MessagesFile .\my-messages.json` (run it with Windows PowerShell 5.1).
  The version is `1`, `1.2` or `1.2.3` (each number 0-999999, no leading zeros). The signature covers
  `unicornpet-messages-v1|version|url|sha256`, so a code-release signature can never pass for a message signature. Upload the two files it
  writes to a release tagged `v<version>` in the releases repository. The script refuses to run if the signing key is missing, inside the
  repository or different from the public key built into the app, and it never creates a key (`sign-release.ps1 -InitKey` does, once,
  and refuses to replace an embedded key without `-Rotate`).
- **Tests:** `dotnet test tests\UnicornPet.Tests` runs on both `net10.0` and `net48` (the runtime the app really uses).

## Building

Requires the .NET SDK (built with 10.0). The app targets .NET Framework 4.8, so the target PC needs no extra runtime.

```powershell
dotnet build -c Release
dotnet test -c Release
```

Command-line flags for maintenance and testing are listed under [Maintenance flags](#maintenance-flags).
Set `UNICORNPET_APPDATA` / `UNICORNPET_LOCALDIR` to point the app at temporary folders when testing; the app then also leaves the
Windows startup registry entry alone.

## Maintenance flags

None of these show any window except where noted, and none are needed in normal use.

- `--init-state <version>`: resets the update state for a clean install of that version (3 or 4 numbers), exit code 0 or 1; `install.ps1` calls it.
- `--verify-manifest <file>`: checks a `manifest.json` against the public key built into the app, prints OK or FAIL, exit code 0 or 1; `publish.ps1` calls it.
- `--trial-token <token>`: passed by the launcher to the version it starts, so that version can end its start-up trial; never type it by hand.
- `--from-bootstrap`: added by the launcher so the started version does not launch another copy of itself.
- `--snapshot <dir>`: renders every pose and the speech bubble to PNG files in that folder (the app's own drawing only, never the desktop), then exits.
- `--diag <file>`: writes the pet window's style and DWM settings to a file, then exits.
- `--selftest-menu|--selftest-settings|--selftest-bubble|--selftest-startup <file>`: headless checks of the menu, Settings window, bubble placement and
  startup registration; they write their results to the file and exit.
- `--hold idle|walk|sleep`: pins the pet to one state (used by `tools\measure.ps1` for CPU and memory readings).
- Debug builds only: `--update-url <url>` (may be a `file:` URL) and `--update-now` run an update check immediately against that manifest.

## Making the install package

```powershell
.\tools\package.ps1 -Version 1.0.0
```

Produces, under `dist\`: the `UnicornPet-Setup` folder (exe, libraries, `install.cmd`, `install.ps1`, `uninstall.ps1`)
and `UnicornPet-portable-<version>.zip`.

## Publishing an update

Updates are hosted as GitHub release assets in the releases repository. The app checks
`https://github.com/dann89/unicorn-pet-releases/releases/latest/download/manifest.json` at most every 6 hours and only
applies a release whose manifest signature verifies against the public key built into the app.

1. Run `.\tools\publish.ps1 -Version 1.0.1`. The version must be exactly three numbers (x.y.z).
   It checks the signing key first, then builds, zips, hashes and signs, and writes `dist\UnicornPet-1.0.1.zip` and
   `dist\manifest.json`. It then runs the built app's own verifier (`UnicornPet.exe --verify-manifest <file>`, exit code 0 means OK)
   against the new manifest and stops if that fails. It uploads nothing.
2. Create a release tagged `v1.0.1` in the releases repository and attach exactly those two files.
3. Installed copies pick it up on a later check, stage it, and switch on the next launch. A version that fails to start
   twice is rolled back automatically and never retried.

Keep release notes and file contents free of personal details.

### What updates do not change

An update replaces the version folder under `%LOCALAPPDATA%\UnicornPet\versions\`. It does **not** replace the launcher: the copy of
`UnicornPet.exe` in `%LOCALAPPDATA%\UnicornPet\` that the Run entry points at. The launcher is deliberately tiny and stable; it reads
`state.json`, picks the version to run, and passes it a one-time launch token. If a change touches launcher behaviour (anything that
runs before the window, in `App.xaml.cs`, `UpdateStager` or the state format), updating is not enough: re-run `install.cmd` from the new
setup folder. `install.ps1` calls `UnicornPet.exe --init-state <version>`, which resets the update state for a clean install.

## Signing key (read this)

- The key is created once, on purpose, with `.\tools\sign-release.ps1 -InitKey`. It writes `private.xml` and `public.xml` to
  `%USERPROFILE%\UnicornPet-signing\` and embeds the public key in `src/UnicornPet.Core/Updater/UpdateConfig.cs`. Commit that change;
  **never commit `private.xml`** (the repository ignores key files, and the key directory must be outside the repository: the scripts refuse a
  key folder inside it). `-InitKey` refuses to overwrite an existing key. A normal publish never creates or changes a key, and it fails if the
  private key is missing or does not match the public key built into the app.
- **Back up `private.xml` somewhere safe and private** (for example an encrypted password manager attachment or an offline drive).
  If it is lost, copies already installed can never be updated, because they only trust this key. Recovery means reinstalling
  by hand with a build that embeds a new key.
- If the key might be exposed, treat every installed copy as needing a manual reinstall with a new key.

## Layout

```
src/UnicornPet.Core/   logic with no UI: messages, scheduler, settings, behaviour, menu model, names, updater
src/UnicornPet.App/    WPF shell: pet window, bubble, tray, Settings window, startup registration, launcher
tests/UnicornPet.Tests/
tools/                 install, uninstall, package, publish, sign-release, measure
assets/poses-512/      runtime pose frames (AI-generated artwork)
```

## Release checklist (on the physical screen, not a remote session)

- No box or border around the pet on the real monitor; the transparent area clicks through to the window beneath.
- Right-click the pet and the tray icon: every menu item works, including Settings.
- Settings opens centred, Esc closes it, the name preview updates, Save persists the name.
- The pet greets after start, talks on schedule, and stays quiet when set to Quiet or asleep.
- Sign out and back in: the pet starts by itself, once.
- Typing in another app is never interrupted while the pet walks and talks.
- A second launch (double-click while it runs) exits silently.
- `install.cmd` and `uninstall.ps1` need no administrator prompt and leave no files behind after removal.
