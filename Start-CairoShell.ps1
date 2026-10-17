<#
.SYNOPSIS
    Runs this Cairo build as the Windows shell for the current session, in place of Explorer.

.DESCRIPTION
    Builds Cairo, stops Explorer (taskbar and desktop), starts Cairo with /shell=true, and waits.
    When Cairo exits (Cairo menu > Exit Cairo) or crashes, Explorer is started again.

    Nothing is installed and the registry shell setting is not changed; logging off or rebooting
    returns you to Explorer.

    If something goes wrong and you are left without a shell: press Ctrl+Shift+Esc to open Task
    Manager, choose "Run new task", and run explorer.exe.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Start-CairoShell.ps1

.EXAMPLE
    .\Start-CairoShell.ps1 -NoBuild -Force
#>
[CmdletBinding()]
param(
    [ValidateSet('net10.0-windows', 'net6.0-windows', 'net480')]
    [string]$Framework = 'net10.0-windows',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # Run the existing build without rebuilding.
    [switch]$NoBuild,

    # Don't ask for confirmation before stopping Explorer.
    [switch]$Force,

    # Leave Explorer stopped when Cairo exits.
    [switch]$NoExplorerRestore
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'Cairo Desktop\Cairo Desktop\Cairo Desktop.csproj'
$outputDir = Join-Path $repoRoot "Cairo Desktop\Cairo Desktop\bin\x64\$Configuration\$Framework\win-x64"
$exe = Join-Path $outputDir 'CairoDesktop.exe'
$explorerExe = Join-Path $env:WINDIR 'explorer.exe'

function Start-Explorer {
    if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) {
        Write-Host 'Starting Explorer...'
        Start-Process -FilePath $explorerExe
    }
}

# A shell should not run elevated: every program started from it would be elevated too.
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'This PowerShell is running as administrator. Run the script from a normal (non-elevated) PowerShell.'
}

if (Get-Process -Name CairoDesktop -ErrorAction SilentlyContinue) {
    Write-Error 'Cairo is already running. Exit it first (Cairo menu > Exit Cairo).'
}

# Build
if (-not $NoBuild) {
    Write-Host "Building $Configuration $Framework..."
    & dotnet build $project -c $Configuration -f $Framework -p:Platform=x64 -nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed (exit code $LASTEXITCODE)."
    }
}

if (-not (Test-Path $exe)) {
    Write-Error "CairoDesktop.exe not found at $exe. Run without -NoBuild."
}

# The .NET SDK on this machine is installed per user (not under Program Files), so the exe can't find the
# runtime by itself. Point it at the dotnet that is on PATH. Not needed for the .NET Framework (net480) build.
if ($Framework -ne 'net480') {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Error 'dotnet was not found on PATH, so Cairo cannot find the .NET runtime. Try -Framework net480.'
    }
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet.Source
    Write-Host "Using .NET runtime from $env:DOTNET_ROOT"
}

if (-not $Force) {
    Write-Host ''
    Write-Host 'Explorer (taskbar and desktop) is about to stop, and Cairo will take over as the shell.' -ForegroundColor Yellow
    Write-Host 'Keep this window open: when Cairo exits, this script starts Explorer again.' -ForegroundColor Yellow
    Write-Host 'If you get stuck: Ctrl+Shift+Esc > Run new task > explorer.exe' -ForegroundColor Yellow
    Read-Host 'Press Enter to continue, or Ctrl+C to cancel'
}

try {
    # Stop Explorer. A forced kill exits with a non-zero code, so Winlogon does not restart it.
    Write-Host 'Stopping Explorer...'
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        Get-Process -Name explorer -ErrorAction SilentlyContinue | Stop-Process -Force
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Process -Name explorer -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 200
        }

        # make sure it doesn't come straight back
        Start-Sleep -Seconds 2
        if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) {
            break
        }
    }

    if (Get-Process -Name explorer -ErrorAction SilentlyContinue) {
        Write-Error 'Explorer keeps restarting, so Cairo cannot become the shell.'
    }

    Write-Host "Starting Cairo as the shell: $exe /shell=true"
    $cairo = Start-Process -FilePath $exe -ArgumentList '/shell=true' -WorkingDirectory $outputDir -PassThru
    Write-Host "Cairo is running (PID $($cairo.Id)). Waiting for it to exit..."
    $cairo.WaitForExit()

    # In shell mode Cairo exits with code 1 on purpose, so Winlogon doesn't restart a shell.
    $code = $cairo.ExitCode
    if ($code -eq 0 -or $code -eq 1) {
        Write-Host "Cairo exited (code $code)."
    }
    else {
        $logs = Join-Path $env:LOCALAPPDATA 'Cairo Desktop\Logs'
        Write-Warning "Cairo exited with code $code. Check the logs in $logs."
    }
}
finally {
    if (-not $NoExplorerRestore) {
        Start-Explorer
    }
}
