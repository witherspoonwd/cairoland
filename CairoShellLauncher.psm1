<#
.SYNOPSIS
    Shared helpers for the Cairo Shell Launcher scripts (Enable-, Disable-CairoShellLauncher.ps1,
    Test-CairoEnvironment.ps1). Ships next to them in the installer's Setup folder.

.DESCRIPTION
    Shell Launcher v2 is configured through the ShellLauncher property of MDM_AssignedAccess in
    root\cimv2\mdm\dmmap. That WMI class only works as SYSTEM, so Invoke-AsSystem runs a script block through a
    one-shot scheduled task instead of needing psexec.
#>

Set-StrictMode -Version 2

$script:Namespace = 'root\cimv2\mdm\dmmap'
$script:ClassName = 'MDM_AssignedAccess'
$script:Features = @('Client-DeviceLockdown', 'Client-EmbeddedShellLauncher')

# Profile Id used for Cairo's profile, so our own configuration can be recognised later
$script:CairoProfileId = '{8C9C5C4A-2E7B-4B0E-9C6D-CA1205E11A00}'

# HKCU marker read by Cairo (ShellHost.IsConfiguredAsShellLauncher)
$script:MarkerKey = 'Software\CairoShell'
$script:MarkerValue = 'ShellLauncher'

# Return codes from Cairo (ShellHost.StopExitCode / RestartExitCode)
$script:StopExitCode = 1
$script:RestartExitCode = 3

function Test-Elevated {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

<#
.SYNOPSIS
    Re-runs the calling script elevated, passing its parameters on, and returns its exit code.
#>
function Invoke-SelfElevated([string]$ScriptPath, [hashtable]$BoundParameters) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$ScriptPath`"")
    foreach ($entry in $BoundParameters.GetEnumerator()) {
        if ($entry.Value -is [switch] -or $entry.Value -is [bool]) {
            if ($entry.Value) { $arguments += "-$($entry.Key)" }
        }
        else {
            $arguments += "-$($entry.Key)"
            $arguments += "`"$($entry.Value)`""
        }
    }

    $process = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    return $process.ExitCode
}

<#
.SYNOPSIS
    Finds where the installer put Cairo (HKLM\Software\CairoShell\Install_Dir). NSIS is 32-bit, so the value is
    usually in the 32-bit registry view.
#>
function Get-CairoInstallDir {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        try {
            $key = $base.OpenSubKey('Software\CairoShell')
            if ($key) {
                $dir = $key.GetValue('Install_Dir')
                $key.Close()
                if ($dir -and (Test-Path -LiteralPath (Join-Path $dir 'CairoDesktop.exe'))) {
                    return $dir
                }
            }
        }
        finally {
            $base.Close()
        }
    }

    return $null
}

<#
.SYNOPSIS
    The user signed in at the console, not the (possibly different) elevated account running this script.
    Returns @{ Name; Sid; Source }.
#>
function Get-ConsoleUser {
    $name = $null
    $source = $null

    try {
        $name = (Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop).UserName
        if ($name) { $source = 'Win32_ComputerSystem' }
    }
    catch { }

    if (-not $name) {
        # RDP or fast user switching: the owner of the shell in this script's session
        $sessionId = (Get-Process -Id $PID).SessionId
        foreach ($shell in @('CustomShellHost.exe', 'explorer.exe', 'CairoDesktop.exe', 'CairoDesktop.UIAccess.exe')) {
            $process = Get-CimInstance -ClassName Win32_Process -Filter "Name='$shell' AND SessionId=$sessionId" -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($process) {
                $owner = Invoke-CimMethod -InputObject $process -MethodName GetOwner -ErrorAction SilentlyContinue
                if ($owner -and $owner.ReturnValue -eq 0) {
                    $name = "$($owner.Domain)\$($owner.User)"
                    $source = $shell
                    break
                }
            }
        }
    }

    if (-not $name) {
        $name = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        $source = 'CurrentIdentity'
    }

    $sid = ([Security.Principal.NTAccount]$name).Translate([Security.Principal.SecurityIdentifier]).Value
    return [pscustomobject]@{ Name = $name; Sid = $sid; Source = $source }
}

