!include "MUI2.nsh"

!ifndef VERSION
    !define VERSION "0.1.0"
!endif

!ifndef NUMERIC_VERSION
    !define NUMERIC_VERSION "0.1.0.0"
!endif

!ifndef APP_BASEDIR
    !define APP_BASEDIR "..\release\app"
!endif

Name "VRCInventoryManager"
OutFile "..\release\VRCInventoryManager-v${VERSION}-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\VRCInventoryManager"
InstallDirRegKey HKCU "Software\VRCInventoryManager" ""
RequestExecutionLevel user
ShowInstDetails show
CRCCheck force
SetOverwrite on
Icon "..\src\VRCInventoryManager\Assets\AppIcon.ico"
UninstallIcon "..\src\VRCInventoryManager\Assets\AppIcon.ico"

VIProductVersion "${NUMERIC_VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "VRCInventoryManager"
VIAddVersionKey /LANG=1033 "FileDescription" "VRCInventoryManager Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright (c) 2026 RealWhyKnot"

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "Install"
    SetShellVarContext current

    SetOutPath "$INSTDIR"
    File /r "${APP_BASEDIR}\*.*"

    CreateDirectory "$SMPROGRAMS\VRCInventoryManager"
    CreateShortCut "$SMPROGRAMS\VRCInventoryManager\VRCInventoryManager.lnk" "$INSTDIR\VRCInventoryManager.exe"

    WriteUninstaller "$INSTDIR\Uninstall.exe"
    WriteRegStr HKCU "Software\VRCInventoryManager" "" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRCInventoryManager" "DisplayName" "VRCInventoryManager"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRCInventoryManager" "DisplayVersion" "${VERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRCInventoryManager" "Publisher" "RealWhyKnot"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRCInventoryManager" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRCInventoryManager" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRCInventoryManager" "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRCInventoryManager" "NoRepair" 1
SectionEnd

Section "Uninstall"
    SetShellVarContext current

    Delete "$SMPROGRAMS\VRCInventoryManager\VRCInventoryManager.lnk"
    RMDir "$SMPROGRAMS\VRCInventoryManager"

    Delete "$INSTDIR\Uninstall.exe"
    RMDir /r "$INSTDIR"

    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRCInventoryManager"
    DeleteRegKey HKCU "Software\VRCInventoryManager"
SectionEnd
