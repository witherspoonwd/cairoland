# Plan: Shell Launcher v2 as Cairo's shell, then tiling UWP apps

## Context

Today Cairo becomes the shell by stopping Explorer (`Start-CairoShell.ps1`), or by writing the Winlogon `Shell` value from the Settings button. Either way, nothing hosts UWP windows, because that support lives inside Explorer. As a result:
- UWP apps (Calculator, Settings, Store) don't start.
- Cairo hides the Settings and Action Center commands in shell mode.

The machine is now Windows 10 **Enterprise** 22H2 (19045). That edition has **Shell Launcher v2**, which replaces Explorer with `CustomShellHost.exe`. Microsoft's docs say: *"You can use a custom Windows desktop application that can then launch UWP apps, such as Settings."*

Goal:
1. Make Shell Launcher v2 the only way Cairo runs as the shell.
2. Then make the tiler manage UWP windows properly.

The old ways of becoming the shell go away. When you tap Win on its own, nothing happens (Start doesn't open).

### Facts this plan relies on

**Microsoft documentation:**
- Enable the feature with `Enable-WindowsOptionalFeature -Online -FeatureName Client-DeviceLockdown,Client-EmbeddedShellLauncher` (needs admin).
- Configure it locally by writing XML to the `ShellLauncher` property of `MDM_AssignedAccess` in `root\cimv2\mdm\dmmap`. That WMI class only works when run as **SYSTEM**.
  - Write the XML HTML-encoded: `[System.Net.WebUtility]::HtmlEncode(...)`.
  - Errors are logged to `Microsoft-Windows-AssignedAccess/Admin`.
  - The docs' removal snippet clears `.Configuration`. That's the kiosk property. For Shell Launcher, clear `.ShellLauncher` instead.
- XML structure:
  - Namespaces: `http://schemas.microsoft.com/ShellLauncher/2018/Configuration`, plus `V2` = `.../2019/Configuration`.
  - A `Shell` element takes the attributes `Shell`, `V2:AppType="Desktop"` and `V2:AllAppsFullScreen="false"`, plus up to four `ReturnCodeAction` entries and one `DefaultAction`. Actions: `RestartShell` | `RestartDevice` | `ShutdownDevice` | `DoNothing`.
  - `DefaultProfile` applies to accounts without a `Config`. A local account can be named `.\user`, and it must already exist.
- The `Shell` attribute takes a full path and can include arguments.
- Shell Launcher runs the `Run` and `RunOnce` registry keys itself before starting the shell.
- Changes take effect at the next sign-in.
- A shell that starts another process and then exits isn't supported: Shell Launcher only watches the process it started.

**Community reports** (one bbLean user, Win10 Enterprise; unverified, so step 0 checks them):
- UWP apps, the Store, Alt+Tab, Win+Tab and virtual desktops work.
- Win or Ctrl+Esc opens a Start menu that can't be clicked.
- Toast notifications don't work.
- CustomShellHost "takes over the tray".
- Non-Microsoft-signed exes may not start.

**Account:** the only account is local admin `runch` (SID `...-1001`). The built-in Administrator is disabled.

---

## Phase 0: Spike to check how CustomShellHost behaves (decides the open questions below)

Install the current build (`Build-Installer.ps1`, then run the installer). Apply a hand-written config with the Phase 1 scripts. Sign out and back in. Then run a new read-only `Get-ShellDiagnostics.ps1` (repo root), which dumps:
- `GetShellWindow()` and the process that owns it.
- Every `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`, `Progman` and `WorkerW` window, with its owning process.
- The `CustomShellHost.exe` process tree, i.e. who is Cairo's parent.
- Cairo's log lines for: `Running as shell`, hotkey registrations that failed, and the tray service.
- After launching Calculator and `ms-settings:`:
  - every `ApplicationFrameWindow` and its child `Windows.UI.Core.CoreWindow`;
  - their styles, cloak state and process IDs.

Record the answers in `plan.md` under "Spike results". Each one picks a branch:

| Question | If yes | If no |
|---|---|---|
| Does CustomShellHost own `Shell_TrayWnd`? | Phase 1 step 6: check whether ManagedShell's tray still receives icons, and only fix it if it doesn't | Nothing to do |
| Is `GetShellWindow()` non-zero before Cairo starts? | Rely on `/shell=true` to force shell mode, and accept that `SetShellWindow` fails (the wallpaper windows still work) | Nothing to do |
| Does a non-Microsoft-signed `CairoDesktop.exe` start? | Nothing to do | Sign `CairoDesktop.exe` with the existing local certificate (`New-CairoSigningCertificate.ps1`; `Build-Installer.ps1` already signs the UIAccess copy) |
| Which Win hotkeys does CustomShellHost own? | Leave them to the existing keyboard-hook fallback in `TilingHotKeyService` | — |

### Spike results

Not run yet: it needs a sign-out under Shell Launcher. The code was written so each branch above works either way:
- `ShellHost.IsShellLauncher` forces `IsAppRunningAsShell`, so a non-zero `GetShellWindow()` doesn't matter.
- The tray (step 6) is untouched until the spike shows it gets no icons.
- `Test-CairoEnvironment.ps1` only checks the UIAccess copy's signature; extend it to `CairoDesktop.exe` if the spike finds unsigned exes don't start.
- Phase 2 step 4 ships both hide modes: `SW_HIDE` by default, and `Settings.TilingParkUwpWindows` switches UWP frames to off-screen parking without a rebuild.

| Question | Answer |
|---|---|
| Does CustomShellHost own `Shell_TrayWnd`? | |
| Is `GetShellWindow()` non-zero before Cairo starts? | |
| Does a non-Microsoft-signed `CairoDesktop.exe` start? | |
| Which Win hotkeys does CustomShellHost own? | |
| Does the WMI bridge write enable the features by itself (and need a reboot)? | |
| Phase 2: do UWP frames survive `SW_HIDE`/`SW_SHOWNA`? | |

---

## Phase 1: Swap to Shell Launcher v2

### 1. Scripts (repo root). Delete `Start-CairoShell.ps1`.

- **`Enable-CairoShellLauncher.ps1`** (asks for elevation):
  - Enable the two optional features. If a reboot is needed, say so and stop.
  - Generate the XML:
    - `DefaultProfile` → `%SystemRoot%\explorer.exe`, so any other account still gets Explorer.
    - Cairo `Profile` → `"<InstallDir>\CairoDesktop.exe" /shell=true /shelllauncher=true`, with `AppType=Desktop` and `AllAppsFullScreen=false`.
    - `ReturnCodeAction`s: `1` → `DoNothing` (Cairo exited on purpose), `3` → `RestartShell` (Cairo asked to restart). `DefaultAction` → `RestartShell` (a crash).
    - `Config`: `.\$env:USERNAME` → the Cairo profile. `-User` can override the account.
  - Write the XML as SYSTEM through a one-shot scheduled task: `Register-ScheduledTask -User SYSTEM`, run it, wait, then unregister. That avoids needing psexec.
  - Read the value back, print any new `AssignedAccess/Admin` events, and offer to sign out.
  - Find the install directory from `HKLM\Software\CairoShell\Install_Dir`, which the NSIS installer writes.
- **`Disable-CairoShellLauncher.ps1`** (asks for elevation): same SYSTEM-task method, sets `ShellLauncher = $null`. Its header documents how to recover:
  - Ctrl+Shift+Esc → Run new task → tick "admin" → `powershell -File ...\Disable-CairoShellLauncher.ps1`.
  - In the same session you can also start `explorer.exe` from there.
- **`Deploy-Cairo.ps1`** (the dev loop):
  1. `Build-Installer.ps1`.
  2. `taskkill /F` both `CairoDesktop.exe` and `CairoDesktop.UIAccess.exe`. A forced kill exits with code 1, which maps to `DoNothing`, so Shell Launcher doesn't restart Cairo while the files are being replaced. Windows hidden by the tiler come back on the next start through `HiddenWindowJournal.RestoreAll()`.
  3. Run the installer silently (`/S`).
  4. Start the installed Cairo with the same arguments as the XML. Shell Launcher won't watch it until the next sign-in, and the script prints that.
- Update `README.md` for the new flow: certificate (once) → run the installer → pick "Use Cairo as my shell" → sign out. The installer calls these scripts (step 7). Running them by hand is for development and recovery.

### 2. Shell Launcher mode in Cairo

- Add a `ShellLauncher` command-line option next to `Shell`/`NoShell`. Find where `_options.CurrentValue.Shell` is defined.
- Add a static `ShellHost` class in `CairoDesktop.Common`:
  - `IsShellLauncher`: the option is set, or a fallback check finds a `CustomShellHost.exe` process in this session.
  - `HasImmersiveShell => !EnvironmentHelper.IsAppRunningAsShell || IsShellLauncher`.
- Set it in `SetIsCairoRunningAsShell()` (`CairoApplicationInitializationService.InitializationRoutines.cs:34`) and log it.

### 3. Process lifetime: Shell Launcher only watches the process it started

- **`UIAccessLauncher.TryHandOff`** (`Cairo Desktop/UIAccessLauncher.cs`): return the started `Process`. In Shell Launcher mode, `Program.cs` waits for that process and exits with its exit code. That turns the original exe into a small supervisor, so code 1 or 3 reaches Shell Launcher. The command-line arguments are already passed through by `GetArguments()`.
- **`RestartCairo()`** (`CairoApplication.xaml.cs:245`): in Shell Launcher mode, don't start a new process. Restore the tiled windows and shut down with exit code **3**.
- **`OnExit`** currently forces exit code 1 in shell mode. Change it to keep a requested exit code (3), and otherwise use 1. The render-thread failure path and the user-restart path call `RestartCairo` and then `FailFast`. Under Shell Launcher, use the exit-code-3 shutdown instead.
- **Crash-loop guard:** at startup in Shell Launcher mode, add a timestamp to `%LOCALAPPDATA%\Cairo Desktop\starts.txt`. If there are 3 or more starts within 60 s, show a `CairoMessage` ("Cairo keeps crashing — opening Task Manager"), start `taskmgr.exe`, and exit with 1 (`DoNothing`). A shutdown with exit code 3 doesn't count as a crash, because the restarted process is started without `/restart`. Put the guard in a new small class `Cairo Desktop/SupportingClasses/CrashLoopGuard.cs`.

### 4. Things Shell Launcher already handles

- `StartupRunner` (`CairoApplication.xaml.cs:105`): skip it when `ShellHost.IsShellLauncher`. Shell Launcher already runs the `Run` and `RunOnce` keys, so they would otherwise start twice.

### 5. Settings and commands that depend on Explorer's UWP support

- Switch the gates from `IsAppRunningAsShell` to `ShellHost.HasImmersiveShell` in:
  - `OpenWindowsSettingsCommand.cs:29`
  - `ActionCenterMenuBarExtension.cs:20`
  - `OpenDisplayControlPanelCommand.cs:29`
  - the two `SettingsUI.xaml.cs` branches at 468 and 472, which decide which extras checkboxes show
- `VolumeMenuBarExtension.cs:21` stays shell-only: Explorer's volume icon isn't there under CustomShellHost.
- **Settings → Advanced, "Change shell" (`btnChangeShell_Click`, `SettingsUI.xaml.cs:780`):** replace the Winlogon toggle with a Shell Launcher status line, plus one button that runs `Enable-` or `Disable-CairoShellLauncher.ps1` elevated (`runas`). Ship the scripts with the installer, so the NSIS script copies them to `$INSTDIR`.
  - `IsAppConfiguredAsShell` (`checkRunAtLogOn`, line 650) becomes "configured through Shell Launcher". Detect that cheaply, without SYSTEM: the profile marker `HKCU\Software\CairoShell\ShellLauncher=1`, written by the Enable script and removed by the Disable script.
- **Localization:** add the new strings to `DisplayString.cs`/`Language.cs`, following the existing pattern.

### 6. Tray (only if the spike shows CustomShellHost owns `Shell_TrayWnd` and Cairo gets no icons)

- Find out whether ManagedShell 0.0.358's `TrayService` can take over first: it creates a topmost `Shell_TrayWnd` and re-sends `TaskbarCreated`.
- If it can't, record that as a known limitation instead of building a workaround now.

### 7. Setup runs at install and first startup, not by hand

The scripts from step 1 become the engine. The installer runs them, and Cairo checks the result on first startup. Running the scripts by hand is only for development and recovery.

**What the installer has today** (`Installer/CairoShell.nsi`):
- It runs elevated (`RequestExecutionLevel Admin`).
- It ships a self-contained .NET build (`Build-Installer.ps1`), so no .NET runtime check is needed. The `net480` build still runs `AbortIfBadFramework`.
- Its components are: Cairo (required); `startupCU`, which writes the HKCU `Run` value `CairoShell` and is on by default; and `shellCU`, which writes the HKCU Winlogon `Shell` value.
- Uninstall deletes the `Run` and Winlogon values, then removes `$INSTDIR`.

**Installer changes:**
- **Replace the components with one choice on a custom nsDialogs page:**
  - **"Use Cairo as my shell (replaces Explorer)"**, through Shell Launcher.
  - **"Run Cairo alongside Explorer at sign-in"**, through the `Run` key.
  - **"Don't start Cairo automatically"**.
  - Remember the choice in `HKLM\Software\CairoShell\StartMode`. On an upgrade, or with `/S` (silent, which `Deploy-Cairo.ps1` uses), reuse the saved choice. A silent install never shows a prompt.
- **Environment checks before Next is allowed.** These run in a bundled read-only `Test-CairoEnvironment.ps1`. It returns JSON, which the page shows as a pass/warn/fail list:
  - **Windows edition:** Enterprise, Education or IoT Enterprise, by `EditionID`. Otherwise "Use Cairo as my shell" is disabled with the reason, and the other two choices stay available. Pro and Home have no supported way to run UWP apps without Explorer, which is why the old Winlogon method goes away.
  - **Target account:** the user signed in at the console, not the elevated account. An admin entering their password over a standard user's session would otherwise configure the wrong person. Find the user with `Win32_ComputerSystem.UserName`, resolve its SID, and put `<Account Sid="..."/>` in the XML; the XSD allows `Sid`. Show "Cairo will be the shell for DOMAIN\user".
  - **Existing assigned-access or MDM policy:** if `MDM_AssignedAccess` already has a kiosk `Configuration`, or a `ShellLauncher` value that Cairo didn't write, warn that it will be replaced, or that MDM (Intune) may overwrite it. Leave the choice to the user.
  - **Shell Launcher feature state:** enabled, not enabled, or reboot pending.
  - **UIAccess signature:** `Get-AuthenticodeSignature` on `CairoDesktop.UIAccess.exe`. If it isn't `Valid` on this machine (the local certificate isn't trusted here), warn that Cairo can't manage windows running as administrator. This is a warning, not a blocker. If the Phase 0 spike finds that CustomShellHost needs a signed `CairoDesktop.exe`, this check covers the main exe too and blocks the shell choice.
  - **Leftovers from earlier installs:** an HKCU or HKLM Winlogon `Shell` value pointing at Cairo, and an HKCU `Run` value `CairoShell`. Remove both when "Use Cairo as my shell" is chosen. Shell Launcher runs the `Run` keys before the shell, so a leftover `Run` value would start Cairo twice.
