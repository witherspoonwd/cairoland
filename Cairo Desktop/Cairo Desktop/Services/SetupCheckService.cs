using CairoDesktop.Common;
using CairoDesktop.Common.Localization;
using CairoDesktop.Tiling;
using ManagedShell.Common.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;

namespace CairoDesktop.Services
{
    public enum SetupCheckStatus
    {
        Pass,
        Info,
        Warning,

        /// <summary>Something the user should act on; opens the Setup check at startup.</summary>
        ActionNeeded
    }

    public class SetupCheckItem
    {
        public string Id { get; set; }
        public SetupCheckStatus Status { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public string FixLabel { get; set; }

        /// <summary>Returns true when the fix worked (or was started) and the check should run again.</summary>
        public Func<Task<bool>> Fix { get; set; }

        public bool HasFix => Fix != null && !string.IsNullOrEmpty(FixLabel);
        public bool HasDetail => !string.IsNullOrEmpty(Detail);

        public string Glyph
        {
            get
            {
                switch (Status)
                {
                    case SetupCheckStatus.Pass: return "✔";
                    case SetupCheckStatus.ActionNeeded: return "✖";
                    case SetupCheckStatus.Warning: return "!";
                    default: return "i";
                }
            }
        }
    }

    /// <summary>
    /// Checks how Cairo is set up to start: the same read-only checks as the installer (Test-CairoEnvironment.ps1),
    /// plus what only a running Cairo can see. Each fix runs the matching bundled script, elevated when it has to be.
    /// </summary>
    public class SetupCheckService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "CairoShell";
        private const string WinlogonKeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
        private const int RebootRequired = 3010;

        private readonly ILogger<SetupCheckService> _logger;
        private readonly Settings _settings;
        private readonly WorkstationLockManager _lockManager;

        public SetupCheckService(ILogger<SetupCheckService> logger, Settings settings, WorkstationLockManager lockManager)
        {
            _logger = logger;
            _settings = settings;
            _lockManager = lockManager;
        }

        public async Task<List<SetupCheckItem>> RunAsync()
        {
            List<SetupCheckItem> items = new List<SetupCheckItem>();
            EnvironmentResult environment = await Task.Run(() => RunEnvironmentScript());

            AddShellChecks(items, environment);
            AddLeftoverChecks(items);
            AddLockPolicyCheck(items);

            if (environment != null)
            {
                // leftovers are checked above, with fixes that don't need elevation
                foreach (SetupCheckItem item in environment.Checks.Where(c => !c.Id.StartsWith("leftover")))
                {
                    items.Add(item);
                }
            }
            else
            {
                items.Add(new SetupCheckItem
                {
                    Id = "environment",
                    Status = SetupCheckStatus.Info,
                    Title = DisplayString.sSetupCheck_EnvironmentUnavailable
                });
            }

            return items.OrderByDescending(item => item.Status).ToList();
        }

        /// <summary>
        /// True when the check found something to act on that the user hasn't dismissed.
        /// </summary>
        public bool ShouldShow(IEnumerable<SetupCheckItem> items)
        {
            string signature = GetSignature(items);
            return signature.Length > 0 && signature != _settings.SetupCheckDismissed;
        }

        public void Dismiss(IEnumerable<SetupCheckItem> items)
        {
            _settings.SetupCheckDismissed = GetSignature(items);
        }

        private static string GetSignature(IEnumerable<SetupCheckItem> items)
        {
            return string.Join(",", items.Where(item => item.Status == SetupCheckStatus.ActionNeeded).Select(item => item.Id).OrderBy(id => id));
        }

        #region Checks

