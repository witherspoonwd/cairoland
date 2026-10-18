<#
.SYNOPSIS
    Removes Cairo's Shell Launcher configuration, so Explorer is the shell again from the next sign-in.

.DESCRIPTION
    Clears the ShellLauncher property of MDM_AssignedAccess as SYSTEM (through a one-shot scheduled task) and
    removes the HKCU marker Cairo reads. The Shell Launcher features stay enabled. The uninstaller runs this
    before removing Cairo's files, so no sign-in points Shell Launcher at a missing exe.

    RECOVERY, if Cairo won't start or keeps crashing:
      1. Press Ctrl+Shift+Esc to open Task Manager (Cairo also opens it after three crashes in a minute).
      2. File > Run new task, tick "Create this task with administrative privileges", and run:
           powershell -ExecutionPolicy Bypass -File "C:\Program Files\Cairo Shell\Setup\Disable-CairoShellLauncher.ps1"
      3. In the same session you can also run explorer.exe from there. Sign out and back in to get Explorer.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Disable-CairoShellLauncher.ps1
#>
[CmdletBinding()]
param(
    # The account whose marker is removed (name or SID). Defaults to the console user.
    [string]$User,

    [string]$Sid,

    # Don't ask to sign out at the end.
    [switch]$NoSignOutPrompt,

    # Leave HKLM\Software\CairoShell\StartMode alone (the uninstaller removes it anyway).
    [switch]$KeepStartMode
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CairoShellLauncher.psm1') -Force

if (-not (Test-Elevated)) {
    Write-Host 'Asking for administrator approval...'
    exit (Invoke-SelfElevated $PSCommandPath $PSBoundParameters)
}

$started = Get-Date

try {
    if ($Sid) { $account = Resolve-Account $Sid }
    elseif ($User) { $account = Resolve-Account $User }
    else { $account = Get-ConsoleUser }

    Unregister-ShellLauncherConfigAtStartup

    if ((Get-ShellLauncherFeatureState) -eq 'Enabled') {
        $existing = Get-AssignedAccessConfig
        if ($existing -and $existing.ShellLauncher -and -not (Test-IsCairoShellLauncherXml $existing.ShellLauncher)) {
            Write-Warning 'The Shell Launcher configuration on this computer was not written by Cairo; clearing it anyway.'
        }

        Write-Host 'Clearing the Shell Launcher configuration as SYSTEM...'
        $readBack = Set-ShellLauncherConfig $null
        if ($readBack) {
            throw 'The Shell Launcher configuration is still there after clearing it.'
        }
    }
    else {
        Write-Host 'Shell Launcher is not enabled; nothing to clear.'
    }

    Set-UserMarker $account.Sid $false
    if (-not $KeepStartMode) {
        Set-CairoStartMode 'Manual'
    }
}
catch {
    Write-Host "Failed: $($_.Exception.Message)" -ForegroundColor Red
    foreach ($event in (Get-AssignedAccessEvents $started)) {
        Write-Host "[$($event.TimeCreated)] $($event.LevelDisplayName): $($event.Message)" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Done. $($account.Name) gets Explorer at the next sign-in." -ForegroundColor Green
if (-not $NoSignOutPrompt -and [Environment]::UserInteractive) {
    $answer = Read-Host 'Sign out now? [y/N]'
    if ($answer -match '^(y|yes)$') {
        & shutdown.exe /l
    }
}

exit 0
