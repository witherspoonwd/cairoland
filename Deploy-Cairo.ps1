<#
.SYNOPSIS
    The dev loop: builds the installer, stops Cairo, installs the new build silently, and starts it again.

.DESCRIPTION
    1. Builds the installer with Build-Installer.ps1 (parameters are passed through).
    2. Force-kills CairoDesktop.exe and CairoDesktop.UIAccess.exe. A forced kill exits with code 1, which Shell
       Launcher maps to DoNothing, so it doesn't restart Cairo while the files are being replaced. Windows the
       tiler had hidden come back on the next start (HiddenWindowJournal.RestoreAll).
    3. Runs the installer silently (/S). It keeps the start mode chosen at the last interactive install.
    4. Starts the installed Cairo with the same arguments as the Shell Launcher configuration. Shell Launcher
       doesn't watch this process (it only watches what it started), so a crash isn't restarted until you sign
       out and back in.

    Run it from a normal (non-elevated) PowerShell; the installer asks for administrator approval itself.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Deploy-Cairo.ps1

.EXAMPLE
    .\Deploy-Cairo.ps1 -NoPublish
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86', 'x64_net480', 'x86_net480', 'arm64')]
    [string]$PublishProfile = 'x64',

    # Reuse the existing publish output.
    [switch]$NoPublish,

    # Leave out CairoDesktop.UIAccess.exe.
    [switch]$NoUIAccess,

    # Install, but don't start Cairo.
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
Import-Module (Join-Path $repoRoot 'CairoShellLauncher.psm1') -Force

if (Test-Elevated) {
    Write-Error 'This PowerShell is running as administrator, and Cairo would start elevated. Run the script from a normal PowerShell.'
}

# 1. Build
$buildParameters = @{ PublishProfile = $PublishProfile }
if ($NoPublish) { $buildParameters.NoPublish = $true }
if ($NoUIAccess) { $buildParameters.NoUIAccess = $true }
& (Join-Path $repoRoot 'Build-Installer.ps1') @buildParameters

$installer = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts\installer') -Filter '*.exe' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $installer) {
    Write-Error 'No installer found in artifacts\installer.'
}

# 2. Stop Cairo (exit code 1 = DoNothing, so Shell Launcher leaves it down)
foreach ($name in @('CairoDesktop.UIAccess.exe', 'CairoDesktop.exe')) {
    & taskkill.exe /F /IM $name 2>&1 | Out-Null
}

$deadline = (Get-Date).AddSeconds(10)
while ((Get-Process -Name 'CairoDesktop', 'CairoDesktop.UIAccess' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 200
}

# 3. Install
Write-Host "Installing $($installer.Name)..."
$setup = Start-Process -FilePath $installer.FullName -ArgumentList '/S' -Verb RunAs -Wait -PassThru
if ($setup.ExitCode -ne 0) {
    Write-Error "The installer failed (exit code $($setup.ExitCode))."
}

# 4. Start
if ($NoStart) {
    return
}

$installDir = Get-CairoInstallDir
if (-not $installDir) {
    Write-Error 'Cairo is installed, but HKLM\Software\CairoShell\Install_Dir was not found.'
}

if (Get-Process -Name 'CairoDesktop', 'CairoDesktop.UIAccess' -ErrorAction SilentlyContinue) {
    Write-Host 'Cairo is already running (the installer started it).'
    return
}

$exe = Join-Path $installDir 'CairoDesktop.exe'
$explorerRunning = [bool](Get-Process -Name explorer -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq (Get-Process -Id $PID).SessionId })
if ($explorerRunning) {
    Write-Host "Explorer is the shell in this session; starting Cairo alongside it."
    Start-Process -FilePath $exe -WorkingDirectory $installDir
}
else {
    Start-Process -FilePath $exe -ArgumentList '/shell=true', '/shelllauncher=true' -WorkingDirectory $installDir
    Write-Host 'Started Cairo as the shell. Shell Launcher does not watch this process until you sign out and back in.' -ForegroundColor Yellow
}
