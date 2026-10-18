# Cairo Desktop Environment
[![Current release](https://img.shields.io/github/v/release/cairoshell/cairoshell?include_prereleases)](https://github.com/cairoshell/cairoshell/releases) ![Build status](https://github.com/cairoshell/cairoshell/workflows/Build/badge.svg)

![Cairo Desktop Environment](http://cairoshell.github.io/img/ss_dynDesk.jpg)

## A customizable, intuitive desktop environment for Windows.
Cairo is a desktop environment for Windows. Our goal is to develop a desktop experience that increases productivity and advances current technology standards. With a focus on stability, performance, and productivity, Cairo aims to deliver an interface that exceeds expectations and delivers superior ease of use.

## Download
The latest version of Cairo can be downloaded **[on the Releases page](https://github.com/cairoshell/cairoshell/releases/latest)**.

## Requirements
Windows 7 SP1, Windows 8.1, Windows 10, or Windows 11

## Using Cairo as your shell

Cairo replaces Explorer through **Shell Launcher v2**, which is part of Windows 10/11 Enterprise, Education and IoT Enterprise. Unlike the old Winlogon `Shell` setting, Shell Launcher keeps Store apps, Settings and Action Center working. On Pro and Home, Cairo runs alongside Explorer instead.

1. **Once per machine:** run `New-CairoSigningCertificate.ps1` (as administrator). It signs `CairoDesktop.UIAccess.exe`, which lets Cairo manage windows running as administrator.
2. **Build and run the installer** (`Build-Installer.ps1`, then the setup exe in `artifacts\installer`).
3. On the **"How should Cairo start?"** page, pick **Use Cairo as my shell (replaces Explorer)**. The page lists what it checked: the Windows edition, the account Cairo will be the shell for (the user signed in at the console), Shell Launcher's state, the UI access signature, and leftovers from older installs.
4. **Sign out** from the finish page (or restart, if Windows asks to finish enabling Shell Launcher). Cairo starts in place of Explorer at the next sign-in.

The installer does the work with the scripts in `C:\Program Files\Cairo Shell\Setup`. Running them by hand is for development and recovery:

| Script | What it does |
|---|---|
| `Enable-CairoShellLauncher.ps1` | Enables Shell Launcher and makes Cairo the shell for an account (default: the console user). Takes effect at the next sign-in. |
| `Disable-CairoShellLauncher.ps1` | Removes Cairo's Shell Launcher configuration, so Explorer comes back at the next sign-in. |
| `Test-CairoEnvironment.ps1` | Read-only checks, as JSON. Used by the installer and by Cairo's Setup check. |
| `Deploy-Cairo.ps1` (repo only) | Dev loop: builds the installer, stops Cairo, installs silently (keeping the start mode) and starts it again. |
| `Get-ShellDiagnostics.ps1` (repo only) | Dumps who owns the shell and taskbar windows, the process tree, and (with `-Uwp`, `-WatchSeconds`) how UWP frame windows behave. |

Cairo checks its own setup at startup and opens a **Setup check** when something needs attention (for example "Sign out to start Cairo as your shell"). Settings > Advanced > Shell setup opens the same check, which is also where you switch between Cairo and Explorer.

### If Cairo won't start

Cairo restarts by itself after a crash. If it crashes three times within a minute, it opens Task Manager and stays down. Then:

1. In Task Manager, choose **File > Run new task**, tick **Create this task with administrative privileges**, and run
   `powershell -ExecutionPolicy Bypass -File "C:\Program Files\Cairo Shell\Setup\Disable-CairoShellLauncher.ps1"`.
2. Run `explorer.exe` the same way to get a shell for this session, then sign out and back in.

### Known limitations

- Toast notifications reportedly don't appear under Shell Launcher.
- The network and volume tray icons come from Explorer, so they're missing. Cairo's Volume menu extra covers volume.
- Virtual desktops work, but the window manager treats windows on other desktops like minimized ones; tags aren't per desktop.

## More information
- For an overview of Cairo features, [visit our website](https://cairodesktop.com/).
- Further information for advanced users and developers is available [on our wiki](https://github.com/cairoshell/cairoshell/wiki).

### Powered by
[![JetBrains logo.](https://resources.jetbrains.com/storage/products/company/brand/logos/jetbrains.svg)](https://jb.gg/OpenSource)