- **Apply after the files are installed:**
  - Shell choice: `ExecWait` `Enable-CairoShellLauncher.ps1 -Sid <sid>`, which enables the feature and writes the XML as SYSTEM.
    - If DISM returns 3010 (reboot required), call `SetRebootFlag true` and change the finish page to "Restart to finish". Microsoft's docs say writing through the AssignedAccess CSP enables the feature by itself. Phase 0 checks whether the WMI bridge does the same; if so, skip the separate DISM call.
    - The Win+L fix that `WorkstationLockManager.FixPolicyAccessAsync` asks admin approval for (the ACL on HKCU `Policies\System`) happens here too. The installer is already elevated, so first startup needs no second UAC prompt.
  - "Alongside Explorer" choice: write the `Run` value, and make sure no Shell Launcher config for this user remains.
  - **Finish page:**
    - Shell choice: "Sign out now" / "Later", instead of "Run Cairo". Starting Cairo in place of Explorer in the current session isn't what Shell Launcher does.
    - Other choices: keep "Run Cairo".
- **Uninstall:** run `Disable-CairoShellLauncher.ps1` *before* removing `$INSTDIR`. Otherwise the next sign-in points Shell Launcher at a missing exe, which leaves a black screen and a restart loop. Also delete `StartMode` and the profile marker `HKCU\Software\CairoShell\ShellLauncher`.
- **Ship the scripts:** `Enable-`, `Disable-`, `Test-CairoEnvironment.ps1`. Put them in `$INSTDIR\Setup\`, and have the installer call them with `-ExecutionPolicy Bypass`.

**First startup:**
- Extend the first-run `Welcome` window (`Cairo Desktop/Welcome.xaml(.cs)`, which already clears `Settings.IsFirstRun`) with a **"Setup check"** step. It also opens on any startup where the check fails, until the user dismisses it. It runs the same `Test-CairoEnvironment.ps1` checks, plus things only Cairo can see:
  - **Shell choice made, but still under Explorer** (signed in before signing out): "Sign out to start Cairo as your shell", with a Sign out button.
  - **Shell Launcher configured, but Cairo wasn't started by it** (`ShellHost.IsShellLauncher` is false while the marker is set): offer to re-apply the config.
  - **Running under Shell Launcher, but the config targets a different account or SID:** offer to re-apply.
  - **The Win+L policy is still unwritable:** the existing `LockPolicyPrompt` already covers this, so fold it into this list instead of a separate popup.
  - **Duplicate `Run` value while under Shell Launcher:** offer to remove it.
- Each fix runs the matching bundled script elevated (`runas`), the same way `WorkstationLockManager.FixPolicyAccessAsync` does it.
- The Settings → Advanced button from step 5 opens this same check, so changing modes later uses one code path.

**Research to finish before building this:**
- NSIS: is a custom page with a dynamic checklist easier with `nsDialogs` + `nsJSON`, or by having PowerShell write an INI file that NSIS reads with `ReadINIStr`? (The INI route avoids adding plugins.)
- When the WMI bridge write auto-enables the feature, does it need a reboot before the first sign-in? (Phase 0.)
- Does `Win32_ComputerSystem.UserName` give the right user under RDP or fast user switching? Fallback: `WTSQuerySessionInformation` on the active console session.
- Does a machine enrolled in MDM let a local MDM-bridge write stand, or reset it at the next sync?

### 8. Lone Win tap does nothing

- `LowLevelKeyboardListener` (`CairoDesktop.Common/LowLevelKeyboardListener.cs`) already masks Win with `SendMaskKey()` after a hotkey it swallowed. Add an opt-in `MaskLoneWin` flag: when Win goes up and nothing else was pressed since Win went down (`keysPressed.Count == 1`), send the mask key before passing the key-up on. Then Start never opens.
- Also swallow Ctrl+Esc when the flag is on.
- `TilingHotKeyService.Register()` keeps a listener alive while `ShellHost.IsShellLauncher`, even when there are no hooked entries, and sets the flag.
- Win+drag (`WindowDragger`) already masks Win on its own.

---

## Phase 2: Tiling UWP apps

Start with a short spike: run `Get-ShellDiagnostics.ps1 -Uwp`, then open, minimize, switch tags, switch virtual desktops, and close Calculator, Settings and Store. Log the window events (Shown, Hidden, Cloaked, Uncloaked, Destroyed) for each `ApplicationFrameWindow`. The steps below have their fallbacks marked; the spike decides between them.

1. **Filter** (`CairoDesktop.Tiling/WindowFilter.cs`):
   - Manage an `ApplicationFrameWindow` when it isn't cloaked and has a `Windows.UI.Core.CoreWindow` child or a title. Leftover frames have neither.
   - `Windows.UI.Core.CoreWindow` and `ApplicationFrameInputSinkWindow` stay ignored.
2. **Identity** (`ManagedWindow.cs`):
   - For frame windows, find the hosted app from the child `CoreWindow`'s process (its exe name). Also read its AUMID with `SHGetPropertyStoreForWindow` + `PKEY_AppUserModel_ID`; the interop goes in `CairoDesktop.Interop`.
   - Re-resolve it on `NameChanged`, because the child is attached after the frame appears.
   - Float rules (`TilingFloatExes`) then match `Calculator.exe` or the AUMID instead of `ApplicationFrameHost.exe`.
3. **Icons** (`WindowIconHelper.cs:26`, currently a generic icon): load the icon for `shell:AppsFolder\<AUMID>` through `IShellItemImageFactory`, and cache it per AUMID.
4. **Hiding on tag switches** (`WindowArranger.Hide/Show`):
   - Try `SW_HIDE`/`SW_SHOWNA` first.
   - If the spike shows a blank or suspended frame, or a lost window, add `ManagedWindow.HideMode = Offscreen` for frame windows. Park them at (-32000, -32000), and record their original rect in `HiddenWindowJournal` so `RestoreAll()` puts them back after a crash.
5. **Windows hidden by the system:** today `Cloaked` → `Unmanage` (`TilingManager.Listener_WindowEvent`). That makes a UWP window lose its tag whenever it suspends or moves to another virtual desktop.
   - For a window that is managed and still exists, treat a cloak like a minimize: remember its state, `SetState(Minimized)`, and keep it out of the window list. On `Uncloaked`, put the old state back.
   - Unmanage on `Destroyed`, or when an uncloak check finds the `CoreWindow` child gone.
   - Track this as `ManagedWindow.IsSystemCloaked`, so it's separate from cloaks the tiler causes itself.
6. **Fullscreen:** `UpdateAutoFullscreen` requires a window with no caption. UWP frames keep `WS_CAPTION` when an app goes fullscreen (F11, or "full screen" in Settings), so for frame windows treat covering the whole monitor as fullscreen.
7. **Launching** (`AppGrabberService.LaunchProgram`, ~line 390):
   - When `ShellHost.HasImmersiveShell`, start every Store app through `ShellHelper.ActivateApplication(app.Target)`, not only full-trust ones. The existing comment ("Non-FullTrust apps ... will hang us") only applies without UWP support.
   - Call it on a background thread, so a slow activation can't freeze the UI.
8. Remove the now-stale UWP note in `plan.md` (old line 527) once 4 and 5 are settled.

## Known limitations to document (not fixed in this plan)

- Toast notifications don't work under CustomShellHost (community report).
- Network and volume tray icons come from Explorer, so they're missing. Cairo's own Volume extra covers volume.
- Virtual desktops work, but the tiler treats windows on other desktops like minimized ones (Phase 2 step 5). It has no per-desktop tags.

## Critical files

- **New:**
  - scripts: `Enable-CairoShellLauncher.ps1`, `Disable-CairoShellLauncher.ps1`, `Test-CairoEnvironment.ps1`, `Deploy-Cairo.ps1`, `Get-ShellDiagnostics.ps1`
  - the installer's start-mode/environment-check page, and the "Setup check" step in `Welcome.xaml(.cs)`
  - `CairoDesktop.Common/ShellHost.cs`
  - `Cairo Desktop/SupportingClasses/CrashLoopGuard.cs`
- **Delete:** `Start-CairoShell.ps1`
- **Edit:**
  - startup and lifetime: `Program.cs`, `UIAccessLauncher.cs`, `CairoApplication.xaml.cs`, `CairoApplicationInitializationService.InitializationRoutines.cs`, the command-line options class
  - settings and gates: `SettingsUI.xaml(.cs)`, `OpenWindowsSettingsCommand.cs`, `OpenDisplayControlPanelCommand.cs`, `ActionCenterMenuBarExtension.cs`
  - keyboard: `LowLevelKeyboardListener.cs`, `TilingHotKeyService.cs`
  - packaging and docs: `Installer/CairoShell*.nsi`, `README.md`, `plan.md`
  - Phase 2: `WindowFilter.cs`, `ManagedWindow.cs`, `WindowIconHelper.cs`, `WindowArranger.cs`, `HiddenWindowJournal.cs`, `TilingManager.cs`, `AppGrabberService.cs`, `CairoDesktop.Interop/User32.cs` (or a new `Shell32.cs`)

**Reuse:**
- `SendMaskKey`/`VK_MASK` in `LowLevelKeyboardListener`
- `HiddenWindowJournal.RestoreAll()` for crash recovery
- `TilingManager.MarkDirty`/`SetState` for cloak-as-minimize
- `ShellHelper.ActivateApplication` (ManagedShell)
- the `Build-Installer.ps1` signing helpers (`New-UIAccessCopy`)
- the TagSet `Minimized` state

## Verification

**Build:** `dotnet build` the solution and run the existing `Tests/CairoDesktop.Tiling.Core.Tests`.

**Phase 1:**
1. `Deploy-Cairo.ps1`, then `Enable-CairoShellLauncher.ps1`, then sign out and back in. Cairo comes up with no Explorer. `Get-ShellDiagnostics.ps1` shows Cairo's parent is the supervisor, whose parent is CustomShellHost.
2. Startup apps from the `Run` key start exactly once.
3. Cairo menu → Restart: Cairo exits with 3 and Shell Launcher starts it again. Tiled windows come back.
4. Kill `CairoDesktop.UIAccess.exe` from Task Manager: it restarts (crash → `RestartShell`). Kill it 3 times quickly: the crash-loop guard opens Task Manager and Cairo stays down.
5. Tapping Win alone does nothing, and Ctrl+Esc does nothing. Win+1…9, Win+R and Win+drag still work.
6. `ms-settings:`, the Settings command, Calculator and the Store open. Action Center opens from the menu bar.
7. `Disable-CairoShellLauncher.ps1`, then sign out and back in: Explorer is back. The recovery path from Task Manager works.
8. **Installer:**
   - Fresh install with "Use Cairo as my shell": the checklist shows the edition, the account (your console user) and the signature, the finish page offers Sign out, and Cairo is the shell after sign-in.
   - Reinstall with `/S`: the mode is kept, with no prompts.
   - Install with "Alongside Explorer": only the `Run` value is written, and no Shell Launcher config exists.
   - On a Pro VM: the shell choice is disabled, with the reason shown.
   - Uninstall: the Shell Launcher config is removed before the files, and the next sign-in gets Explorer.
9. **First startup:** install with the shell choice, but don't sign out. Starting Cairo shows the Setup check with "Sign out to start Cairo as your shell". After sign-in, the check passes and doesn't appear again.

**Phase 2:**
- With Calculator, Settings and Notepad on one tag: tile, swap, resize, Win+drag, float, and move each to another tag and another monitor.
- Switch tags back and forth: UWP windows come back drawn and responsive.
- Minimize and restore. Switch virtual desktops and come back: the windows keep their tags.
- Close a UWP app: it leaves the window list, and no ghost frame stays behind.
- F11 in a UWP app goes fullscreen.
- The window list shows the real app icon and name.
- A float rule on `Calculator.exe` floats it.
- Kill Cairo while a UWP window is parked or hidden: the next start restores it.