        private void AddShellChecks(List<SetupCheckItem> items, EnvironmentResult environment)
        {
            bool configured = ShellHost.IsConfiguredAsShellLauncher;
            bool shellChosen = configured || GetStartMode() == "Shell";
            bool shellSupported = environment?.ShellSupported ?? true;

            if (ShellHost.IsShellLauncher)
            {
                if (configured)
                {
                    items.Add(new SetupCheckItem
                    {
                        Id = "shell",
                        Status = SetupCheckStatus.Pass,
                        Title = DisplayString.sSetupCheck_ShellLauncherActive,
                        FixLabel = DisplayString.sSetupCheck_UseExplorer,
                        Fix = DisableShellLauncherAsync
                    });
                }
                else
                {
                    // started by a configuration Cairo didn't write for this account (e.g. the default profile)
                    items.Add(new SetupCheckItem
                    {
                        Id = "shell-foreign",
                        Status = SetupCheckStatus.Warning,
                        Title = DisplayString.sSetupCheck_ShellLauncherForeign,
                        Detail = DisplayString.sSetupCheck_ShellLauncherForeignDetail,
                        FixLabel = DisplayString.sSetupCheck_Reapply,
                        Fix = EnableShellLauncherAsync
                    });
                }

                return;
            }

            if (shellChosen)
            {
                bool appliedThisSession = configured && WasAppliedAfterSignIn();
                if (appliedThisSession)
                {
                    items.Add(new SetupCheckItem
                    {
                        Id = "shell-signout",
                        Status = SetupCheckStatus.ActionNeeded,
                        Title = DisplayString.sSetupCheck_SignOutToFinish,
                        Detail = DisplayString.sSetupCheck_SignOutToFinishDetail,
                        FixLabel = DisplayString.sSetupCheck_SignOut,
                        Fix = SignOutAsync
                    });
                }
                else
                {
                    // chosen, but Shell Launcher didn't start Cairo at this sign-in: the configuration is missing
                    items.Add(new SetupCheckItem
                    {
                        Id = "shell-missing",
                        Status = SetupCheckStatus.ActionNeeded,
                        Title = DisplayString.sSetupCheck_ShellLauncherMissing,
                        Detail = DisplayString.sSetupCheck_ShellLauncherMissingDetail,
                        FixLabel = DisplayString.sSetupCheck_Reapply,
                        Fix = EnableShellLauncherAsync
                    });
                }

                return;
            }

            if (EnvironmentHelper.IsAppRunningAsShell)
            {
                // the old Winlogon way: no immersive shell, so no Store apps or Settings
                items.Add(new SetupCheckItem
                {
                    Id = "shell-winlogon",
                    Status = shellSupported ? SetupCheckStatus.ActionNeeded : SetupCheckStatus.Warning,
                    Title = DisplayString.sSetupCheck_OldShell,
                    Detail = shellSupported ? DisplayString.sSetupCheck_OldShellDetail : environment?.ShellReason,
                    FixLabel = shellSupported ? DisplayString.sSetupCheck_UseCairoAsShell : null,
                    Fix = shellSupported ? EnableShellLauncherAsync : (Func<Task<bool>>)null
                });
                return;
            }

            items.Add(new SetupCheckItem
            {
                Id = "shell-explorer",
                Status = SetupCheckStatus.Info,
                Title = DisplayString.sSetupCheck_ExplorerIsShell,
                Detail = shellSupported ? null : environment?.ShellReason,
                FixLabel = shellSupported ? DisplayString.sSetupCheck_UseCairoAsShell : null,
                Fix = shellSupported ? EnableShellLauncherAsync : (Func<Task<bool>>)null
            });
        }

        private void AddLeftoverChecks(List<SetupCheckItem> items)
        {
            bool shell = ShellHost.IsShellLauncher || ShellHost.IsConfiguredAsShellLauncher;

            // Shell Launcher runs the Run keys before starting Cairo, so this would start it twice
            if (shell && ReadUserValue(RunKeyPath, RunValueName) != null)
            {
                items.Add(new SetupCheckItem
                {
                    Id = "leftover-run",
                    Status = SetupCheckStatus.ActionNeeded,
                    Title = DisplayString.sSetupCheck_DuplicateRun,
                    Detail = DisplayString.sSetupCheck_DuplicateRunDetail,
                    FixLabel = DisplayString.sSetupCheck_Remove,
                    Fix = () => Task.FromResult(DeleteUserValue(RunKeyPath, RunValueName))
                });
            }

            string winlogonShell = ReadUserValue(WinlogonKeyPath, "Shell") as string;
            if (shell && winlogonShell != null && winlogonShell.IndexOf("Cairo", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                items.Add(new SetupCheckItem
                {
                    Id = "leftover-winlogon",
                    Status = SetupCheckStatus.ActionNeeded,
                    Title = DisplayString.sSetupCheck_OldWinlogon,
                    Detail = winlogonShell,
                    FixLabel = DisplayString.sSetupCheck_Remove,
                    Fix = () => Task.FromResult(DeleteUserValue(WinlogonKeyPath, "Shell"))
                });
            }
        }

        private void AddLockPolicyCheck(List<SetupCheckItem> items)
        {
            if (!_settings.RemapWinL || !_lockManager.IsUnavailable)
            {
                return;
            }

            items.Add(new SetupCheckItem
            {
                Id = "lockpolicy",
                Status = SetupCheckStatus.ActionNeeded,
                Title = DisplayString.sLockFix_Title,
                Detail = DisplayString.sSetupCheck_LockPolicyDetail,
                FixLabel = DisplayString.sLockFix_Fix,
                Fix = _lockManager.FixPolicyAccessAsync
            });
        }

        /// <summary>
        /// True when the Shell Launcher configuration was written after this session started, so a sign-out is
        /// all that's missing.
        /// </summary>
        private bool WasAppliedAfterSignIn()
        {
            if (!(ReadUserValue(ShellHost.MarkerKeyPath, "ShellLauncherAppliedAt") is string text)
                || !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime appliedAt))
            {
                return true;
            }

            DateTime? signedIn = GetSessionStart();
            return !signedIn.HasValue || appliedAt.ToUniversalTime() > signedIn.Value;
        }

