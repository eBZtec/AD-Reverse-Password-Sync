;--------------------------------
; NSIS Script for AD-midPoint Reverse Sync Installer
;--------------------------------
!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"

; ----- App metadata -----
!define PRODUCT_VERSION "1.0.4"
!define APP_NAME "AD-midPoint Reverse Sync"
!define COMPANY_NAME "eBZ Tecnologia"
!define INSTALL_DIR "$PROGRAMFILES32\${COMPANY_NAME}\${APP_NAME}\src"
!define DLL_NAME "newEbzPass.dll"
!define SYSDIR "$WINDIR\Sysnative"
!define SERVICE_NAME "AD-midPoint-Sync-Service"
!define SERVICE_EXE "MidpointSyncService.exe"
RequestExecutionLevel admin
InstallDir "${INSTALL_DIR}"
BrandingText " ${COMPANY_NAME}"
Name "${APP_NAME}-${PRODUCT_VERSION}"
OutFile "AD-ReverseSync-${PRODUCT_VERSION}.exe"

; icons / UI tweaks
!define MUI_ICON "AD-sync.ico"
!define MUI_UNICON "ad-sync-uninst.ico"
!define MUI_ABORTWARNING


; ----- Variables -----
Var URL
Var USERNAME
Var PASSWORD
Var ATTMIDPOINT
Var hUrl
Var hUser
Var hPass
Var hAttM
; =====================================
; Custom Page (URL, Name, Password)
; =====================================
Function ConfigPage_Create
  !insertmacro MUI_HEADER_TEXT "Connection settings" "Enter the server URL, midPoint attribute, user and password."
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  ; Title/description
  ${NSD_CreateLabel} 0 0 100% 12u "Enter connection settings for ${APP_NAME}:"

  ; URL
  ${NSD_CreateLabel} 0 18u 40% 12u "midPoint URL:"
  Pop $0
  ${NSD_CreateText} 45% 16u 55% 12u "$URL"
  Pop $hUrl

  ; midPoint Attribute
  ${NSD_CreateLabel} 0 36u 40% 12u "midPoint Attribute:"
  Pop $0
  ${NSD_CreateText} 45% 34u 55% 12u "$ATTMIDPOINT"
  Pop $hAttM

  ; Name
  ${NSD_CreateLabel} 0 54u 40% 12u "User name:"
  Pop $0
  ${NSD_CreateText} 45% 52u 55% 12u "$USERNAME"
  Pop $hUser

  ; Password (masked)
  ${NSD_CreateLabel} 0 72u 40% 12u "Password:"
  Pop $0
  ${NSD_CreatePassword} 45% 70u 55% 12u "$PASSWORD"
  Pop $hPass

  nsDialogs::Show
FunctionEnd

Function ConfigPage_Leave
  ${NSD_GetText} $hUrl  $URL
  ${NSD_GetText} $hAttM  $ATTMIDPOINT
  ${NSD_GetText} $hUser $USERNAME
  ${NSD_GetText} $hPass $PASSWORD

FunctionEnd

; =====================================
; Pages
; =====================================
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "LICENSE"
Page Custom ConfigPage_Create ConfigPage_Leave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "English"


;--------------------------------
; Install Section
;--------------------------------
Section "Install"

; ----------- DLL Log dir -----------
  CreateDirectory "C:\ProgramData\${COMPANY_NAME}"

; ----------- REGISTRY -----------
  SetRegView 64
  WriteRegStr HKLM "SOFTWARE\${COMPANY_NAME}\AD-midPoint Sync" "midpoint_url" "$URL"
  WriteRegDWORD HKLM "SOFTWARE\${COMPANY_NAME}\AD-midPoint Sync" "allow_all" 0
  WriteRegStr HKLM "SOFTWARE\${COMPANY_NAME}\AD-midPoint Sync" "midpoint_attribute" "$ATTMIDPOINT"
  WriteRegDWORD HKLM "SOFTWARE\${COMPANY_NAME}\AD-midPoint Sync" "admin_bypass" 0

