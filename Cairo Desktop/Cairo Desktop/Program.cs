using CairoDesktop.AppGrabber;
using CairoDesktop.Application.Interfaces;
using CairoDesktop.Common.Logging;
using CairoDesktop.Common;
using CairoDesktop.Infrastructure.DependencyInjection;
using CairoDesktop.Infrastructure.Options;
using CairoDesktop.MenuBarExtensions;
using CairoDesktop.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using CairoDesktop.Commands;
using CairoDesktop.Common.Localization;
using CairoDesktop.SupportingClasses;
using CairoDesktop.Infrastructure.ObjectModel;
using CairoDesktop.MenuBar.Services;
using CairoDesktop.AppGrabber.Commands;
using CairoDesktop.MenuBar.Commands;
using CairoDesktop.MenuBarExtensions.Commands;
using CairoDesktop.MenuBarExtensions.Services;
using CairoDesktop.MenuBar.Launcher;
using CairoDesktop.Tiling;
using CairoDesktop.Tiling.Commands;

namespace CairoDesktop
{
    internal sealed class Program
    {
        private const string MutexName = "CairoShell";
        private const int MutexAttempts = 10;
        private const int MutexWaitMs = 1000;

        private static IHost _host;
        private static System.Threading.Mutex _cairoMutex;

        /// <summary>
        /// The main entry point for the application
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            bool isShellLauncher = ShellHost.HasShellLauncherArgument(args);

            // once per start by Shell Launcher: in this process, not again in the UI access copy
            if (isShellLauncher && !UIAccessLauncher.IsCopy && CrashLoopGuard.RecordStart())
            {
                return StopCrashLoop();
            }

            // before the single instance check, so the copy can take the mutex
            Process copy = UIAccessLauncher.TryHandOff(args);
            if (copy != null)
            {
                if (!isShellLauncher)
                {
                    return 1;
                }

                // Shell Launcher only watches the process it started, so stay as a supervisor and pass the copy's
                // exit code on: 1 stops, anything else is a crash and Shell Launcher restarts us. A restart Cairo
                // asked for (3) is done here, which also works when Shell Launcher isn't watching (Deploy-Cairo.ps1).
                while (true)
                {
                    int exitCode;
                    using (copy)
                    {
                        copy.WaitForExit();
                        exitCode = copy.ExitCode;
                    }

                    if (exitCode != ShellHost.RestartExitCode)
                    {
                        return exitCode;
                    }

                    copy = UIAccessLauncher.TryHandOff(args);
                    if (copy == null)
                    {
                        return exitCode;
                    }
                }
            }

            if (!SingleInstanceCheck())
            {
                return 1;
            }