        /// <summary>When the shell of this session started, which is close enough to the sign-in time.</summary>
        private static DateTime? GetSessionStart()
        {
            try
            {
                int sessionId;
                using (Process current = Process.GetCurrentProcess())
                {
                    sessionId = current.SessionId;
                }

                DateTime? earliest = null;
                foreach (Process process in Process.GetProcessesByName("explorer").Concat(Process.GetProcessesByName("sihost")))
                {
                    using (process)
                    {
                        if (process.SessionId != sessionId)
                        {
                            continue;
                        }

                        DateTime start = process.StartTime.ToUniversalTime();
                        if (!earliest.HasValue || start < earliest.Value)
                        {
                            earliest = start;
                        }
                    }
                }

                return earliest;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string GetStartMode()
        {
            foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey key = baseKey.OpenSubKey(@"Software\CairoShell"))
                    {
                        if (key?.GetValue("StartMode") is string mode)
                        {
                            return mode;
                        }
                    }
                }
                catch (Exception)
                {
                    // try the other view
                }
            }

            return null;
        }

        private static object ReadUserValue(string path, string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path))
                {
                    return key?.GetValue(name);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private bool DeleteUserValue(string path, string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, true))
                {
                    key?.DeleteValue(name, false);
                }

                _logger.LogInformation($"Setup check: removed HKCU\\{path}\\{name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Setup check: unable to remove HKCU\\{path}\\{name}: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Fixes

        private Task<bool> EnableShellLauncherAsync()
        {
            string sid = WindowsIdentity.GetCurrent().User?.Value;
            string exe = Path.Combine(CairoApplication.StartupPath, "CairoDesktop.exe");
            return RunScriptAsync("Enable-CairoShellLauncher.ps1", $"-Sid \"{sid}\" -CairoExe \"{exe}\" -FixLockPolicy -NoSignOutPrompt", elevated: true);
        }

        private Task<bool> DisableShellLauncherAsync()
        {
            string sid = WindowsIdentity.GetCurrent().User?.Value;
            return RunScriptAsync("Disable-CairoShellLauncher.ps1", $"-Sid \"{sid}\" -NoSignOutPrompt", elevated: true);
        }

        private Task<bool> SignOutAsync()
        {
            ShellHelper.Logoff();
            return Task.FromResult(false);
        }

        /// <summary>
        /// Runs a bundled setup script. Elevated scripts go through UAC, the same way
        /// <see cref="WorkstationLockManager.FixPolicyAccessAsync"/> does it.
        /// </summary>
        private async Task<bool> RunScriptAsync(string scriptName, string arguments, bool elevated)
        {
            string script = FindScript(scriptName);
            if (script == null)
            {
                _logger.LogWarning($"Setup check: {scriptName} not found");
                CairoMessage.Show(string.Format(DisplayString.sSetupCheck_ScriptMissing, scriptName), DisplayString.sSetupCheck_Title, CairoMessageImage.Error);
                return false;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" {arguments}",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (elevated)
            {
                startInfo.Verb = "runas";
            }

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
                _logger.LogInformation($"Setup check: {scriptName} cancelled at the UAC prompt");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Setup check: unable to run {scriptName}: {ex.Message}");
                CairoMessage.Show(string.Format(DisplayString.sSetupCheck_FixFailed, ex.Message), DisplayString.sSetupCheck_Title, CairoMessageImage.Error);
                return false;
            }

            _logger.LogInformation($"Setup check: {scriptName} exited with {exitCode}");

            if (exitCode == RebootRequired)
            {
                CairoMessage.Show(DisplayString.sSetupCheck_RestartToFinish, DisplayString.sSetupCheck_Title, CairoMessageImage.Information);
                return true;
            }

            if (exitCode != 0)
            {
                CairoMessage.Show(string.Format(DisplayString.sSetupCheck_FixFailed, $"{scriptName} exited with code {exitCode}. See Microsoft-Windows-AssignedAccess/Admin in Event Viewer."),
                    DisplayString.sSetupCheck_Title, CairoMessageImage.Error);
                return false;
            }

            if (scriptName.StartsWith("Enable") || scriptName.StartsWith("Disable"))
            {
                CairoMessage.ShowOkCancel(DisplayString.sSettings_Advanced_ShellChangedText,
                    DisplayString.sSettings_Advanced_ShellChanged, CairoMessageImage.LogOff,
                    DisplayString.sSettings_Advanced_LogOffNow, DisplayString.sSettings_Advanced_LogOffLater,
                    result =>
                    {
                        if (result == true)
                        {
                            ShellHelper.Logoff();
                        }
                    });
            }

            return true;
        }

        /// <summary>
        /// Installed builds have the scripts in Setup\ next to the exe; development builds find them in the repo.
        /// </summary>
        public static string FindScript(string name)
        {
            string installed = Path.Combine(CairoApplication.StartupPath, "Setup", name);
            if (File.Exists(installed))
            {
                return installed;
            }

            DirectoryInfo directory = new DirectoryInfo(CairoApplication.StartupPath);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        #endregion

        #region Environment script

        private class EnvironmentResult
        {
            public bool ShellSupported { get; set; }
            public string ShellReason { get; set; }
            public List<SetupCheckItem> Checks { get; } = new List<SetupCheckItem>();
        }

        private EnvironmentResult RunEnvironmentScript()
        {
            string script = FindScript("Test-CairoEnvironment.ps1");
            if (script == null)
            {
                _logger.LogWarning("Setup check: Test-CairoEnvironment.ps1 not found");
                return null;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" -CairoDir \"{CairoApplication.StartupPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                string output;
                using (Process process = Process.Start(startInfo))
                {
                    Task<string> error = process.StandardError.ReadToEndAsync();
                    output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        _logger.LogWarning($"Setup check: Test-CairoEnvironment.ps1 exited with {process.ExitCode}: {error.Result}");
                        return null;
                    }
                }

                return ParseEnvironment(output);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Setup check: unable to run Test-CairoEnvironment.ps1: {ex.Message}");
                return null;
            }
        }

        private EnvironmentResult ParseEnvironment(string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement root = document.RootElement;
                EnvironmentResult result = new EnvironmentResult
                {
                    ShellSupported = root.TryGetProperty("ShellSupported", out JsonElement supported) && supported.ValueKind == JsonValueKind.True,
                    ShellReason = GetString(root, "ShellReason")
                };

                if (root.TryGetProperty("Checks", out JsonElement checks) && checks.ValueKind == JsonValueKind.Array)
                {
                    bool shellChosen = ShellHost.IsConfiguredAsShellLauncher || GetStartMode() == "Shell";
                    foreach (JsonElement check in checks.EnumerateArray())
                    {
                        bool blocksShell = check.TryGetProperty("BlocksShell", out JsonElement blocks) && blocks.ValueKind == JsonValueKind.True;
                        result.Checks.Add(new SetupCheckItem
                        {
                            Id = GetString(check, "Id") ?? "",
                            Status = ToStatus(GetString(check, "Status"), blocksShell && shellChosen),
                            Title = GetString(check, "Title"),
                            Detail = GetString(check, "Detail")
                        });
                    }
                }

                return result;
            }
        }

        private static SetupCheckStatus ToStatus(string status, bool blocksChosenShell)
        {
            switch (status)
            {
                case "pass": return SetupCheckStatus.Pass;
                case "warn": return SetupCheckStatus.Warning;
                case "fail": return blocksChosenShell ? SetupCheckStatus.ActionNeeded : SetupCheckStatus.Warning;
                default: return SetupCheckStatus.Info;
            }
        }

        private static string GetString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        #endregion
    }
}
