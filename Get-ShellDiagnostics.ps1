<#
.SYNOPSIS
    Read-only dump of who hosts the shell, for checking how Cairo behaves under Shell Launcher (plan.md Phase 0)
    and how UWP frame windows behave (Phase 2).

.DESCRIPTION
    Prints:
      - GetShellWindow() and the process that owns it;
      - every Shell_TrayWnd, Shell_SecondaryTrayWnd, Progman and WorkerW window, with its process;
      - the CustomShellHost.exe process tree (who is Cairo's parent);
      - Cairo's log lines about the shell, failed hotkey registrations and the tray;
      - with -Uwp: starts Calculator and ms-settings:, then lists every ApplicationFrameWindow and its
        Windows.UI.Core.CoreWindow child, with styles, cloak state and process IDs;
      - with -WatchSeconds: logs Shown, Hidden, Cloaked, Uncloaked and Destroyed events for frame windows while
        you minimize, switch tags or virtual desktops, and close apps.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Get-ShellDiagnostics.ps1

.EXAMPLE
    .\Get-ShellDiagnostics.ps1 -Uwp -WatchSeconds 60 | Tee-Object diag.txt
#>
[CmdletBinding()]
param(
    # Start Calculator and Settings, then list UWP frame windows.
    [switch]$Uwp,

    # Log frame window events for this many seconds.
    [int]$WatchSeconds = 0
)

$ErrorActionPreference = 'Stop'

if (-not ('CairoDiag.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CairoDiag
{
    public class WindowInfo
    {
        public IntPtr Hwnd;
        public string Class;
        public string Title;
        public uint Pid;
        public bool Visible;
        public bool Cloaked;
        public long Style;
        public long ExStyle;
        public string Rect;
        public override string ToString()
        {
            return string.Format("0x{0:X} [{1}] \"{2}\" pid={3} visible={4} cloaked={5} style=0x{6:X} ex=0x{7:X} rect={8}",
                Hwnd.ToInt64(), Class, Title, Pid, Visible, Cloaked, Style, ExStyle, Rect);
        }
    }

    public static class Native
    {
        public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }

        public static WindowInfo Describe(IntPtr hwnd)
        {
            StringBuilder name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            StringBuilder text = new StringBuilder(512);
            GetWindowText(hwnd, text, text.Capacity);
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            int cloaked;
            bool isCloaked = DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0;
            RECT rect;
            GetWindowRect(hwnd, out rect);
            return new WindowInfo
            {
                Hwnd = hwnd, Class = name.ToString(), Title = text.ToString(), Pid = pid, Visible = IsWindowVisible(hwnd),
                Cloaked = isCloaked, Style = GetWindowLongPtr(hwnd, -16).ToInt64(), ExStyle = GetWindowLongPtr(hwnd, -20).ToInt64(),
                Rect = string.Format("{0},{1} {2}x{3}", rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            };
        }

        public static List<WindowInfo> TopLevel()
        {
            List<WindowInfo> list = new List<WindowInfo>();
            EnumWindows((hwnd, l) => { list.Add(Describe(hwnd)); return true; }, IntPtr.Zero);
            return list;
        }

        public static List<WindowInfo> Children(IntPtr parent)
        {
            List<WindowInfo> list = new List<WindowInfo>();
            EnumChildWindows(parent, (hwnd, l) => { list.Add(Describe(hwnd)); return true; }, IntPtr.Zero);
            return list;
        }

        public static List<string> Watch(int seconds, string className)
        {
            List<string> log = new List<string>();
            Dictionary<uint, string> names = new Dictionary<uint, string>
            {
                { 0x8001, "Destroyed" }, { 0x8002, "Shown" }, { 0x8003, "Hidden" }, { 0x8017, "Cloaked" }, { 0x8018, "Uncloaked" },
                { 0x0016, "MinimizeStart" }, { 0x0017, "MinimizeEnd" }, { 0x800C, "NameChanged" }
            };
            HashSet<IntPtr> seen = new HashSet<IntPtr>();
            WinEventProc proc = (hook, evt, hwnd, idObject, idChild, thread, time) =>
            {
                if (idObject != 0 || idChild != 0 || !names.ContainsKey(evt)) return;
                WindowInfo info = Describe(hwnd);
                if (info.Class != className && !seen.Contains(hwnd)) return;
                seen.Add(hwnd);
                log.Add(string.Format("{0:HH:mm:ss.fff} {1,-13} {2}", DateTime.Now, names[evt], info));
            };

            IntPtr[] hooks =
            {
                SetWinEventHook(0x8001, 0x8003, IntPtr.Zero, proc, 0, 0, 2),
                SetWinEventHook(0x800C, 0x800C, IntPtr.Zero, proc, 0, 0, 2),
                SetWinEventHook(0x8017, 0x8018, IntPtr.Zero, proc, 0, 0, 2),
                SetWinEventHook(0x0016, 0x0017, IntPtr.Zero, proc, 0, 0, 2)
            };

            DateTime end = DateTime.Now.AddSeconds(seconds);
            MSG msg;
            while (DateTime.Now < end)
            {
                while (PeekMessage(out msg, IntPtr.Zero, 0, 0, 1))
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                System.Threading.Thread.Sleep(10);
            }

            foreach (IntPtr hook in hooks) UnhookWinEvent(hook);
            GC.KeepAlive(proc);
            return log;
        }
    }
}
'@
}

function Get-ProcessLabel([uint32]$ProcessId) {
    $process = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction SilentlyContinue
    if ($process) { return "$($process.Name) ($ProcessId)" }
    return "pid $ProcessId (gone)"
}

function Write-Section([string]$Title) {
    Write-Output ''
    Write-Output "== $Title =="
}

$sessionId = (Get-Process -Id $PID).SessionId

Write-Section 'Shell window'
$shellWindow = [CairoDiag.Native]::GetShellWindow()
if ($shellWindow -eq [IntPtr]::Zero) {
    Write-Output 'GetShellWindow() = 0 (no shell window)'
}
else {
    $info = [CairoDiag.Native]::Describe($shellWindow)
    Write-Output "GetShellWindow() = $info"
    Write-Output "  owner: $(Get-ProcessLabel $info.Pid)"
}

Write-Section 'Taskbar and desktop windows'
foreach ($window in ([CairoDiag.Native]::TopLevel() | Where-Object { $_.Class -in @('Shell_TrayWnd', 'Shell_SecondaryTrayWnd', 'Progman', 'WorkerW') })) {
    Write-Output "$window"
    Write-Output "  owner: $(Get-ProcessLabel $window.Pid)"
}

Write-Section 'Process tree (this session)'
$processes = @(Get-CimInstance -ClassName Win32_Process -Filter "SessionId=$sessionId")
$interesting = $processes | Where-Object { $_.Name -in @('CustomShellHost.exe', 'explorer.exe', 'CairoDesktop.exe', 'CairoDesktop.UIAccess.exe', 'ShellExperienceHost.exe', 'StartMenuExperienceHost.exe', 'sihost.exe') }
foreach ($process in $interesting) {
    $parent = $processes | Where-Object { $_.ProcessId -eq $process.ParentProcessId } | Select-Object -First 1
    $parentLabel = if ($parent) { "$($parent.Name) ($($parent.ProcessId))" } else { "pid $($process.ParentProcessId) (gone)" }
    Write-Output ("{0,-30} pid {1,-6} parent {2}" -f $process.Name, $process.ProcessId, $parentLabel)
    Write-Output "  $($process.CommandLine)"
}

Write-Section 'Cairo log'
$logsFolder = Join-Path $env:LOCALAPPDATA 'Cairo Desktop\Logs'
$log = Get-ChildItem -LiteralPath $logsFolder -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($log) {
    Write-Output "From $($log.FullName):"
    Select-String -LiteralPath $log.FullName -Pattern 'Running as shell', 'Shell Launcher', 'is taken', 'keyboard hook', 'Tray', 'TaskbarCreated', 'Shell_TrayWnd' |
        ForEach-Object { "  $($_.Line)" }
}
else {
    Write-Output "No Cairo log in $logsFolder"
}

if ($Uwp) {
    Write-Section 'UWP frame windows'
    Start-Process 'calc.exe'
    Start-Process 'ms-settings:'
    Start-Sleep -Seconds 4

    foreach ($frame in ([CairoDiag.Native]::TopLevel() | Where-Object { $_.Class -eq 'ApplicationFrameWindow' })) {
        Write-Output "$frame"
        Write-Output "  frame owner: $(Get-ProcessLabel $frame.Pid)"
        foreach ($child in ([CairoDiag.Native]::Children($frame.Hwnd) | Where-Object { $_.Class -eq 'Windows.UI.Core.CoreWindow' })) {
            Write-Output "  child $child"
            Write-Output "    app: $(Get-ProcessLabel $child.Pid)"
        }
    }

    Write-Output ''
    Write-Output 'Top-level CoreWindows (suspended or not yet attached apps):'
    foreach ($core in ([CairoDiag.Native]::TopLevel() | Where-Object { $_.Class -eq 'Windows.UI.Core.CoreWindow' })) {
        Write-Output "  $core  app: $(Get-ProcessLabel $core.Pid)"
    }
}

if ($WatchSeconds -gt 0) {
    Write-Section "Frame window events for $WatchSeconds s (minimize, switch tags or desktops, close apps now)"
    [CairoDiag.Native]::Watch($WatchSeconds, 'ApplicationFrameWindow') | ForEach-Object { Write-Output $_ }
}
