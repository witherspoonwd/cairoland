using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CairoDesktop.Common.Localization
{
    public class DisplayString
    {
        public static List<KeyValuePair<string, string>> Languages = new List<KeyValuePair<string, string>>()
        {
            new KeyValuePair<string, string>("Chinese (Simplified) 简体中文", "zh_CN"),
            new KeyValuePair<string, string>("Chinese (Traditional) 繁體中文", "zh_TW"),
            new KeyValuePair<string, string>("Czech", "cs_CZ"),
            new KeyValuePair<string, string>("Dutch (Nederlands)", "nl_NL"),
            new KeyValuePair<string, string>("English", "en_US"),
            new KeyValuePair<string, string>("Français", "fr_FR"),
            new KeyValuePair<string, string>("German", "de_DE"),
            new KeyValuePair<string, string>("Italiano", "it_IT"),
            new KeyValuePair<string, string>("Norsk bokmål", "nb_NO"),
            new KeyValuePair<string, string>("Polski", "pl_PL"),
            new KeyValuePair<string, string>("Português (Brasil)", "pt_BR"),
            new KeyValuePair<string, string>("Русский", "ru_RU"),
            new KeyValuePair<string, string>("Español (España)", "es_ES"),
            new KeyValuePair<string, string>("Svenska", "sv_SE"),
            new KeyValuePair<string, string>("Turkish (Türkçe)", "tr_TR"),
            new KeyValuePair<string, string>("한국어", "ko_KR"),
            new KeyValuePair<string, string>("Magyar (Hungarian)", "hu_HU"),
			new KeyValuePair<string, string>("Slovenščina (Slovensko)", "sl_SI"),
            new KeyValuePair<string, string>("日本語", "ja_JP")
        };

        public DisplayString()
        {

        }

        private static string getString([CallerMemberName]string stringName = null)
        {
            Dictionary<string, string> lang;
            bool isDefault = false;
            string useLang = Settings.Instance.Language.ToLower();

            if (useLang.StartsWith("fr_"))
            {
                lang = Language.fr_FR;
            }
            else if (useLang.StartsWith("it_"))
            {
                lang = Language.it_IT;
            }
            else if (useLang.StartsWith("pt_"))
            {
                lang = Language.pt_BR;
            }
            else if (useLang.StartsWith("sv_"))
            {
                lang = Language.sv_SE;
            }
            else if (useLang.StartsWith("zh_"))
            {
                if (useLang.StartsWith("zh_tw"))
                    lang = Language.zh_TW;
                else
                    lang = Language.zh_CN;
            }
            else if (useLang.StartsWith("cs_"))
            {
                lang = Language.cs_CZ;
            }
            else if (useLang.StartsWith("de_"))
            {
                lang = Language.de_DE;
            }
            else if (useLang.StartsWith("es_"))
            {
                lang = Language.es_ES;
            }
            else if (useLang.StartsWith("nb_"))
            {
                lang = Language.nb_NO;
            }
            else if (useLang.StartsWith("nl_"))
            {
                lang = Language.nl_NL;
            }
            else if (useLang.StartsWith("ru_"))
            {
                lang = Language.ru_RU;
            }
            else if (useLang.StartsWith("pl_"))
            {
                lang = Language.pl_PL;
            }
            else if (useLang.StartsWith("tr_"))
            {
                lang = Language.tr_TR;
            }
            else if (useLang.StartsWith("ko_"))
            {
                lang = Language.ko_KR;
            }
            else if (useLang.StartsWith("hu_"))
            {
                lang = Language.hu_HU;
            }
            else if (useLang.StartsWith("sl_"))
            {
                lang = Language.sl_SI;
            }
            else if (useLang.StartsWith("ja_"))
            {
                lang = Language.ja_JP;
            }
            else
            {
                lang = Language.en_US;
                isDefault = true;
            }

            if (lang.ContainsKey(stringName))
                return lang[stringName];
            else
            {
                // default is en_US - return string from there if not found in set language
                if (!isDefault)
                {
                    lang = Language.en_US;
                    if (lang.ContainsKey(stringName))
                        return lang[stringName];
                }
            }

            return stringName;
        }

        public static string sProgramsMenu => getString();


        public static string sCairoMenu_AboutCairo => getString();

        public static string sCairoMenu_CheckForUpdates => getString();

        public static string sCairoMenu_CairoSettings => getString();

        public static string sCairoMenu_WindowsControlPanel => getString();

        public static string sCairoMenu_WindowsSettings => getString();

        public static string sCairoMenu_Run => getString();

        public static string sCairoMenu_TaskManager => getString();

        public static string sCairoMenu_ExitCairo => getString();

        public static string sCairoMenu_Hibernate => getString();

        public static string sCairoMenu_Sleep => getString();

        public static string sCairoMenu_Restart => getString();

        public static string sCairoMenu_ShutDown => getString();

        public static string sCairoMenu_Lock => getString();

        public static string sCairoMenu_LogOff => getString();

        public static string sAbout_Version => getString();

        public static string sAbout_PreRelease => getString();

        public static string sAbout_Copyright => getString();

        public static string sInterface_OK => getString();

        public static string sInterface_Cancel => getString();

        public static string sInterface_Continue => getString();

        public static string sInterface_Finish => getString();

        public static string sInterface_Yes => getString();

        public static string sInterface_No => getString();

        public static string sInterface_Open => getString();

        public static string sInterface_OpenWith => getString();

        public static string sInterface_OpenAsAdministrator => getString();

        public static string sInterface_RunAsUser => getString();



        public static string sInterface_Copy => getString();

        public static string sInterface_Paste => getString();

        public static string sInterface_Delete => getString();

        public static string sInterface_Rename => getString();

        public static string sInterface_Properties => getString();

        public static string sInterface_Browse => getString();

        public static string sInterface_Go => getString();

        public static string sInterface_MoveDown => getString();

        public static string sInterface_MoveUp => getString();

        public static string sInterface_Dismiss => getString();

        public static string sAppGrabber => getString();

        public static string sAppGrabber_PleaseWait => getString();

        public static string sAppGrabber_Page1Title => getString();

        public static string sAppGrabber_Page1Line1 => getString();

        public static string sAppGrabber_Page1Line2 => getString();

        public static string sAppGrabber_ProgramsMenuItems => getString();

        public static string sAppGrabber_InstalledApplications => getString();

        public static string sAppGrabber_Page2Title => getString();

        public static string sAppGrabber_Page2Line1 => getString();

        public static string sAppGrabber_Page2Line2 => getString();

        public static string sAppGrabber_Page2Directions => getString();

        public static string sAppGrabber_Page2Directions1 => getString();

        public static string sAppGrabber_Page2Directions2 => getString();

        public static string sAppGrabber_Page2Directions3 => getString();

        public static string sAppGrabber_Page2Directions4 => getString();

        public static string sAppGrabber_Page2Directions5 => getString();

        public static string sAppGrabber_Page2Directions6 => getString();

        public static string sAppGrabber_ProgramCategories => getString();

        public static string sAppGrabber_Hide => getString();

        public static string sAppGrabber_QuickLaunch => getString();

        public static string sAppGrabber_Uncategorized => getString();

        public static string sAppGrabber_All => getString();

        public static string sAppGrabber_Untitled => getString();

        public static string sAppGrabber_Category_Accessories => getString();

        public static string sAppGrabber_Category_Productivity => getString();

        public static string sAppGrabber_Category_Development => getString();

        public static string sAppGrabber_Category_Graphics => getString();

        public static string sAppGrabber_Category_Media => getString();

        public static string sAppGrabber_Category_Internet => getString();

        public static string sAppGrabber_Category_Games => getString();

        public static string sRun_Title => getString();

        public static string sRun_Info => getString();

        public static string sExitCairo_Title => getString();

        public static string sExitCairo_Info => getString();

        public static string sExitCairo_ExitCairo => getString();

        public static string sLogoff_Title => getString();

        public static string sLogoff_Info => getString();

        public static string sLogoff_Logoff => getString();

        public static string sRestart_Title => getString();

        public static string sRestart_Info => getString();

        public static string sRestart_Restart => getString();

        public static string sShutDown_Title => getString();

        public static string sShutDown_Info => getString();

        public static string sShutDown_ShutDown => getString();


        public static string sProgramsMenu_UninstallAProgram => getString();





        public static string sProgramsMenu_RemoveTitle => getString();

        public static string sProgramsMenu_RemoveInfo => getString();

        public static string sProgramsMenu_AlwaysAdminTitle => getString();

        public static string sProgramsMenu_AlwaysAdminInfo => getString();

        public static string sProgramsMenu_Remove => getString();

        public static string sProgramsMenu_UWPInfo => getString();








        public static string sProgramsMenu_RenameTitle => getString();

        public static string sProgramsMenu_RenameAppInfo => getString();



        public static string sStacks_OpenInNewWindow => getString();




        public static string sMenuBar_OpenDateTimeSettings => getString();

        public static string sMenuBar_OpenActionCenter => getString();

        public static string sMenuBar_ToggleNotificationArea => getString();

        public static string sMenuBar_Volume => getString();

        public static string sMenuBar_OpenSoundSettings => getString();

        public static string sMenuBar_OpenVolumeMixer => getString();

        public static string sSearch_Title => getString();

        public static string sSearch_ViewAllResults => getString();

        public static string sSearch_LastModified => getString();

        public static string sSearch_Error => getString();

        public static string sDesktop_DisplaySettings => getString();

        public static string sDesktop_Personalize => getString();

        public static string sDesktop_DeleteTitle => getString();

        public static string sDesktop_DeleteInfo => getString();

























        public static string sError_OhNo => getString();

        public static string sError_FileNotFoundInfo => getString();

        public static string sError_CantOpenAppWiz => getString();

        public static string sSettings_General => getString();

        public static string sSettings_MenuBar => getString();



        public static string sSettings_Appearance => getString();

        public static string sSettings_Advanced => getString();

        public static string sSettings_Behavior => getString();

        public static string sSettings_Default => getString();

        public static string sSettings_RestartCairo => getString();

        public static string sSettings_Restarting => getString();

        public static string sSettings_General_UpdateCheck => getString();

        public static string sSettings_General_RunAtLogOn => getString();

        public static string sSettings_General_Language => getString();

        public static string sSettings_General_Theme => getString();

        public static string sSettings_General_ThemeTooltip => getString();

        public static string sSettings_General_Background => getString();

        public static string sSettings_General_BackgroundTooltip => getString();

        public static string sSettings_General_BackgroundWindows => getString();

        public static string sSettings_General_BackgroundBrowse => getString();

        public static string sSettings_General_BackgroundReset => getString();

        public static string sSettings_General_BackgroundStyle => getString();

        public static string sSettings_General_BackgroundFill => getString();

        public static string sSettings_General_BackgroundFit => getString();

        public static string sSettings_General_BackgroundStretch => getString();

        public static string sSettings_General_BackgroundCenter => getString();

        public static string sSettings_General_BackgroundTile => getString();

        public static string sSettings_General_TimeFormat => getString();

        public static string sSettings_General_TimeFormatTooltip => getString();

        public static string sSettings_General_DateFormat => getString();

        public static string sSettings_General_DateFormatTooltip => getString();

        public static string sSettings_General_FilesAndFolders => getString();

        public static string sSettings_General_ShowFileExtensions => getString();

        public static string sSettings_General_ForceSoftwareRendering => getString();

        public static string sSettings_General_FileManager => getString();



        public static string sSettings_MenuBar_EnableMenuBarAutoHide => getString();

        public static string sSettings_MenuBar_EnableMenuBarShadow => getString();

        public static string sSettings_MenuBar_MenuBarEdge => getString();

        public static string sSettings_MenuBar_NotificationArea => getString();

        public static string sSettings_MenuBar_EnableNotificationArea => getString();

        public static string sSettings_MenuBar_ShowNotifyIcons => getString();

        public static string sSettings_MenuBar_ShowNotifyIconsCollapsed => getString();

        public static string sSettings_MenuBar_ShowNotifyIconsAlways => getString();

        public static string sSettings_MenuBar_CollapsibleIcons => getString();

        public static string sSettings_MenuBar_PinnedIcons => getString();

        public static string sSettings_MenuBar_NotificationAreaPinHelp => getString();

        public static string sSettings_MenuBar_EnableCairoMenuHotKey => getString();


        public static string sSettings_MenuBar_EnableMenuBarBlur => getString();


        public static string sSettings_MenuBar_ShowHibernate => getString();





        public static string sSettings_MenuBar_MenuExtras => getString();

        public static string sSettings_MenuBar_EnableMenuExtraVolume => getString();

        public static string sSettings_MenuBar_EnableMenuExtraActionCenter => getString();

        public static string sSettings_MenuBar_EnableMenuExtraClock => getString();

        public static string sSettings_MenuBar_EnableMenuExtraSearch => getString();
























        public static string sSettings_IconSize => getString();

        public static string sSettings_IconSizeLarge => getString();

        public static string sSettings_IconSizeMedium => getString();

        public static string sSettings_IconSizeSmall => getString();



        public static string sSettings_PositionBottom => getString();

        public static string sSettings_PositionTop => getString();





















        public static string sSettings_Advanced_LoggingLevel => getString();

        public static string sSettings_Advanced_Shell => getString();

        public static string sSettings_Advanced_CairoIsShell => getString();

        public static string sSettings_Advanced_ExplorerIsShell => getString();

        public static string sSettings_Advanced_SetCairoAsShell => getString();

        public static string sSettings_Advanced_SetExplorerAsShell => getString();

        public static string sSettings_Advanced_ShellChanged => getString();

        public static string sSettings_Advanced_ShellChangedText => getString();

        public static string sSettings_Advanced_LogOffNow => getString();

        public static string sSettings_Advanced_LogOffLater => getString();

        public static string sSettings_Advanced_OpenLogsFolder => getString();

        public static string sSettings_Advanced_AutoHideShowDelayMs => getString();

        public static string sWelcome_StartTour => getString();

        public static string sWelcome_FinishTour => getString();

        public static string sWelcome_Welcome => getString();

        public static string sWelcome_SelectLanguage => getString();

        public static string sWelcome_ChangingLanguage => getString();

        public static string sWelcome_ChangingLanguageText => getString();

        public static string sWelcome_MenuBar => getString();



        public static string sWelcome_MenuBarText => getString();

        public static string sWelcome_MenuBarSec1Heading => getString();

        public static string sWelcome_MenuBarSec1Text => getString();







        public static string sWelcome_MenuBarSec5Heading => getString();

        public static string sWelcome_MenuBarSec5Text => getString();

        public static string sWelcome_MenuBarSec6Heading => getString();

        public static string sWelcome_MenuBarSec6Text => getString();













        public static string sCommand_OpenFile => getString();

        public static string sCommand_OpenLocation => getString();



        public static string sCommand_AddToPrograms => getString();

        public static string sCommand_AddToQuickLaunch => getString();

        public static string sCommand_ToggleCairoMenu => getString();


        public static string sCommand_ToggleCalendar => getString();

        public static string sCommand_ToggleSearch => getString();

        public static string sCommand_TilingViewTag => getString();

        public static string sCommand_TilingMoveToTag => getString();

        public static string sCommand_TilingViewPreviousTag => getString();

        public static string sCommand_TilingFocusDirection => getString();

        public static string sCommand_TilingSwapDirection => getString();

        public static string sCommand_TilingMoveDirection => getString();

        public static string sCommand_TilingResize => getString();

        public static string sCommand_TilingFocusNext => getString();

        public static string sCommand_TilingFocusPrev => getString();

        public static string sCommand_TilingToggleOrientation => getString();

        public static string sCommand_TilingEqualize => getString();

        public static string sCommand_TilingToggleFloating => getString();

        public static string sCommand_TilingToggleFullscreen => getString();

        public static string sCommand_TilingCloseWindow => getString();

        public static string sCommand_TilingFocusScreen => getString();

        public static string sCommand_TilingMoveToScreen => getString();

        public static string sCommand_TilingLaunchTerminal => getString();

        public static string sCommand_ToggleProgramLauncher => getString();

        public static string sCommand_DisplayProjection => getString();

        public static string sCommand_RestartCairo => getString();

        public static string sCommand_CloseActiveWindow => getString();

        public static string sCommand_RunCommand => getString();

        public static string sWindowManager => getString();

        public static string sWindowManager_EnableTiling => getString();

        public static string sWindowManager_FocusFollowsMouse => getString();

        public static string sWindowManager_RaiseOnFocus => getString();

        public static string sWindowManager_FollowActivation => getString();

        public static string sWindowManager_Gaps => getString();

        public static string sWindowManager_Settings => getString();

        public static string sWindowManager_MoveToTag => getString();

        public static string sWindowManager_Float => getString();

        public static string sSettings_Tiling => getString();

        public static string sSettings_Tiling_EnableTiling => getString();

        public static string sSettings_Tiling_EnableTilingOutsideShell => getString();

        public static string sSettings_Tiling_Gap => getString();

        public static string sSettings_Tiling_FollowActivation => getString();

        public static string sSettings_Tiling_Modifier => getString();

        public static string sSettings_Tiling_TerminalCommand => getString();

        public static string sSettings_Tiling_FloatClasses => getString();

        public static string sSettings_Tiling_FloatExes => getString();

        public static string sSettings_Tiling_FocusFollowsMouse => getString();

        public static string sSettings_Tiling_FocusFollowsMouseRaise => getString();

        public static string sSettings_Tiling_FocusFollowsMouseDelay => getString();

        public static string sSettings_Tiling_FocusFollowsMouseMode => getString();

        public static string sSettings_Tiling_RemapWinL => getString();

        public static string sSettings_Tiling_RemapWinLUnavailable => getString();

        public static string sSettings_Tiling_RemapWinLFix => getString();

        public static string sLockFix_Title => getString();

        public static string sLockFix_Message => getString();

        public static string sLockFix_Fix => getString();

        public static string sLockFix_NotNow => getString();

        public static string sLockFix_Fixed => getString();

        public static string sLockFix_Failed => getString();

        public static string sCrashLoop_Title => getString();

        public static string sCrashLoop_Message => getString();

        public static string sSetupCheck_Title => getString();

        public static string sSetupCheck_Intro => getString();

        public static string sSetupCheck_Running => getString();

        public static string sSetupCheck_CheckAgain => getString();

        public static string sSetupCheck_Done => getString();

        public static string sSetupCheck_EnvironmentUnavailable => getString();

        public static string sSetupCheck_ShellLauncherActive => getString();

        public static string sSetupCheck_UseExplorer => getString();

        public static string sSetupCheck_ShellLauncherForeign => getString();

        public static string sSetupCheck_ShellLauncherForeignDetail => getString();

        public static string sSetupCheck_Reapply => getString();

        public static string sSetupCheck_SignOutToFinish => getString();

        public static string sSetupCheck_SignOutToFinishDetail => getString();

        public static string sSetupCheck_SignOut => getString();

        public static string sSetupCheck_ShellLauncherMissing => getString();

        public static string sSetupCheck_ShellLauncherMissingDetail => getString();

        public static string sSetupCheck_OldShell => getString();

        public static string sSetupCheck_OldShellDetail => getString();

        public static string sSetupCheck_UseCairoAsShell => getString();

        public static string sSetupCheck_ExplorerIsShell => getString();

        public static string sSetupCheck_DuplicateRun => getString();

        public static string sSetupCheck_DuplicateRunDetail => getString();

        public static string sSetupCheck_Remove => getString();

        public static string sSetupCheck_OldWinlogon => getString();

        public static string sSetupCheck_LockPolicyDetail => getString();

        public static string sSetupCheck_ScriptMissing => getString();

        public static string sSetupCheck_FixFailed => getString();

        public static string sSetupCheck_RestartToFinish => getString();

        public static string sSettings_Advanced_ShellSetup => getString();

        public static string sSettings_Advanced_ShellLauncherActive => getString();

        public static string sSettings_Advanced_ShellLauncherPending => getString();

        public static string sSettings_Advanced_OldShell => getString();

        public static string sSettings_Shortcuts => getString();

        public static string sSettings_Shortcuts_Help => getString();

        public static string sSettings_Shortcuts_Key => getString();

        public static string sSettings_Shortcuts_Action => getString();

        public static string sSettings_Shortcuts_Value => getString();

        public static string sSettings_Shortcuts_Add => getString();

        public static string sSettings_Shortcuts_Reset => getString();

        public static string sSettings_Shortcuts_Remove => getString();

        public static string sSettings_Shortcuts_Yours => getString();

        public static string sSettings_Shortcuts_BuiltIn => getString();

        public static string sShortcutState_Active => getString();

        public static string sShortcutState_Hooked => getString();

        public static string sShortcutState_Replaces => getString();

        public static string sShortcutState_Overridden => getString();

        public static string sShortcutState_Duplicate => getString();

        public static string sShortcutState_Incomplete => getString();

        public static string sShortcutState_NeedsValue => getString();

        public static string sShortcutState_LockKey => getString();

        public static string sShortcutState_InactiveTiling => getString();

        public static string sShortcutState_InactiveShell => getString();

        public static string sShortcutState_Inactive => getString();

        public static string sSettings_Tiling_ExplorerHotkeysHelp => getString();

        public static string sSettings_Tiling_ElevatedHelp => getString();

        public static string sSettings_Tiling_ElevatedHelpUIAccess => getString();

        public static string sSettings_Tiling_Behavior => getString();

        public static string sSettings_Tiling_Focus => getString();

        public static string sSettings_Tiling_Keyboard => getString();

        public static string sLauncher_Placeholder => getString();

        public static string sLauncher_NoResults => getString();

        public static string sLauncher_RunPrompt => getString();

        public static string sLauncher_RunCommand => getString();

        public static string sLauncher_RunFailed => getString();
    }
}
