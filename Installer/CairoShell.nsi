;--------------------------------
; CairoShell.nsi

!ifndef ARCBITS | ARCNAME | NETTARGET | OUTNAME
!error Defines not set, compile CairoShell_<32|64>.nsi instead!"
!endif

; Folder holding the Shell Launcher scripts (repo root). Build-Installer.ps1 passes an absolute path.
!ifndef SETUPSCRIPTS
!define SETUPSCRIPTS "..\"
!endif

!define PUBLISHDIR "..\Cairo Desktop\Cairo Desktop\bin\Release\${NETTARGET}\publish-${ARCNAME}"

; The name of the installer
Name "Cairo Desktop Environment"

; Use Unicode rather than ANSI
Unicode True

; The file to write
OutFile "${OUTNAME}.exe"

; The default installation directory
InstallDir "$PROGRAMFILES${ARCBITS}\Cairo Shell"

; Registry key to check for directory (so if you install again, it will
; overwrite the old one automatically)
InstallDirRegKey HKLM "Software\CairoShell" "Install_Dir"

; Request admin rights on Vista+ (when UAC is turned on)
RequestExecutionLevel Admin

; Minimum .NET Framework release (4.8)
!define MIN_FRA_RELEASE "528040"

!define MUI_ABORTWARNING
!include "MUI.nsh"
!include "LogicLib.nsh"
!include "nsDialogs.nsh"
!include "x64.nsh"
!include "StrFunc.nsh"
${StrStr}

; Start mode: Shell (Shell Launcher replaces Explorer), Alongside (Run key) or Manual
Var StartMode
Var ShellSupported
Var ShellReason
Var AccountName
Var AccountSid
Var EnvIni
Var PowerShell
Var RadioShell
Var RadioAlongside
Var RadioManual

;--------------------------------
; Pages

  !define MUI_ICON inst_icon.ico
  !define MUI_HEADERIMAGE
  !define MUI_HEADERIMAGE_RIGHT
  !define MUI_HEADERIMAGE_BITMAP header_img.bmp
  !define MUI_UNWELCOMEFINISHPAGE_BITMAP left_img.bmp
  !define MUI_UNICON inst_icon.ico
  !define MUI_WELCOMEFINISHPAGE_BITMAP left_img.bmp
  !define MUI_WELCOMEPAGE_TITLE_3LINES
  !define MUI_FINISHPAGE_TITLE_3LINES
  !define MUI_FINISHPAGE_RUN
  !define MUI_FINISHPAGE_RUN_TEXT "$(PAGE_Finish_RunText)"
  !define MUI_FINISHPAGE_RUN_FUNCTION "FinishAction"
  !define MUI_PAGE_CUSTOMFUNCTION_SHOW "FinishShow"
  !insertmacro MUI_PAGE_WELCOME
  !insertmacro MUI_PAGE_LICENSE "License.txt"
  Page custom StartModePage StartModePageLeave
  !insertmacro MUI_PAGE_INSTFILES
  !insertmacro MUI_PAGE_FINISH

  !define MUI_WELCOMEPAGE_TITLE_3LINES
  !define MUI_FINISHPAGE_TITLE_3LINES
  !insertmacro MUI_UNPAGE_WELCOME
  !define MUI_UNCONFIRMPAGE_TEXT_TOP "$(PAGE_UnDir_TopText)"
  !insertmacro MUI_UNPAGE_CONFIRM
  !insertmacro MUI_UNPAGE_INSTFILES
  !insertmacro MUI_UNPAGE_FINISH

;--------------------------------
; Languages

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "French"
!insertmacro MUI_LANGUAGE "Slovenian"

;--------------------------------
; Initialization

!macro EnsureAllUserRights
  UserInfo::GetAccountType
  Pop $0
  ReadEnvStr $1 "__COMPAT_LAYER"
  ${If} $0 != "Admin"
  ${AndIf} $0 != "Power"
  ${AndIf} $1 != "RunAsInvoker"
    MessageBox MB_IconStop "Administrator rights required!"
    SetErrorLevel 740 ; ERROR_ELEVATION_REQUIRED
    Quit
  ${EndIf}