function Resolve-Account([string]$User) {
    if ($User -match '^S-1-[0-9-]+$') {
        $sid = $User
        $name = ([Security.Principal.SecurityIdentifier]$sid).Translate([Security.Principal.NTAccount]).Value
    }
    else {
        if ($User -notmatch '\\') { $User = "$env:COMPUTERNAME\$User" }
        if ($User.StartsWith('.\')) { $User = "$env:COMPUTERNAME\$($User.Substring(2))" }
        $sid = ([Security.Principal.NTAccount]$User).Translate([Security.Principal.SecurityIdentifier]).Value
        $name = $User
    }

    return [pscustomobject]@{ Name = $name; Sid = $sid; Source = 'Parameter' }
}

<#
.SYNOPSIS
    Runs a script block as SYSTEM through a one-shot scheduled task and returns what it wrote to the pipeline
    (round-tripped as CLIXML). The script and its output live in a folder only SYSTEM and Administrators can
    touch, so a standard user can't swap the script before it runs.
#>
function Invoke-AsSystem([scriptblock]$ScriptBlock, [hashtable]$Arguments = @{}, [int]$TimeoutSeconds = 120) {
    if ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -eq 'S-1-5-18') {
        return ,@(& $ScriptBlock @Arguments)
    }

    if (-not (Test-Elevated)) {
        throw 'Invoke-AsSystem needs an elevated PowerShell.'
    }

    $taskName = "CairoShellLauncher-$([guid]::NewGuid().ToString('N'))"
    $workDir = Join-Path $env:SystemRoot "Temp\$taskName"
    New-Item -ItemType Directory -Path $workDir -Force | Out-Null

    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
        $identity = New-Object System.Security.Principal.SecurityIdentifier($sid)
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    Set-Acl -LiteralPath $workDir -AclObject $acl

    $scriptPath = Join-Path $workDir 'run.ps1'
    $argsPath = Join-Path $workDir 'args.xml'
    $outputPath = Join-Path $workDir 'output.xml'
    $errorPath = Join-Path $workDir 'error.txt'

    $Arguments | Export-Clixml -LiteralPath $argsPath
    $wrapper = @"
`$ErrorActionPreference = 'Stop'
try {
    `$arguments = Import-Clixml -LiteralPath '$argsPath'
    `$block = [scriptblock]::Create(@'
$($ScriptBlock.ToString())
'@)
    `$result = & `$block @arguments
    ,@(`$result) | Export-Clixml -LiteralPath '$outputPath'
}
catch {
    (`$_ | Out-String) | Set-Content -LiteralPath '$errorPath' -Encoding UTF8
    exit 1
}
"@
    Set-Content -LiteralPath $scriptPath -Value $wrapper -Encoding UTF8

    try {
        $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$scriptPath`""
        $principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds $TimeoutSeconds)
        Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
        Start-ScheduledTask -TaskName $taskName

        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        do {
            Start-Sleep -Milliseconds 250
            $info = Get-ScheduledTaskInfo -TaskName $taskName
            $state = (Get-ScheduledTask -TaskName $taskName).State
        } while (($state -eq 'Running' -or $state -eq 'Queued' -or $info.LastTaskResult -eq 267011) -and (Get-Date) -lt $deadline)

        if ($state -eq 'Running') {
            throw "The SYSTEM task did not finish within $TimeoutSeconds seconds."
        }

        if (Test-Path -LiteralPath $errorPath) {
            throw "The SYSTEM task failed: $(Get-Content -LiteralPath $errorPath -Raw)"
        }

        if (-not (Test-Path -LiteralPath $outputPath)) {
            throw "The SYSTEM task produced no output (result 0x$('{0:X}' -f $info.LastTaskResult))."
        }

        return (Import-Clixml -LiteralPath $outputPath)
    }
    finally {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

<#
.SYNOPSIS
    Reads the assigned access configuration as SYSTEM. Returns @{ ShellLauncher; Configuration } (XML strings,
    or $null), or $null when there is no MDM_AssignedAccess instance.
#>
function Get-AssignedAccessConfig {
    $result = Invoke-AsSystem -ScriptBlock {
        $instance = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess' -ErrorAction Stop
        if (-not $instance) { return $null }
        [pscustomobject]@{
            ShellLauncher = [System.Net.WebUtility]::HtmlDecode($instance.ShellLauncher)
            Configuration = [System.Net.WebUtility]::HtmlDecode($instance.Configuration)
        }
    }

    return @($result)[0]
}

<#
.SYNOPSIS
    Writes the Shell Launcher XML as SYSTEM, or clears it when $Xml is $null. Returns the value read back.
#>
function Set-ShellLauncherConfig([AllowNull()][string]$Xml) {
    $result = Invoke-AsSystem -Arguments @{ Xml = $Xml } -ScriptBlock {
        param([string]$Xml)
        $instance = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess' -ErrorAction Stop
        if ($Xml) {
            $instance.ShellLauncher = [System.Net.WebUtility]::HtmlEncode($Xml)
        }
        else {
            $instance.ShellLauncher = $null
        }
        Set-CimInstance -CimInstance $instance -ErrorAction Stop

        $readBack = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess' -ErrorAction Stop
        [System.Net.WebUtility]::HtmlDecode($readBack.ShellLauncher)
    }

    return @($result)[0]
}

<#
.SYNOPSIS
    When enabling the features needs a reboot, the WMI bridge may not take the configuration until then. Registers
    a SYSTEM task that writes it at the next startup and then deletes itself. The XML is embedded in the task's
    command line, so the task doesn't run a script file someone could change in the meantime.
#>
function Register-ShellLauncherConfigAtStartup([string]$Xml) {
    $taskName = 'CairoShellLauncher-ApplyAtStartup'
    $encodedXml = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Xml))
    $script = @"
