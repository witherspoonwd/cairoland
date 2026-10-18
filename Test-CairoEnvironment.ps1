<#
.SYNOPSIS
    Read-only checks for running Cairo as the shell through Shell Launcher v2. Changes nothing.

.DESCRIPTION
    Writes JSON to stdout (and, with -IniPath, an INI file the installer reads with ReadINIStr):
      - Summary: edition, whether "Use Cairo as my shell" is possible (and why not), the target account.
      - Checks: a list of { Id, Status (pass | warn | fail | info), Title, Detail, BlocksShell }.

    Used by the installer's start mode page and by Cairo's Setup check. Some checks need more rights than others:
    the assigned access configuration can only be read as SYSTEM, so it is checked only when this runs elevated.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Test-CairoEnvironment.ps1 | ConvertFrom-Json
#>
[CmdletBinding()]
param(
    # Folder holding the CairoDesktop.exe / CairoDesktop.UIAccess.exe to check. Defaults to the installed one.
    [string]$CairoDir,

    # The account to check for (name or SID). Defaults to the console user.
    [string]$User,

    # Also write the result as an INI file here.
    [string]$IniPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CairoShellLauncher.psm1') -Force

$checks = New-Object System.Collections.Generic.List[object]
function Add-Check([string]$Id, [string]$Status, [string]$Title, [string]$Detail = '', [bool]$BlocksShell = $false) {
    $checks.Add([pscustomobject]@{ Id = $Id; Status = $Status; Title = $Title; Detail = $Detail; BlocksShell = $BlocksShell })
}

function Split-Lines([string]$Text, [int]$Width) {
    $line = ''
    foreach ($word in ($Text -split '\s+' | Where-Object { $_ })) {
        if ($line.Length -gt 0 -and $line.Length + 1 + $word.Length -gt $Width) {
            $line
            $line = $word
        }
        elseif ($line.Length -gt 0) {
            $line += " $word"
        }
        else {
            $line = $word
        }
    }
    if ($line) { $line }
}

$elevated = Test-Elevated

# Windows edition: Shell Launcher only exists on Enterprise, Education and IoT Enterprise
$currentVersion = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$edition = $currentVersion.EditionID
$productName = $currentVersion.ProductName
$supportedEditions = @('Enterprise', 'EnterpriseN', 'EnterpriseS', 'EnterpriseSN', 'Education', 'EducationN', 'IoTEnterprise', 'IoTEnterpriseS')
$editionSupported = $supportedEditions -contains $edition
if ($editionSupported) {
    Add-Check 'edition' 'pass' "Windows edition: $productName" 'This edition has Shell Launcher v2.'
}
else {
    Add-Check 'edition' 'fail' "Windows edition: $productName" 'Shell Launcher needs Windows Enterprise, Education or IoT Enterprise. Pro and Home have no supported way to run Store apps without Explorer, so Cairo can only run alongside Explorer here.' $true
}

# Target account
$account = $null
try {
    if ($User) { $account = Resolve-Account $User } else { $account = Get-ConsoleUser }
    Add-Check 'account' 'pass' "Cairo will be the shell for $($account.Name)" "SID $($account.Sid), found from $($account.Source)."
}
catch {
    Add-Check 'account' 'fail' 'The signed-in user could not be determined' $_.Exception.Message $true
}

# Shell Launcher features
$featureState = 'Unknown'
if ($editionSupported) {
    try {
        $featureState = Get-ShellLauncherFeatureState
        switch ($featureState) {
            'Enabled' { Add-Check 'feature' 'pass' 'Shell Launcher is enabled' }
            'RebootPending' { Add-Check 'feature' 'warn' 'Shell Launcher is waiting for a restart' 'Restart Windows to finish enabling it. Setup can still configure it; it takes effect after the restart.' }
            default { Add-Check 'feature' 'info' 'Shell Launcher is not enabled yet' 'Setup enables it. Windows may need to restart.' }
        }
    }
    catch {
        Add-Check 'feature' 'warn' 'Shell Launcher state could not be read' $_.Exception.Message
    }
}

# Existing assigned access or MDM policy (readable only as SYSTEM)
if ($editionSupported -and $featureState -eq 'Enabled') {
    if ($elevated) {
        try {
            $existing = Get-AssignedAccessConfig
            $enrolled = @(Get-ChildItem -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Enrollments' -ErrorAction SilentlyContinue |
                Where-Object { (Get-ItemProperty -LiteralPath $_.PSPath -Name ProviderID -ErrorAction SilentlyContinue).ProviderID }).Count -gt 0

            if ($existing -and $existing.Configuration) {
                Add-Check 'assignedaccess' 'warn' 'A kiosk (assigned access) configuration exists' 'It is left as is, but it may take precedence over Shell Launcher for some accounts.'
            }

            if ($existing -and $existing.ShellLauncher -and -not (Test-IsCairoShellLauncherXml $existing.ShellLauncher)) {
                Add-Check 'shelllauncher' 'warn' 'Another Shell Launcher configuration exists' 'Setup replaces it with Cairo''s.'
            }
            elseif ($existing -and (Test-IsCairoShellLauncherXml $existing.ShellLauncher)) {
                if ($account -and $existing.ShellLauncher.Contains($account.Sid)) {
                    Add-Check 'shelllauncher' 'pass' 'Cairo''s Shell Launcher configuration is in place for this account'
                }
                else {
                    Add-Check 'shelllauncher' 'warn' 'Cairo''s Shell Launcher configuration targets a different account' 'Setup re-applies it for this account.'
                }
            }

            if ($enrolled) {
                Add-Check 'mdm' 'warn' 'This computer is managed (MDM, e.g. Intune)' 'A policy sync may overwrite or remove the local Shell Launcher configuration.'
            }
        }
        catch {
            Add-Check 'assignedaccess' 'warn' 'The existing Shell Launcher configuration could not be read' $_.Exception.Message
        }
    }
    else {
        Add-Check 'assignedaccess' 'info' 'Existing Shell Launcher configuration not checked' 'Reading it needs administrator rights.'
    }
}

# UI access signature
if (-not $CairoDir) {
    $CairoDir = Get-CairoInstallDir
}
if ($CairoDir) {
    $uiAccessExe = Join-Path $CairoDir 'CairoDesktop.UIAccess.exe'
    if (Test-Path -LiteralPath $uiAccessExe) {
        $signature = Get-AuthenticodeSignature -LiteralPath $uiAccessExe
        if ($signature.Status -eq 'Valid') {
            Add-Check 'uiaccess' 'pass' 'Cairo can manage windows running as administrator' "CairoDesktop.UIAccess.exe is signed by $($signature.SignerCertificate.Subject)."
        }
        else {
            Add-Check 'uiaccess' 'warn' 'Cairo can''t manage windows running as administrator' "CairoDesktop.UIAccess.exe's signature isn't trusted on this computer ($($signature.Status)). Run New-CairoSigningCertificate.ps1 here, or ignore this."
        }
    }
    else {
        Add-Check 'uiaccess' 'info' 'This build has no UI access copy' 'Cairo can''t manage windows running as administrator.'
    }
}

# Leftovers from earlier ways of becoming the shell
if ($account) {
    try {
        foreach ($leftover in (Get-CairoLeftovers $account.Sid)) {
            if ($leftover.Name -eq 'CairoShell') {
                Add-Check 'leftover-run' 'warn' 'Cairo starts from the Run key' 'If Cairo becomes the shell, setup removes this value so Cairo doesn''t start twice.'
            }
            else {
                Add-Check "leftover-winlogon-$($leftover.Hive.ToLower())" 'warn' "An old Winlogon shell setting points at Cairo ($($leftover.Hive.ToLower()))" "$($leftover.Value). Setup removes it."
            }
        }
    }
    catch {
        Add-Check 'leftover' 'info' 'Earlier Cairo settings could not be checked' $_.Exception.Message
    }
}

$shellBlockers = @($checks | Where-Object { $_.BlocksShell })
$result = [pscustomobject]@{
    Edition = $edition
    ProductName = $productName
    Elevated = $elevated
    ShellSupported = $shellBlockers.Count -eq 0
    ShellReason = ($shellBlockers | ForEach-Object { $_.Detail }) -join ' '
    AccountName = if ($account) { $account.Name } else { $null }
    AccountSid = if ($account) { $account.Sid } else { $null }
    FeatureState = $featureState
    Checks = $checks.ToArray()
}

if ($IniPath) {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('[Summary]')
    $lines.Add("ShellSupported=$([int]$result.ShellSupported)")
    $lines.Add("ShellReason=$($result.ShellReason)")
    $lines.Add("AccountName=$($result.AccountName)")
    $lines.Add("AccountSid=$($result.AccountSid)")
    $lines.Add("FeatureState=$featureState")
    $lines.Add("Count=$($checks.Count)")
    for ($i = 0; $i -lt $checks.Count; $i++) {
        $lines.Add("[Check$i]")
        $lines.Add("Status=$($checks[$i].Status)")
        $lines.Add("Title=$($checks[$i].Title)")

        # wrapped, because the installer shows each line as a list box row
        $detailLines = @(Split-Lines $checks[$i].Detail 90)
        $lines.Add("DetailCount=$($detailLines.Count)")
        for ($j = 0; $j -lt $detailLines.Count; $j++) {
            $lines.Add("Detail$j=$($detailLines[$j])")
        }
    }

    # UTF-16 LE with BOM, which the Unicode NSIS reads correctly
    [IO.File]::WriteAllLines($IniPath, $lines, (New-Object Text.UnicodeEncoding($false, $true)))
}

$result | ConvertTo-Json -Depth 4