!macroend

; 64-bit PowerShell where there is one: DISM (Enable-WindowsOptionalFeature) refuses to run under WOW64
!macro FindPowerShell
  ${If} ${RunningX64}
  ${AndIf} ${FileExists} "$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
    StrCpy $PowerShell "$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
  ${Else}
    StrCpy $PowerShell "$SYSDIR\WindowsPowerShell\v1.0\powershell.exe"
  ${EndIf}
!macroend

Function .onInit
  SetShellVarContext All
  !insertmacro EnsureAllUserRights
  !insertmacro FindPowerShell
  InitPluginsDir

  ; reuse the choice from the last install; a silent install never asks
  ReadRegStr $StartMode HKLM "Software\CairoShell" "StartMode"
  ${If} $StartMode != "Shell"
  ${AndIf} $StartMode != "Alongside"
  ${AndIf} $StartMode != "Manual"
    StrCpy $StartMode "Alongside"
  ${EndIf}
FunctionEnd

Function un.onInit
  SetShellVarContext All
  !insertmacro EnsureAllUserRights
  !insertmacro FindPowerShell
FunctionEnd

;--------------------------------
; Start mode page

; Runs Test-CairoEnvironment.ps1 (read-only) once, and reads its INI
Function RunEnvironmentCheck
  ${If} $EnvIni != ""
    Return
  ${EndIf}

  SetOutPath "$PLUGINSDIR\setup"
  File "${SETUPSCRIPTS}CairoShellLauncher.psm1"
  File "${SETUPSCRIPTS}Test-CairoEnvironment.ps1"
  SetOutPath "$PLUGINSDIR\check"
  File /nonfatal "${PUBLISHDIR}\CairoDesktop.UIAccess.exe"

  StrCpy $EnvIni "$PLUGINSDIR\env.ini"
  nsExec::Exec '"$PowerShell" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\setup\Test-CairoEnvironment.ps1" -CairoDir "$PLUGINSDIR\check" -IniPath "$EnvIni"'
  Pop $0

  ReadINIStr $ShellSupported $EnvIni "Summary" "ShellSupported"
  ReadINIStr $ShellReason $EnvIni "Summary" "ShellReason"
  ReadINIStr $AccountName $EnvIni "Summary" "AccountName"
  ReadINIStr $AccountSid $EnvIni "Summary" "AccountSid"
  ${If} $ShellSupported == ""
    StrCpy $ShellSupported "0"
    StrCpy $ShellReason "$(PAGE_StartMode_CheckFailed)"
  ${EndIf}
FunctionEnd