`$xml = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$encodedXml'))
`$instance = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess'
`$instance.ShellLauncher = [System.Net.WebUtility]::HtmlEncode(`$xml)
Set-CimInstance -CimInstance `$instance
Unregister-ScheduledTask -TaskName '$taskName' -Confirm:`$false
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))

    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encoded"
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
}

function Unregister-ShellLauncherConfigAtStartup {
    Unregister-ScheduledTask -TaskName 'CairoShellLauncher-ApplyAtStartup' -Confirm:$false -ErrorAction SilentlyContinue
}

function Test-IsCairoShellLauncherXml([AllowNull()][string]$Xml) {
    return [bool]$Xml -and $Xml.Contains($script:CairoProfileId)
}

<#
.SYNOPSIS
    Builds the Shell Launcher v2 XML: Cairo for one account, Explorer for everyone else.
#>
function New-ShellLauncherXml([string]$CairoExe, [string]$Sid) {
    $shell = [Security.SecurityElement]::Escape("`"$CairoExe`" /shell=true /shelllauncher=true")
    $sidAttribute = [Security.SecurityElement]::Escape($Sid)

    return @"
<?xml version="1.0" encoding="utf-8"?>
<ShellLauncherConfiguration xmlns="http://schemas.microsoft.com/ShellLauncher/2018/Configuration" xmlns:V2="http://schemas.microsoft.com/ShellLauncher/2019/Configuration">
  <Profiles>
    <DefaultProfile>
      <Shell Shell="%SystemRoot%\explorer.exe" V2:AppType="Desktop" />
    </DefaultProfile>
    <Profile Id="$($script:CairoProfileId)" Name="Cairo Desktop">
      <Shell Shell="$shell" V2:AppType="Desktop" V2:AllAppsFullScreen="false">
        <ReturnCodeActions>
          <ReturnCodeAction ReturnCode="$($script:StopExitCode)" Action="DoNothing" />
          <ReturnCodeAction ReturnCode="$($script:RestartExitCode)" Action="RestartShell" />
        </ReturnCodeActions>
        <DefaultAction Action="RestartShell" />
      </Shell>
    </Profile>
  </Profiles>
  <Configs>
    <Config>
      <Account Sid="$sidAttribute" />
      <Profile Id="$($script:CairoProfileId)" />
    </Config>
  </Configs>
</ShellLauncherConfiguration>
"@
}

<#
.SYNOPSIS
    Returns 'Enabled', 'Disabled' or 'RebootPending' for the two Shell Launcher features. Works without elevation.
#>
function Get-ShellLauncherFeatureState {
    if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') {
        $pending = $true
    }
    else {
        $pending = $false
    }

    $states = foreach ($feature in $script:Features) {
        $instance = Get-CimInstance -ClassName Win32_OptionalFeature -Filter "Name='$feature'" -ErrorAction SilentlyContinue
        if ($instance) { $instance.InstallState } else { 3 }
    }

    if (@($states | Where-Object { $_ -ne 1 }).Count -eq 0) {
        return 'Enabled'
    }

    if ($pending) {
        return 'RebootPending'
    }

    return 'Disabled'
}

<#
.SYNOPSIS
    Enables the two optional features. Returns $true if a reboot is needed.
#>
function Enable-ShellLauncherFeature {
    $restartNeeded = $false
    foreach ($feature in $script:Features) {
        $result = Enable-WindowsOptionalFeature -Online -FeatureName $feature -All -NoRestart -ErrorAction Stop
        if ($result.RestartNeeded) {
            $restartNeeded = $true
        }
    }

    return $restartNeeded
}

<#
.SYNOPSIS
    Sets or clears the HKCU marker Cairo reads (ShellHost.IsConfiguredAsShellLauncher) under HKEY_USERS\<sid>.
    The user's hive is only loaded while they are signed in, which is the case for the console user.
#>
function Set-UserMarker([string]$Sid, [bool]$Configured) {
    $key = [Microsoft.Win32.Registry]::Users.OpenSubKey("$Sid", $true)
    if (-not $key) {
        Write-Warning "The registry hive of $Sid isn't loaded (not signed in), so the Cairo marker wasn't updated."
        return
    }

    try {
        if ($Configured) {
            $cairoKey = $key.CreateSubKey($script:MarkerKey)
            $cairoKey.SetValue($script:MarkerValue, 1, [Microsoft.Win32.RegistryValueKind]::DWord)
            $cairoKey.SetValue('ShellLauncherAppliedAt', (Get-Date).ToUniversalTime().ToString('o'))
            $cairoKey.Close()
        }
        else {
            $cairoKey = $key.OpenSubKey($script:MarkerKey, $true)
            if ($cairoKey) {
                $cairoKey.DeleteValue($script:MarkerValue, $false)
                $cairoKey.DeleteValue('ShellLauncherAppliedAt', $false)
                $cairoKey.Close()
            }
        }
    }
    finally {
        $key.Close()
    }
}

<#
.SYNOPSIS
    Leftovers from the old ways of making Cairo the shell: Winlogon Shell values pointing at Cairo, and the HKCU
    Run value (Shell Launcher runs the Run keys before the shell, so it would start Cairo twice).
#>
function Get-CairoLeftovers([string]$Sid) {
    $leftovers = @()

    $userKey = [Microsoft.Win32.Registry]::Users.OpenSubKey($Sid)
    if ($userKey) {
        try {
            $winlogon = $userKey.OpenSubKey('Software\Microsoft\Windows NT\CurrentVersion\Winlogon')
            if ($winlogon -and "$($winlogon.GetValue('Shell'))" -match 'Cairo') {
                $leftovers += [pscustomobject]@{ Hive = 'User'; Path = 'Software\Microsoft\Windows NT\CurrentVersion\Winlogon'; Name = 'Shell'; Value = $winlogon.GetValue('Shell') }
            }

            $run = $userKey.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
            if ($run -and $null -ne $run.GetValue('CairoShell')) {
                $leftovers += [pscustomobject]@{ Hive = 'User'; Path = 'Software\Microsoft\Windows\CurrentVersion\Run'; Name = 'CairoShell'; Value = $run.GetValue('CairoShell') }
            }
        }
        finally {
            $userKey.Close()
        }
    }

    $machineShell = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' -Name Shell -ErrorAction SilentlyContinue).Shell
    if ("$machineShell" -match 'Cairo') {
        $leftovers += [pscustomobject]@{ Hive = 'Machine'; Path = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'; Name = 'Shell'; Value = $machineShell }
    }

    return $leftovers
}

function Remove-CairoLeftovers([string]$Sid) {
    foreach ($leftover in (Get-CairoLeftovers $Sid)) {
        if ($leftover.Hive -eq 'Machine') {
            # Explorer is the machine default
            Set-ItemProperty -LiteralPath "HKLM:\$($leftover.Path)" -Name Shell -Value 'explorer.exe'
        }
        else {
            $key = [Microsoft.Win32.Registry]::Users.OpenSubKey("$Sid\$($leftover.Path)", $true)
            if ($key) {
                $key.DeleteValue($leftover.Name, $false)
                $key.Close()
            }
        }

        Write-Host "Removed leftover $($leftover.Hive) $($leftover.Path)\$($leftover.Name) ($($leftover.Value))"
    }
}

function Get-AssignedAccessEvents([datetime]$Since) {
    Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-AssignedAccess/Admin'; StartTime = $Since } -ErrorAction SilentlyContinue
}

<#
.SYNOPSIS
    Windows' default ACL makes HKCU\...\Policies read-only for standard users, which stops Cairo from taking over
    Win+L. Creates the System policy key for the user and grants them full control of it (the same fix
    WorkstationLockManager.FixPolicyAccessAsync asks UAC for).
#>
function Grant-LockPolicyAccess([string]$Sid) {
    $path = "$Sid\Software\Microsoft\Windows\CurrentVersion\Policies\System"
    $identity = New-Object System.Security.Principal.SecurityIdentifier($Sid)

    $key = [Microsoft.Win32.Registry]::Users.CreateSubKey($path)
    if (-not $key) {
        Write-Warning "The registry hive of $Sid isn't loaded, so the Win+L policy key wasn't fixed."
        return
    }
    $key.Close()

    $key = [Microsoft.Win32.Registry]::Users.OpenSubKey($path, [Microsoft.Win32.RegistryKeyPermissionCheck]::ReadWriteSubTree, [System.Security.AccessControl.RegistryRights]'ReadKey, ChangePermissions')
    try {
        $acl = $key.GetAccessControl([System.Security.AccessControl.AccessControlSections]::Access)
        foreach ($rule in @($acl.GetAccessRules($true, $false, [System.Security.Principal.SecurityIdentifier]))) {
            if ($rule.AccessControlType -eq 'Deny' -and $rule.IdentityReference -eq $identity) { [void]$acl.RemoveAccessRuleSpecific($rule) }
        }
        $acl.AddAccessRule((New-Object System.Security.AccessControl.RegistryAccessRule($identity, 'FullControl', 'ContainerInherit', 'None', 'Allow')))
        $key.SetAccessControl($acl)
    }
    finally {
        $key.Close()
    }
}

<#
.SYNOPSIS
    Remembers the installer's start mode choice (Shell, Alongside or Manual) in HKLM\Software\CairoShell\StartMode,
    in the same (32-bit) registry view the installer uses.
#>
function Set-CairoStartMode([string]$Mode) {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry32)
    try {
        $key = $base.CreateSubKey('Software\CairoShell')
        $key.SetValue('StartMode', $Mode)
        $key.Close()
    }
    finally {
        $base.Close()
    }
}

Export-ModuleMember -Function * -Variable CairoProfileId, StopExitCode, RestartExitCode
