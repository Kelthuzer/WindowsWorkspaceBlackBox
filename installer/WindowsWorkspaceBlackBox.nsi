Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"

!define APP "Windows Workspace BlackBox"
!define VERSION "0.1.0"
!define EXE "WindowsWorkspaceBlackBox.exe"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\WindowsWorkspaceBlackBox"
!ifndef SourceExe
  !define SourceExe "..\artifacts\publish\${EXE}"
!endif
!ifndef OutputExe
  !define OutputExe "..\artifacts\installer\WindowsWorkspaceBlackBox-Setup-${VERSION}-x64.exe"
!endif
!ifndef IconFile
  !define IconFile "Assets\wwbb.ico"
!endif
Name "${APP}"
OutFile "${OutputExe}"
Icon "${IconFile}"
InstallDir "$PROGRAMFILES64\WindowsWorkspaceBlackBox"
InstallDirRegKey HKLM "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetCompressorDictSize 32
VIProductVersion "0.1.0.0"
VIAddVersionKey /LANG=1033 "ProductName" "${APP}"
VIAddVersionKey /LANG=1033 "FileDescription" "Workspace recovery application installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Kelthuzer"
!define MUI_ABORTWARNING
!define MUI_ICON "${IconFile}"
!define MUI_UNICON "${IconFile}"
!define MUI_FINISHPAGE_TEXT "Установка завершена. Запустите Windows Workspace BlackBox из меню Пуск. При следующем входе в Windows программа запустится автоматически."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"
!insertmacro MUI_LANGUAGE "English"

LangString DESKTOP_SECTION ${LANG_RUSSIAN} "Ярлык на рабочем столе"
LangString DESKTOP_SECTION ${LANG_ENGLISH} "Desktop shortcut"
LangString DESKTOP_DESCRIPTION ${LANG_RUSSIAN} "Добавить ярлык Windows Workspace BlackBox на рабочий стол."
LangString DESKTOP_DESCRIPTION ${LANG_ENGLISH} "Add a Windows Workspace BlackBox shortcut to the desktop."

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Требуется 64-битная Windows."
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext all
FunctionEnd

Section "Windows Workspace BlackBox" Main
  SetOutPath "$INSTDIR"
  ; Files in use produce a retry/cancel prompt; the installer never kills the user's workspace.
  File "${SourceExe}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\${APP}"
  CreateShortcut "$SMPROGRAMS\${APP}\${APP}.lnk" "$INSTDIR\${EXE}"
  CreateShortcut "$SMPROGRAMS\${APP}\Удалить.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "WindowsWorkspaceBlackBox" '$\"$INSTDIR\${EXE}$\" --startup'
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "${APP}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "Kelthuzer"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section /o "$(DESKTOP_SECTION)" DesktopShortcut
  CreateShortcut "$DESKTOP\${APP}.lnk" "$INSTDIR\${EXE}"
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${DesktopShortcut} "$(DESKTOP_DESCRIPTION)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Function un.onInit
  SetRegView 64
  SetShellVarContext all
FunctionEnd

Section "Uninstall"
  ; Ask to close the tray instead of forcibly terminating a capture or restore.
  retry_delete:
  ClearErrors
  Delete "$INSTDIR\${EXE}"
  ${If} ${Errors}
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Закройте Windows Workspace BlackBox через значок в трее, затем нажмите Повторить." IDRETRY retry_delete
    Abort
  ${EndIf}
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "WindowsWorkspaceBlackBox"
  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  Delete "$SMPROGRAMS\${APP}\${APP}.lnk"
  Delete "$SMPROGRAMS\${APP}\Удалить.lnk"
  RMDir "$SMPROGRAMS\${APP}"
  Delete "$DESKTOP\${APP}.lnk"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  ; LOCALAPPDATA snapshots/settings are intentionally preserved.
SectionEnd