Function StartModePage
  Call RunEnvironmentCheck

  !insertmacro MUI_HEADER_TEXT "$(PAGE_StartMode_Title)" "$(PAGE_StartMode_Subtitle)"
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  ${NSD_CreateRadioButton} 0 0 100% 11u "$(PAGE_StartMode_Shell)"
  Pop $RadioShell
  ${If} $ShellSupported == "1"
    ${NSD_CreateLabel} 12u 11u -12u 10u "$(PAGE_StartMode_ShellFor) $AccountName"
  ${Else}
    ${NSD_CreateLabel} 12u 11u -12u 18u "$ShellReason"
  ${EndIf}
  Pop $0

  ${NSD_CreateRadioButton} 0 30u 100% 11u "$(PAGE_StartMode_Alongside)"
  Pop $RadioAlongside
  ${NSD_CreateRadioButton} 0 42u 100% 11u "$(PAGE_StartMode_Manual)"
  Pop $RadioManual

  ${NSD_CreateLabel} 0 58u 100% 10u "$(PAGE_StartMode_Checks)"
  Pop $0
  nsDialogs::CreateControl LISTBOX ${DEFAULT_STYLES}|${WS_TABSTOP}|${WS_VSCROLL}|${WS_HSCROLL}|${LBS_NOINTEGRALHEIGHT}|${LBS_NOSEL} ${WS_EX_CLIENTEDGE} 0 69u 100% 71u ""
  Pop $R8
  SendMessage $R8 ${LB_SETHORIZONTALEXTENT} 1200 0

  ; one line per check, details indented below it
  ReadINIStr $R0 $EnvIni "Summary" "Count"
  ${If} $R0 == ""
    StrCpy $R0 0
  ${EndIf}
  StrCpy $R1 0
  ${DoWhile} $R1 < $R0
    ReadINIStr $R2 $EnvIni "Check$R1" "Status"
    ReadINIStr $R3 $EnvIni "Check$R1" "Title"
    ${Select} $R2
      ${Case} "pass"
        StrCpy $R2 "$(PAGE_StartMode_Pass)"
      ${Case} "warn"
        StrCpy $R2 "$(PAGE_StartMode_Warn)"
      ${Case} "fail"
        StrCpy $R2 "$(PAGE_StartMode_Fail)"
      ${CaseElse}
        StrCpy $R2 "$(PAGE_StartMode_Info)"
    ${EndSelect}
    ${NSD_LB_AddString} $R8 "$R2  $R3"

    ReadINIStr $R4 $EnvIni "Check$R1" "DetailCount"
    ${If} $R4 == ""
      StrCpy $R4 0
    ${EndIf}
    StrCpy $R5 0
    ${DoWhile} $R5 < $R4
      ReadINIStr $R6 $EnvIni "Check$R1" "Detail$R5"
      ${NSD_LB_AddString} $R8 "        $R6"
      IntOp $R5 $R5 + 1
    ${Loop}

    IntOp $R1 $R1 + 1
  ${Loop}

  ${If} $ShellSupported != "1"
    EnableWindow $RadioShell 0
    ${If} $StartMode == "Shell"
      StrCpy $StartMode "Alongside"
    ${EndIf}
  ${EndIf}

  ${Select} $StartMode
    ${Case} "Shell"
      ${NSD_Check} $RadioShell
    ${Case} "Manual"
      ${NSD_Check} $RadioManual
    ${CaseElse}
      ${NSD_Check} $RadioAlongside
  ${EndSelect}

  nsDialogs::Show
FunctionEnd

Function StartModePageLeave
  ${NSD_GetState} $RadioShell $0
  ${NSD_GetState} $RadioManual $1
  ${If} $0 == ${BST_CHECKED}
    StrCpy $StartMode "Shell"
  ${ElseIf} $1 == ${BST_CHECKED}
    StrCpy $StartMode "Manual"
  ${Else}
    StrCpy $StartMode "Alongside"
  ${EndIf}
FunctionEnd

;--------------------------------
; Installer

Section "$(SECT_cairo)" cairo

  SectionIn RO

  ; Check .NET version
  StrCmp ${NETTARGET} "net480" 0 no_net_check
    Call AbortIfBadFramework
  no_net_check:

  ; Set output path to the installation directory.
  SetOutPath $INSTDIR

  Call AbortIfRunning

  ; Put file there
  DetailPrint "Installing Cairo files"
  File /r "${PUBLISHDIR}\"

  ; Shell Launcher scripts, also used by Cairo's Setup check and for recovery
  SetOutPath "$INSTDIR\Setup"
  File "${SETUPSCRIPTS}CairoShellLauncher.psm1"
  File "${SETUPSCRIPTS}Enable-CairoShellLauncher.ps1"
  File "${SETUPSCRIPTS}Disable-CairoShellLauncher.ps1"
  File "${SETUPSCRIPTS}Test-CairoEnvironment.ps1"
  SetOutPath $INSTDIR

  ; Start menu shortcuts
  CreateShortcut "$SMPROGRAMS\Cairo Desktop.lnk" "$INSTDIR\CairoDesktop.exe"

  ; Write the installation path into the registry
  WriteRegStr HKLM SOFTWARE\CairoShell "Install_Dir" "$INSTDIR"

  ; Write the uninstall keys for Windows
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CairoShell" "DisplayName" "Cairo Desktop Environment"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CairoShell" "DisplayIcon" '"$INSTDIR\RemoveCairo.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CairoShell" "DisplayVersion" "BUILD_VERSION"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CairoShell" "UninstallString" '"$INSTDIR\RemoveCairo.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CairoShell" "URLInfoAbout" "https://cairodesktop.com"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CairoShell" "Publisher" "Cairo Development Team"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CairoShell" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CairoShell" "NoRepair" 1
  WriteUninstaller "$INSTDIR\RemoveCairo.exe"

