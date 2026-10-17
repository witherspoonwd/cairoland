using CairoDesktop.Common;
using CairoDesktop.Interop;
using ManagedShell.Common.Logging;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Frees Win+L for the window manager by setting the per-user DisableLockWorkstation policy, and locks by
    /// briefly clearing it. The original value is kept in the journal so every exit and crash path restores it.
    /// </summary>
    public class WorkstationLockManager
    {
        private const string PolicyKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        private const string PolicyValueName = "DisableLockWorkstation";
        private static readonly TimeSpan RelockDelay = TimeSpan.FromSeconds(1);

        private readonly Settings _settings;
        private readonly HiddenWindowJournal _journal;
        private DispatcherTimer _relockTimer;

        // true between Enable and Disable, i.e. while tiling wants Win+L, even if the remap failed
        private bool _enableRequested;

        /// <summary>True while Win+L does not lock, so it can be bound.</summary>
        public bool IsRemapActive { get; private set; }

        /// <summary>True when an administrator had already disabled locking; we don't touch the value.</summary>
        public bool IsAdminLocked { get; private set; }

        /// <summary>True when the policy key could not be written on this machine.</summary>
        public bool IsUnavailable { get; private set; }

        public event EventHandler StateChanged;

        public WorkstationLockManager(Settings settings, HiddenWindowJournal journal)
        {
            _settings = settings;
            _journal = journal;
            _journal.RestoreLockPolicy = RestoreFromJournal;
        }

        public void Enable()
        {
            _enableRequested = true;

            if (IsRemapActive || !_settings.RemapWinL)
            {
                return;
            }

            try
            {
                object existing = Registry.CurrentUser.OpenSubKey(PolicyKeyPath)?.GetValue(PolicyValueName);
                int? existingValue = existing is int value ? value : (int?)null;

                if (existingValue == 1 && !_journal.State.LockPolicySaved)
                {
                    // an administrator (or something else) already disabled locking; don't fight it
                    ShellLogger.Info("WorkstationLockManager: DisableLockWorkstation is already set; leaving it alone");
                    IsAdminLocked = true;
                    IsRemapActive = true;
                    OnStateChanged();
                    return;
                }

                if (!_journal.State.LockPolicySaved)
                {
                    _journal.SaveLockPolicy(existingValue.HasValue, existingValue ?? 0);
                }

                WritePolicy(1);
                IsRemapActive = true;
                IsUnavailable = false;
                ShellLogger.Info("WorkstationLockManager: Win+L remapped");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException)
            {
                ShellLogger.Warning($"WorkstationLockManager: Unable to write the lock policy, Win+L stays the lock key: {ex.Message}");
                IsUnavailable = true;
                _journal.ClearLockPolicy();
            }

            OnStateChanged();
        }

        public void Disable()
        {
            _enableRequested = false;
            _relockTimer?.Stop();
            _relockTimer = null;

            if (IsRemapActive && !IsAdminLocked)
            {
                RestoreFromJournal(_journal.State);
                _journal.ClearLockPolicy();
            }

            IsRemapActive = false;
            IsAdminLocked = false;
            OnStateChanged();
        }

        /// <summary>
        /// Locks the workstation. Never leaves the user unable to lock: LockWorkStation is called even if the
        /// policy can't be cleared.
        /// </summary>
        public void Lock()
        {
            if (IsRemapActive && !IsAdminLocked)
            {
                try
                {
                    WritePolicy(0);
                }
                catch (Exception ex)
                {
                    ShellLogger.Warning($"WorkstationLockManager: Unable to clear the lock policy before locking: {ex.Message}");
                }

                User32.LockWorkStation();

                _relockTimer?.Stop();
                _relockTimer = new DispatcherTimer { Interval = RelockDelay };
                _relockTimer.Tick += RelockTimer_Tick;
                _relockTimer.Start();
                return;
            }

            if (IsAdminLocked)
            {
                ShellLogger.Info("WorkstationLockManager: Locking is disabled by policy");
            }

            User32.LockWorkStation();
        }

        /// <summary>
        /// Windows' default ACL makes HKCU\...\Policies read-only for standard users. With administrator approval
        /// (UAC), creates the System key for the current user and grants them full control of it, then retries
        /// <see cref="Enable"/>. Returns false if the user cancels UAC or the fix fails.
        /// </summary>
        public async Task<bool> FixPolicyAccessAsync()
        {
            string sid = WindowsIdentity.GetCurrent().User?.Value;
            if (sid == null || !Regex.IsMatch(sid, @"^S-[0-9-]+$"))
            {
                ShellLogger.Warning("WorkstationLockManager: Unable to determine the current user's SID");
                return false;
            }

            // HKEY_USERS\<sid> instead of HKCU, so it still targets this user if a different admin approves UAC
            string script = $@"
$ErrorActionPreference = 'Stop'
$sid = New-Object System.Security.Principal.SecurityIdentifier('{sid}')
$key = [Microsoft.Win32.Registry]::Users.CreateSubKey('{sid}\{PolicyKeyPath}')
$key.Close()
$key = [Microsoft.Win32.Registry]::Users.OpenSubKey('{sid}\{PolicyKeyPath}', [Microsoft.Win32.RegistryKeyPermissionCheck]::ReadWriteSubTree, [System.Security.AccessControl.RegistryRights]'ReadKey, ChangePermissions')
$acl = $key.GetAccessControl([System.Security.AccessControl.AccessControlSections]::Access)
foreach ($rule in @($acl.GetAccessRules($true, $false, [System.Security.Principal.SecurityIdentifier]))) {{
    if ($rule.AccessControlType -eq 'Deny' -and $rule.IdentityReference -eq $sid) {{ [void]$acl.RemoveAccessRuleSpecific($rule) }}
}}
$acl.AddAccessRule((New-Object System.Security.AccessControl.RegistryAccessRule($sid, 'FullControl', 'ContainerInherit', 'None', 'Allow')))
$key.SetAccessControl($acl)
$key.Close()
";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            int exitCode;
            try
            {
                exitCode = await Task.Run(() =>
                {
                    using (Process process = Process.Start(startInfo))
                    {
                        process.WaitForExit();
                        return process.ExitCode;
                    }
                });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                ShellLogger.Info("WorkstationLockManager: Lock policy fix cancelled at the UAC prompt");
                return false;
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"WorkstationLockManager: Unable to run the lock policy fix: {ex.Message}");
                return false;
            }

            if (exitCode != 0)
            {
                ShellLogger.Warning($"WorkstationLockManager: Lock policy fix failed with exit code {exitCode}");
                return false;
            }

            ShellLogger.Info("WorkstationLockManager: Lock policy key is now writable");
            IsUnavailable = false;

            if (_enableRequested)
            {
                Enable();
                return IsRemapActive;
            }

            OnStateChanged();
            return true;
        }

        private void RelockTimer_Tick(object sender, EventArgs e)
        {
            _relockTimer?.Stop();
            _relockTimer = null;

            if (!IsRemapActive || IsAdminLocked)
            {
                return;
            }

            try
            {
                WritePolicy(1);
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"WorkstationLockManager: Unable to set the lock policy after locking: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes back the original policy (or deletes the value if it didn't exist). Safe to call from any thread.
        /// </summary>
        private static void RestoreFromJournal(HiddenWindowJournal.JournalState state)
        {
            if (!state.LockPolicySaved)
            {
                return;
            }

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PolicyKeyPath))
            {
                if (state.LockPolicyExisted)
                {
                    key.SetValue(PolicyValueName, state.LockPolicyValue, RegistryValueKind.DWord);
                }
                else
                {
                    key.DeleteValue(PolicyValueName, false);
                }
            }

            ShellLogger.Info("WorkstationLockManager: Lock policy restored");
        }

        private static void WritePolicy(int value)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PolicyKeyPath))
            {
                key.SetValue(PolicyValueName, value, RegistryValueKind.DWord);
            }
        }

        private void OnStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