            _host = new HostBuilder()
                .ConfigureAppConfiguration((context, builder) =>
                {
                    builder.AddCommandLine(args);
                })
                .ConfigureServices((context, services) =>
                {
                    services.Configure<CommandLineOptions>(context.Configuration);

                    services.AddSingleton<Settings>(s => Settings.Instance);

                    services.AddSingleton<IInitializationService, CairoApplicationInitializationService>();

                    services.AddSingleton<CairoApplication>();
                    services.AddSingleton<ICairoApplication>(provider => provider.GetRequiredService<CairoApplication>());

                    services.AddSingleton<IAppGrabber, AppGrabberService>();

                    services.AddSingleton<ISettingsUIService, SettingsUIService>();

                    services.AddHostedService<MenuBarHotKeyService>();
                    services.AddHostedService<MenuExtraHotKeyService>();

                    services.AddSingleton<IThemeService, CairoApplicationThemeService>();

                    services.AddSingleton<ShellWindowService>();
                    services.AddSingleton<FullScreenWatcher>();
                    services.AddSingleton<IWindowService, MenuBarWindowService>();

                    services.AddSingleton<HiddenWindowJournal>();
                    services.AddSingleton<WorkstationLockManager>();
                    services.AddSingleton<SetupCheckService>();
                    services.AddSingleton<ITilingManager, TilingManager>();
                    services.AddSingleton<ProgramIndex>();
                    services.AddSingleton<LaunchHistory>();
                    services.AddHostedService<TilingService>();
                    services.AddSingleton<TilingHotKeyService>();
                    services.AddHostedService(provider => provider.GetRequiredService<TilingHotKeyService>());

#if ENABLEFIRSTRUN
                    services.AddHostedService<FirstRunService>();
#endif

                    services.AddInfrastructureServices(context.Configuration);

                    var extensionPaths = new[]
                    {
                        Path.Combine(CairoApplication.StartupPath, "Extensions"),
                        Path.Combine(CairoApplication.CairoApplicationDataFolder, "Extensions")
                    };

                    services.AddDependencyLoadingServices(context.Configuration, extensionPaths); // TODO: this should not be a property of CairoApplication... Possible solution, use Configuration?

                    // Inbox extensions below
                    services.AddSingleton<IShellExtension, MenuBarExtensionsShellExtension>();

                    // Inbox commands below
                    services.AddSingleton<ICairoCommand, AboutCairoCommand>();
                    services.AddSingleton<ICairoCommand, CheckForUpdatesCommand>();
                    services.AddSingleton<ICairoCommand, ExitCairoCommand>();
                    services.AddSingleton<ICairoCommand, HibernateCommand>();
                    services.AddSingleton<ICairoCommand, LockCommand>();
                    services.AddSingleton<ICairoCommand, LogOffCommand>();
                    services.AddSingleton<ICairoCommand, OpenAppGrabberCommand>();
                    services.AddSingleton<ICairoCommand, OpenCairoSettingsCommand>();
                    services.AddSingleton<ICairoCommand, OpenControlPanelCommand>();
                    services.AddSingleton<ICairoCommand, OpenWindowsSettingsCommand>();
                    services.AddSingleton<ICairoCommand, RestartCommand>();
                    services.AddSingleton<ICairoCommand, ShowFilePropertiesCommand>();
                    services.AddSingleton<ICairoCommand, ShowRunDialogCommand>();
                    services.AddSingleton<ICairoCommand, ShutDownCommand>();
                    services.AddSingleton<ICairoCommand, SleepCommand>();
                    services.AddSingleton<ICairoCommand, StartTaskManagerCommand>();
                    services.AddSingleton<ICairoCommand, OpenLocationCommand>();
                    services.AddSingleton<ICairoCommand, OpenLocationInWindowCommand>();
                    services.AddSingleton<ICairoCommand, CopyFileCommand>();
                    services.AddSingleton<ICairoCommand, DeleteFileCommand>();
                    services.AddSingleton<ICairoCommand, OpenFileCommand>();
                    services.AddSingleton<ICairoCommand, OpenDateTimeControlPanelCommand>();
                    services.AddSingleton<ICairoCommand, OpenDisplayControlPanelCommand>();
                    services.AddSingleton<ICairoCommand, OpenPersonalizeControlPanelCommand>();
                    services.AddSingleton<ICairoCommand, OpenProgramsControlPanelCommand>();
                    services.AddSingleton<ICairoCommand, AddToProgramsCommand>();
                    services.AddSingleton<ICairoCommand, AddToQuickLaunchCommand>();
                    services.AddSingleton<ICairoCommand, ToggleCairoMenuCommand>();
                    services.AddSingleton<ICairoCommand, ToggleCalendarCommand>();
                    services.AddSingleton<ICairoCommand, ToggleSearchCommand>();

                    // Tiling commands
                    services.AddSingleton<ICairoCommand, TilingViewTagCommand>();
                    services.AddSingleton<ICairoCommand, TilingMoveToTagCommand>();
                    services.AddSingleton<ICairoCommand, TilingViewPreviousTagCommand>();
                    services.AddSingleton<ICairoCommand, TilingFocusDirectionCommand>();
                    services.AddSingleton<ICairoCommand, TilingSwapDirectionCommand>();
                    services.AddSingleton<ICairoCommand, TilingMoveDirectionCommand>();
                    services.AddSingleton<ICairoCommand, TilingResizeCommand>();
                    services.AddSingleton<ICairoCommand, TilingFocusNextCommand>();
                    services.AddSingleton<ICairoCommand, TilingFocusPrevCommand>();
                    services.AddSingleton<ICairoCommand, TilingToggleOrientationCommand>();
                    services.AddSingleton<ICairoCommand, TilingEqualizeCommand>();
                    services.AddSingleton<ICairoCommand, TilingToggleFloatingCommand>();
                    services.AddSingleton<ICairoCommand, TilingToggleFullscreenCommand>();
                    services.AddSingleton<ICairoCommand, TilingCloseWindowCommand>();
                    services.AddSingleton<ICairoCommand, TilingFocusScreenCommand>();
                    services.AddSingleton<ICairoCommand, TilingMoveToScreenCommand>();
                    services.AddSingleton<ICairoCommand, TilingLaunchTerminalCommand>();
                    services.AddSingleton<ICairoCommand, CloseActiveWindowCommand>();
                    services.AddSingleton<ICairoCommand, RunCommandCommand>();
                    services.AddSingleton<ICairoCommand, RestartCairoCommand>();
                    services.AddSingleton<ICairoCommand, DisplayProjectionCommand>();
                    services.AddSingleton<ICairoCommand, ToggleProgramLauncherCommand>();
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);

                    logging.AddManagedShellLogging(options =>
                    {
                        var severity = Settings.Instance.LogSeverity;
                        options.LogLevel = severity.ToLogLevel();
                        options.LogsFolderPath = CairoApplication.LogsFolder;
                    });
                })
                .ConfigureHostOptions(options =>
                {
                    options.ShutdownTimeout = TimeSpan.FromSeconds(10);
                })
                .Build();

            var app = _host.Services.GetRequiredService<ICairoApplication>();
            return app.Run();
        }

        /// <summary>
        /// Cairo keeps crashing under Shell Launcher: give the user Task Manager and stay down.
        /// </summary>
        private static int StopCrashLoop()
        {
            // no WPF application (or theme) exists yet, so a plain message box instead of CairoMessage
            System.Windows.MessageBox.Show(DisplayString.sCrashLoop_Message, DisplayString.sCrashLoop_Title,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

            try
            {
                Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Program: Unable to start Task Manager: {ex.Message}");
            }

            CrashLoopGuard.Reset();
            return ShellHost.StopExitCode;
        }

        private static bool GetMutex()
        {
            _cairoMutex = new System.Threading.Mutex(true, MutexName, out bool ok);

            return ok;
        }

        private static bool SingleInstanceCheck()
        {
            for (int i = 0; i < MutexAttempts; i++)
            {
                if (!GetMutex())
                {
                    // Dispose the mutex, otherwise it will never create new
                    _cairoMutex.Dispose();
                    System.Threading.Thread.Sleep(MutexWaitMs);
                }
                else
                {
                    return true;
                }
            }

            return false;
        }
    }
}