SectionEnd

; Applies the start mode after the files are in place
Section "-StartMode" startMode

  WriteRegStr HKLM "Software\CairoShell" "StartMode" "$StartMode"

  ${If} $StartMode == "Shell"
    ; a silent reinstall (Deploy-Cairo.ps1) keeps a configuration that is already there
    ReadRegDWORD $0 HKCU "Software\CairoShell" "ShellLauncher"
    ${If} ${Silent}
    ${AndIf} $0 == 1
      DetailPrint "Keeping the existing Shell Launcher configuration"
    ${Else}
      StrCpy $1 ""
      ${If} $AccountSid != ""
        StrCpy $1 '-Sid "$AccountSid"'
      ${EndIf}

      ; enables the features, writes the XML as SYSTEM, removes old Winlogon/Run leftovers, fixes the Win+L policy ACL
      DetailPrint "Configuring Shell Launcher"
      nsExec::ExecToLog '"$PowerShell" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Setup\Enable-CairoShellLauncher.ps1" $1 -FixLockPolicy -NoSignOutPrompt'
      Pop $0
      ${If} $0 == 3010
        DetailPrint "Windows must restart to finish enabling Shell Launcher"
        SetRebootFlag true
      ${ElseIf} $0 != 0
        MessageBox MB_OK|MB_ICONEXCLAMATION "$(DLOG_ShellLauncherFailed)" /SD IDOK
      ${EndIf}
    ${EndIf}
  ${Else}
    ${If} $AccountSid != ""
      ${If} $StartMode == "Alongside"
        WriteRegStr HKU "$AccountSid\Software\Microsoft\Windows\CurrentVersion\Run" "CairoShell" "$INSTDIR\CairoDesktop.exe"
      ${Else}
        DeleteRegValue HKU "$AccountSid\Software\Microsoft\Windows\CurrentVersion\Run" "CairoShell"
      ${EndIf}

      ; the old way of replacing Explorer
      ReadRegStr $0 HKU "$AccountSid\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" "Shell"
      ${StrStr} $1 $0 "Cairo"
      ${If} $1 != ""
        DeleteRegValue HKU "$AccountSid\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" "Shell"
      ${EndIf}

      ReadRegDWORD $2 HKU "$AccountSid\Software\CairoShell" "ShellLauncher"
    ${Else}
      ${If} $StartMode == "Alongside"
        WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "CairoShell" "$INSTDIR\CairoDesktop.exe"
      ${Else}
        DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "CairoShell"
      ${EndIf}

      ReadRegStr $0 HKCU "Software\Microsoft\Windows NT\CurrentVersion\Winlogon" "Shell"
      ${StrStr} $1 $0 "Cairo"
      ${If} $1 != ""
        DeleteRegValue HKCU "Software\Microsoft\Windows NT\CurrentVersion\Winlogon" "Shell"
      ${EndIf}

      ReadRegDWORD $2 HKCU "Software\CairoShell" "ShellLauncher"
    ${EndIf}

    ; no Shell Launcher configuration may remain for this user
    ${If} $2 == 1
      DetailPrint "Removing the Shell Launcher configuration"
      StrCpy $1 ""
      ${If} $AccountSid != ""
        StrCpy $1 '-Sid "$AccountSid"'
      ${EndIf}
      nsExec::ExecToLog '"$PowerShell" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Setup\Disable-CairoShellLauncher.ps1" $1 -KeepStartMode -NoSignOutPrompt'
      Pop $0
    ${EndIf}
  ${EndIf}

