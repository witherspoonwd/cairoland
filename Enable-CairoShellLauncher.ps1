<#
.SYNOPSIS
    Makes Cairo the shell for one account through Shell Launcher v2 (Windows Enterprise, Education, IoT Enterprise).

.DESCRIPTION
    1. Enables the Client-DeviceLockdown and Client-EmbeddedShellLauncher optional features.
    2. Writes the Shell Launcher XML as SYSTEM (through a one-shot scheduled task):
       - everyone else keeps Explorer (DefaultProfile);
       - the account gets "<InstallDir>\CairoDesktop.exe" /shell=true /shelllauncher=true. Exit code 1 means
         Cairo stopped on purpose (DoNothing), 3 asks for a restart, anything else is a crash (RestartShell).
    3. Removes leftovers from the old ways of becoming the shell (Winlogon Shell, the CairoShell Run value).
    4. Sets the HKCU marker Cairo reads (HKCU\Software\CairoShell\ShellLauncher=1).

    Takes effect at the account's next sign-in. The installer runs this script; running it by hand is for
    development and recovery. Undo it with Disable-CairoShellLauncher.ps1.

    Exit codes: 0 done, 3010 done but Windows must restart first, anything else failed.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Enable-CairoShellLauncher.ps1

.EXAMPLE
    .\Enable-CairoShellLauncher.ps1 -User .\someone -CairoExe 'C:\dev\publish\CairoDesktop.exe'
#>
[CmdletBinding()]
param(
    # The account Cairo becomes the shell for (name like .\user or DOMAIN\user). Defaults to the console user.
    [string]$User,

    # The account's SID; takes precedence over -User. The installer passes this.
    [string]$Sid,

    # Cairo's exe. Defaults to the installed one (HKLM\Software\CairoShell\Install_Dir).
    [string]$CairoExe,

    # Also make HKCU\...\Policies\System writable for the account, so Cairo can take over Win+L.
    [switch]$FixLockPolicy,

    # Don't ask to sign out at the end (the installer has its own finish page).
    [switch]$NoSignOutPrompt
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CairoShellLauncher.psm1') -Force

if (-not (Test-Elevated)) {
    Write-Host 'Asking for administrator approval...'
    exit (Invoke-SelfElevated $PSCommandPath $PSBoundParameters)
}

$started = Get-Date

try {
    # Who and what
    if ($Sid) { $account = Resolve-Account $Sid }
    elseif ($User) { $account = Resolve-Account $User }
    else { $account = Get-ConsoleUser }
    Write-Host "Cairo will be the shell for $($account.Name) ($($account.Sid))"

    if (-not $CairoExe) {
        $installDir = Get-CairoInstallDir
        if (-not $installDir) {
            throw 'Cairo is not installed (HKLM\Software\CairoShell\Install_Dir not found). Install it, or pass -CairoExe.'
        }
        $CairoExe = Join-Path $installDir 'CairoDesktop.exe'
    }
    $CairoExe = (Resolve-Path -LiteralPath $CairoExe).Path
    Write-Host "Shell: `"$CairoExe`" /shell=true /shelllauncher=true"

    # Features
    $restartNeeded = $false
    $featureState = Get-ShellLauncherFeatureState
    if ($featureState -ne 'Enabled') {
        Write-Host 'Enabling Shell Launcher...'
        $restartNeeded = (Enable-ShellLauncherFeature) -or ((Get-ShellLauncherFeatureState) -ne 'Enabled')
    }

    $xml = New-ShellLauncherXml -CairoExe $CairoExe -Sid $account.Sid

    if ($restartNeeded) {
        # the WMI bridge may not accept the XML until the features are live; write it at the next startup
        Register-ShellLauncherConfigAtStartup $xml
        Write-Host 'Windows must restart to finish enabling Shell Launcher. The configuration is applied at the next startup.' -ForegroundColor Yellow
    }
    else {
        $existing = Get-AssignedAccessConfig
        if ($existing -and $existing.Configuration) {
            Write-Warning 'An assigned access (kiosk) configuration exists on this computer. It is left as is, but it may take precedence over Shell Launcher.'
        }
        if ($existing -and $existing.ShellLauncher -and -not (Test-IsCairoShellLauncherXml $existing.ShellLauncher)) {
            Write-Warning 'Replacing a Shell Launcher configuration that Cairo did not write.'
        }

        Write-Host 'Writing the Shell Launcher configuration as SYSTEM...'
        $readBack = Set-ShellLauncherConfig $xml
        if (-not (Test-IsCairoShellLauncherXml $readBack)) {
            throw 'The Shell Launcher configuration did not stick (reading it back returned something else).'
        }
        Unregister-ShellLauncherConfigAtStartup
    }

    Remove-CairoLeftovers $account.Sid
    Set-UserMarker $account.Sid $true
    Set-CairoStartMode 'Shell'

    if ($FixLockPolicy) {
        Grant-LockPolicyAccess $account.Sid
        Write-Host 'Win+L policy key is writable for the account.'
    }
}
catch {
    Write-Host "Failed: $($_.Exception.Message)" -ForegroundColor Red
    $events = Get-AssignedAccessEvents $started
    foreach ($event in $events) {
        Write-Host "[$($event.TimeCreated)] $($event.LevelDisplayName): $($event.Message)" -ForegroundColor Red
    }
    exit 1
}

$events = Get-AssignedAccessEvents $started
foreach ($event in $events) {
    Write-Host "[$($event.TimeCreated)] $($event.LevelDisplayName): $($event.Message)"
}

if ($restartNeeded) {
    exit 3010
}

Write-Host 'Done. Cairo becomes the shell at the next sign-in.' -ForegroundColor Green
if (-not $NoSignOutPrompt -and [Environment]::UserInteractive) {
    $answer = Read-Host 'Sign out now? [y/N]'
    if ($answer -match '^(y|yes)$') {
        & shutdown.exe /l
    }
}

exit 0