; ----------- INSTALL FILES -----------
  ; Set install directory
  SetOutPath "${INSTALL_DIR}"

  ; Copy the project folder
  File /r "${__FILEDIR__}\MidpointSyncService\publish\*"
  File /r "${__FILEDIR__}\CredManager\publish\*"
  File /r "${__FILEDIR__}\RegistryEditor\publish\*"


; -----------      LSA REGISTRY EDITOR      -----------
  !define REG_EXE "$INSTDIR\RegistryEditor.exe"
  !define REG_PKG "newEbzPass"

  nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Create /F /TN "AddRegPkg" /SC ONCE /ST 23:59 /RU SYSTEM /RL HIGHEST /TR "\"${REG_EXE}\" add ${REG_PKG}"'
  nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Run /TN "AddRegPkg"'
  Sleep 3000
  nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Delete /TN "AddRegPkg" /F'

; ----------- CREDENTIALS (via CredManager) -----------

!define CRED_EXE "$INSTDIR\CredManager.exe"

nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Create /F /TN "CreateSysMidpointCred" /SC ONCE /ST 23:59 /RU SYSTEM /RL HIGHEST /TR "\"${CRED_EXE}\" add MidPointSync \"$USERNAME\" \"$PASSWORD\""'
nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Run /TN "CreateSysMidpointCred"'
Sleep 3000
nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Delete /TN "CreateSysMidpointCred" /F'


  ; Copy DLL to system32
  SetOutPath "${SYSDIR}"
  File "${__FILEDIR__}\PasswordFilter\newEbzPass.dll"

  ; Install the service
  nsExec::ExecToLog 'sc create "${SERVICE_NAME}" binPath= "$INSTDIR\${SERVICE_EXE}" start= auto'

  ; Start the service
  nsExec::ExecToLog 'sc start "${SERVICE_NAME}"'

  ; Add service description
  nsExec::ExecToLog 'sc description "${SERVICE_NAME}" "This service makes REST calls to the midpoint API to transmit password changes from AD."'
    
  ; Add service display name
  nsExec::ExecToLog 'sc config "${SERVICE_NAME}" DisplayName= "${APP_NAME}"'


  ; Create uninstaller
  ; Calculate the size of the installation directory
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2

  ; Write uninstall information with dynamic size
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "EstimatedSize" $0

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Write uninstall registry entries
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayIcon" "${__FILEDIR__}\AD-sync.ico"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "Publisher" "${COMPANY_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "NoRepair" 1

SectionEnd

;--------------------------------
; Uninstall Section
;--------------------------------
Section "Uninstall"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"

  ; Stop the service
    nsExec::ExecToLog 'sc stop ${SERVICE_NAME}'

    Sleep 30000

    ; Delete the service
    nsExec::ExecToLog 'sc delete ${SERVICE_NAME}'

    Sleep 5000


  ; Remove WinCred
  nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Create /F /TN "DeleteSysMidpointCred" /SC ONCE /ST 23:59 /RU SYSTEM /RL HIGHEST /TR "\"${CRED_EXE}\" delete MidPointSync"'
  nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Run /TN "DeleteSysMidpointCred"'
  Sleep 3000
  nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Delete /TN "DeleteSysMidpointCred" /F'


  ; Remove Package from LSA
  nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Create /F /TN "RemoveRegPkg" /SC ONCE /ST 23:59 /RU SYSTEM /RL HIGHEST /TR "\"${REG_EXE}\" remove ${REG_PKG}"'
  nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Run /TN "RemoveRegPkg"'
  Sleep 3000
  nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Delete /TN "RemoveRegPkg" /F'


  ; Remove Registry values
  SetRegView 64
  DeleteRegKey HKLM "SOFTWARE\${COMPANY_NAME}\AD-midPoint Sync"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"

  ; Remove installed files
  RMDir /r "${INSTALL_DIR}"

  ; Remove uninstaller
  Delete "$INSTDIR\Uninstall.exe"

SectionEnd