SectionEnd

  ;Language strings

  LangString PAGE_StartMode_Title ${LANG_ENGLISH} "How should Cairo start?"
  LangString PAGE_StartMode_Subtitle ${LANG_ENGLISH} "You can change this later in Cairo Settings > Advanced."
  LangString PAGE_StartMode_Shell ${LANG_ENGLISH} "Use Cairo as my shell (replaces Explorer)"
  LangString PAGE_StartMode_ShellFor ${LANG_ENGLISH} "Through Shell Launcher. Cairo will be the shell for"
  LangString PAGE_StartMode_Alongside ${LANG_ENGLISH} "Run Cairo alongside Explorer at sign-in"
  LangString PAGE_StartMode_Manual ${LANG_ENGLISH} "Don't start Cairo automatically"
  LangString PAGE_StartMode_Checks ${LANG_ENGLISH} "Checks:"
  LangString PAGE_StartMode_CheckFailed ${LANG_ENGLISH} "The environment check couldn't run, so Shell Launcher can't be set up from here."
  LangString PAGE_StartMode_Pass ${LANG_ENGLISH} "[OK]"
  LangString PAGE_StartMode_Warn ${LANG_ENGLISH} "[Warning]"
  LangString PAGE_StartMode_Fail ${LANG_ENGLISH} "[Problem]"
  LangString PAGE_StartMode_Info ${LANG_ENGLISH} "[Note]"
  LangString PAGE_Finish_SignOutText ${LANG_ENGLISH} "Sign out now to start Cairo as your shell"
  LangString DLOG_ShellLauncherFailed ${LANG_ENGLISH} "Cairo couldn't be set up as your shell. Explorer stays your shell for now.$\r$\n$\r$\nOpen Cairo Settings > Advanced to see what went wrong and try again."

  ;Language strings

  LangString PAGE_Welcome_Text_net480 ${LANG_ENGLISH} "This installer will guide you through the installation of Cairo.\r\n\r\nBefore installing, please ensure .NET Framework 4.8 or higher is installed, and that any running instance of Cairo is ended.\r\n\r\nClick Next to continue."
  LangString PAGE_Welcome_Text ${LANG_ENGLISH} "This installer will guide you through the installation of Cairo.\r\n\r\nBefore installing, please ensure that any running instance of Cairo is ended.\r\n\r\nClick Next to continue."
  LangString PAGE_Finish_RunText ${LANG_ENGLISH} "Start Cairo Desktop Environment"
  LangString PAGE_UnDir_TopText ${LANG_ENGLISH} "Please be sure that you have closed Cairo before uninstalling to ensure that all files are removed. All files in the directory below will be removed."
  LangString DLOG_RunningText ${LANG_ENGLISH} "Cairo is currently running. Please exit Cairo from the Cairo menu and run this installer again."
  LangString DLOG_RunningText2 ${LANG_ENGLISH} "Cairo is currently running. Please exit Cairo from the Cairo menu."
  LangString DLOG_DotNetText ${LANG_ENGLISH} "Cairo requires Microsoft .NET Framework 4.8 or higher. Please install this from the Microsoft web site and install Cairo again."
  LangString SECT_cairo ${LANG_ENGLISH} "Cairo Desktop (required)"

  LangString PAGE_Welcome_Text_net480 ${LANG_FRENCH} "Cet installateur va vous guider au long de l'installation de Cairo.\r\n\r\nAvant d'installer, veuillez vous assurer que le .NET Framework 4.8 ou plus récent est installé, et que vous avez quitté toute instance de Cairo encore en cours de fonctionnement.\r\n\r\nCliquez sur Suivant pour continuer."
  LangString PAGE_Welcome_Text ${LANG_FRENCH} "Cet installateur va vous guider au long de l'installation de Cairo.\r\n\r\nAvant d'installer, veuillez vous assurer que vous avez quitté toute instance de Cairo encore en cours de fonctionnement.\r\n\r\nCliquez sur Suivant pour continuer."
  LangString PAGE_Finish_RunText ${LANG_FRENCH} "Démarrer l'environnement de bureau Cairo"
  LangString PAGE_UnDir_TopText ${LANG_FRENCH} "Veuillez vérifier que vous avez fermé Cairo avant de le désinstaller pour assurer que tous les fichiers soient supprimés. All files in the directory below will be removed."
  LangString DLOG_RunningText ${LANG_FRENCH} "Cairo est en cours de fonctionnement. Veuillez quitter Cairo depuis le menu Cairo et lancer de nouveau cet installateur."
  LangString DLOG_RunningText2 ${LANG_FRENCH} "Cairo est en cours de fonctionnement. Veuillez quitter Cairo depuis le menu Cairo."
  LangString DLOG_DotNetText ${LANG_FRENCH} "Cairo nécessite le Microsoft .NET Framework 4.8 ou plus récent. Veuillez l'installer depuis le site web de Microsoft et installer de nouveau Cairo."
  LangString SECT_cairo ${LANG_FRENCH} "Bureau Cairo (requis)"
  
  LangString PAGE_Welcome_Text_net480 ${LANG_SLOVENIAN} "Ta namestitveni program vas bo vodil skozi namestitev programa Cairo.\r\n\r\nPred namestitvijo se prepričajte, da imate nameščen .NET Framework 4.8 ali novejši ter da je morebitna zagnana instanca programa Cairo ustavljena.\r\n\r\nKliknite Naprej za nadaljevanje."
  LangString PAGE_Welcome_Text ${LANG_SLOVENIAN} "Ta namestitveni program vas bo vodil skozi namestitev programa Cairo.\r\n\r\nPred namestitvijo se prepričajte, da je morebitna zagnana instanca programa Cairo ustavljena.\r\n\r\nKliknite Naprej za nadaljevanje."
  LangString PAGE_Finish_RunText ${LANG_SLOVENIAN} "Zaženi Cairo namizno okolje"
  LangString PAGE_UnDir_TopText ${LANG_SLOVENIAN} "Pred odstranitvijo se prepričajte, da je program Cairo zaprt, da zagotovite odstranitev vseh datotek. Vse datoteke v spodnji mapi bodo odstranjene."
  LangString DLOG_RunningText ${LANG_SLOVENIAN} "Program Cairo je trenutno zagnan. Zaprite ga v meniju Cairo in znova zaženite ta namestitveni program."
  LangString DLOG_RunningText2 ${LANG_SLOVENIAN} "Program Cairo je trenutno zagnan. Zaprite ga v meniju Cairo."
  LangString DLOG_DotNetText ${LANG_SLOVENIAN} "Program Cairo zahteva Microsoft .NET Framework 4.8 ali novejši. Namestite ga s spletne strani Microsoft in nato znova namestite program Cairo."
  LangString SECT_cairo ${LANG_SLOVENIAN} "Cairo namizje (obvezno)"


 LangString PAGE_Welcome_Text_net480 ${LANG_NORWEGIAN} "Denne installasjonsveiviseren hjelper deg med å installere Cairo.\r\n\r\nFør installasjon, sørg for at .NET Framework 4.8 eller nyere er installert, og at alle kjørende Cairo-prosesser er avsluttet.\r\n\r\nKlikk Neste for å fortsette."
LangString PAGE_Welcome_Text ${LANG_NORWEGIAN} "Denne installasjonsveiviseren hjelper deg med å installere Cairo.\r\n\r\nFør installasjon, sørg for at alle kjørende Cairo-prosesser er avsluttet.\r\n\r\nKlikk Neste for å fortsette."
LangString PAGE_Finish_RunText ${LANG_NORWEGIAN} "Start Cairo Desktop-miljø"
LangString PAGE_UnDir_TopText ${LANG_NORWEGIAN} "Vennligst sørg for at Cairo er avsluttet før du avinstallerer, for å sikre at alle filer fjernes. Alle filer i katalogen nedenfor vil bli slettet."
LangString DLOG_RunningText ${LANG_NORWEGIAN} "Cairo kjører for øyeblikket. Lukk Cairo fra Cairo-menyen og kjør installasjonsprogrammet igjen."
LangString DLOG_RunningText2 ${LANG_NORWEGIAN} "Cairo kjører for øyeblikket. Lukk Cairo fra Cairo-menyen."
LangString DLOG_DotNetText ${LANG_NORWEGIAN} "Cairo krever Microsoft .NET Framework 4.8 eller nyere. Installer dette fra Microsofts nettside og prøv igjen."
LangString SECT_cairo ${LANG_NORWEGIAN} "Cairo Desktop (påkrevd)"

;--------------------------------
; Uninstaller

Section "Uninstall"

  System::Call 'kernel32::OpenMutex(i 0x100000, i 0, t "CairoShell")p.R1'
  IntPtrCmp $R1 0 notRunning
    System::Call 'kernel32::CloseHandle(p $R1)'
    MessageBox MB_OK|MB_ICONEXCLAMATION "$(DLOG_RunningText)" /SD IDOK
    Quit

  notRunning:

  ; Remove the Shell Launcher configuration while the scripts are still here. Otherwise the next sign-in points
  ; Shell Launcher at a missing exe: a black screen and a restart loop.
  ReadRegStr $0 HKLM "Software\CairoShell" "StartMode"
  ReadRegDWORD $1 HKCU "Software\CairoShell" "ShellLauncher"
  ${If} $0 == "Shell"
  ${OrIf} $1 == 1
    ${If} ${FileExists} "$INSTDIR\Setup\Disable-CairoShellLauncher.ps1"
      DetailPrint "Removing the Shell Launcher configuration"
      nsExec::ExecToLog '"$PowerShell" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Setup\Disable-CairoShellLauncher.ps1" -KeepStartMode -NoSignOutPrompt'
      Pop $0
    ${EndIf}
  ${EndIf}

  ; Remove registry keys
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CairoShell"
  DeleteRegKey HKLM SOFTWARE\CairoShell
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "CairoShell"
  DeleteRegValue HKCU "Software\Microsoft\Windows NT\CurrentVersion\Winlogon" "Shell"
  DeleteRegValue HKCU "Software\CairoShell" "ShellLauncher"
  DeleteRegValue HKCU "Software\CairoShell" "ShellLauncherAppliedAt"

  ; Remove files and uninstaller. Includes historical files
  ; If we allowed customizing the install dir, this would be bad
  ; (e.g. if someone installed into another existing directory)
  ; If we allow this in the future, need to write an uninstall log.
  RMDir /r "$INSTDIR"

SectionEnd

;--------------------------------
; Functions

Function LaunchCairo
  IfFileExists "$WINDIR\explorer.exe" 0 std_exec
    Exec '"$WINDIR\explorer.exe" "$INSTDIR\CairoDesktop.exe"' ; use the shell to launch as current user (otherwise notification area breaks)
    goto end_launch
  std_exec:
    Exec '$INSTDIR\CairoDesktop.exe /restart=true'
  end_launch:
FunctionEnd

; The finish page offers to sign out instead of starting Cairo when Shell Launcher will start it
Function FinishShow
  ; with the reboot flag set the page shows reboot choices instead of the run checkbox
  ${If} $StartMode == "Shell"
  ${AndIfNot} ${RebootFlag}
    ; Modern UI 1: the run checkbox is field 4 of the finish page's InstallOptions file
    !insertmacro MUI_INSTALLOPTIONS_READ $0 "ioSpecial.ini" "Field 4" "HWND"
    SendMessage $0 ${WM_SETTEXT} 0 "STR:$(PAGE_Finish_SignOutText)"
  ${EndIf}
FunctionEnd

Function FinishAction
  ${If} $StartMode == "Shell"
    Exec '"$SYSDIR\shutdown.exe" /l'
  ${Else}
    Call LaunchCairo
  ${EndIf}
FunctionEnd

Function .onInstSuccess
  ; starting Cairo in place of Explorer in this session isn't what Shell Launcher does; it starts at sign-in
  ${If} ${Silent}
  ${AndIf} $StartMode != "Shell"
    Call LaunchCairo
  ${EndIf}
FunctionEnd


Function AbortIfRunning
  DetailPrint "Checking if Cairo is currently running"
  StrCpy $R0 "0" ; current retry count

  IfSilent 0 +3
    StrCpy $R2 "10" ; 10 retries when running silent
    Goto check
  StrCpy $R2 "2" ; 2 retries when running normally

  check:
    System::Call 'kernel32::OpenMutex(i 0x100000, i 0, t "CairoShell")p.R1'
    IntPtrCmp $R1 0 notRunning running running

  retry:
    MessageBox MB_OK|MB_ICONEXCLAMATION "$(DLOG_RunningText2)" /SD IDOK
    IntOp $R0 $R0 + 1
    Sleep 1000
    Goto check

  running:
    System::Call 'kernel32::CloseHandle(p $R1)'
    IntCmp $R0 $R2 abort retry abort

  abort:
    MessageBox MB_OK|MB_ICONEXCLAMATION "$(DLOG_RunningText)"
    Quit

  notRunning:
FunctionEnd

; https://nsis.sourceforge.io/How_to_Detect_any_.NET_Framework
; Check .NET framework release version and quit if too old
Function AbortIfBadFramework
  DetailPrint "Checking currently installed .NET Framework version"
 
  ; Save the variables in case something else is using them
  Push $0
  Push $1
  Push $2
  Push $3
  Push $4
  Push $R1
  Push $R2
  Push $R3
  Push $R4
  Push $R5
  Push $R6
  Push $R7
  Push $R8
 
  ; Major
  StrCpy $R5 "0"
 
  ; Minor
  StrCpy $R6 "0"
 
  ; Build
  StrCpy $R7 "0"
 
  ; No Framework
  StrCpy $R8 "0.0.0"
 
  StrCpy $0 0
 
  loop:
 
  ; Get each sub key under "SOFTWARE\Microsoft\NET Framework Setup\NDP"
  EnumRegKey $1 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP" $0
  StrCmp $1 "" done ;jump to end if no more registry keys
  IntOp $0 $0 + 1
  StrCpy $2 $1 1 ;Cut off the first character
  StrCpy $3 $1 "" 1 ;Remainder of string
 
  ; Loop if first character is not a 'v'
  StrCmpS $2 "v" start_parse loop
 
  ; Parse the string
  start_parse:
  StrCpy $R1 ""
  StrCpy $R2 ""
  StrCpy $R3 ""
  StrCpy $R4 $3
 
  StrCpy $4 1
 
  parse:
  StrCmp $3 "" parse_done ; If string is empty, we are finished
  StrCpy $2 $3 1 ; Cut off the first character
  StrCpy $3 $3 "" 1 ; Remainder of string
  StrCmp $2 "." is_dot not_dot ; Move to next part if it's a dot
 
  is_dot:
  IntOp $4 $4 + 1 ; Move to the next section
  goto parse ; Carry on parsing
 
  not_dot:
  IntCmp $4 1 major_ver
  IntCmp $4 2 minor_ver
  IntCmp $4 3 build_ver
  IntCmp $4 4 parse_done
 
  major_ver:
  StrCpy $R1 $R1$2
  goto parse ; Carry on parsing
 
  minor_ver:
  StrCpy $R2 $R2$2
  goto parse ; Carry on parsing
 
  build_ver:
  StrCpy $R3 $R3$2
  goto parse ; Carry on parsing
 
  parse_done:
 
  IntCmp $R1 $R5 this_major_same loop this_major_more
  this_major_more:
  StrCpy $R5 $R1
  StrCpy $R6 $R2
  StrCpy $R7 $R3
  StrCpy $R8 $R4
 
  goto loop
 
  this_major_same:
  IntCmp $R2 $R6 this_minor_same loop this_minor_more
  this_minor_more:
  StrCpy $R6 $R2
  StrCpy $R7 $R3
  StrCpy $R8 $R4
  goto loop
 
  this_minor_same:
  IntCmp R3 $R7 loop loop this_build_more
  this_build_more:
  StrCpy $R7 $R3
  StrCpy $R8 $R4
  goto loop
 
  done:
 
  ReadRegDWORD $R9 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" Release
  IntCmp $R9 ${MIN_FRA_RELEASE} end wrong_framework end
 
  wrong_framework:
  MessageBox MB_OK|MB_ICONSTOP "$(DLOG_DotNetText)"
  Quit
 
  end:
 
  ; Pop the variables we pushed earlier
  Pop $R8
  Pop $R7
  Pop $R6
  Pop $R5
  Pop $R4
  Pop $R3
  Pop $R2
  Pop $R1
  Pop $4
  Pop $3
  Pop $2
  Pop $1
  Pop $0
 
FunctionEnd
