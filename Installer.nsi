!include "MUI.nsh"
!include "FileFunc.nsh"
!include "DotNetChecker.nsh" ; https://github.com/ReVolly/NsisDotNetChecker

!define APPNAME "GMB-View"

CRCCheck On
Unicode true
SetCompressor /solid lzma

Name "${APPNAME}"
Caption "${APPNAME} ${VERSION} Setup"
BrandingText " "

InstallDir "$PROGRAMFILES\GMB-View"

OutFile "GMB-View-${TAG}.exe"

!define MUI_ICON "GetReviews\logo.jpg"
!define MUI_UNICON "${NSISDIR}\Contrib\Graphics\Icons\win-uninstall.ico"

Var STARTMENU_FOLDER
!define MUI_STARTMENUPAGE_REGISTRY_ROOT "HKLM"
!define MUI_STARTMENUPAGE_REGISTRY_KEY "Software\GMB-View"
!define MUI_STARTMENUPAGE_REGISTRY_VALUENAME "Start Menu Folder"

!define MUI_ABORTWARNING

!define MUI_FINISHPAGE_RUN "$INSTDIR\GMB-View.exe"
!define MUI_FINISHPAGE_LINK "Visit the GMB Summary Viewer GitHub site for the latest news and support"
!define MUI_FINISHPAGE_LINK_LOCATION "https://github.com/MarkSummerville/GMB-Summary-Viewer"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_STARTMENU Application $STARTMENU_FOLDER
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "Install"
    SetOutPath "$INSTDIR"

    !insertmacro CheckNetFramework 472

    File "GMB-View\bin\Release\GetReviews.exe"
    File "GMB-View\bin\Release\GetReviews.exe.config"
    File "GMB-View\bin\Release\Newtonsoft.Json.dll"
    File "GMB-View\bin\Release\Google.Apis.Auth.dll"
    File "GMB-View\bin\Release\Google.Apis.Auth.PlatformServices.dll"
    File "GMB-View\bin\Release\Google.Apis.Core.dll"
    File "GMB-View\bin\Release\Google.Apis.dll"
    File "GMB-View\bin\Release\Google.Apis.PlatformServices.dll"
    
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    !insertmacro MUI_STARTMENU_WRITE_BEGIN Application
    CreateDirectory "$SMPROGRAMS\$STARTMENU_FOLDER"
    CreateShortCut "$SMPROGRAMS\$STARTMENU_FOLDER\${APPNAME}.lnk" "$INSTDIR\GetReviews.exe"
    CreateShortCut "$SMPROGRAMS\$STARTMENU_FOLDER\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
    !insertmacro MUI_STARTMENU_WRITE_END

    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\TVRename" "DisplayName" "${APPNAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\TVRename" "DisplayIcon" "$INSTDIR\GMB-View.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\TVRename" "DisplayVersion" "${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\TVRename" "Publisher" "${APPNAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\TVRename" "UninstallString" "$INSTDIR\Uninstall.exe"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\TVRename" "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\TVRename" "NoRepair" 1
    
    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\GMB-View" "EstimatedSize" "$0"

SectionEnd

Section "Uninstall"
    Delete "$INSTDIR\GetReviews.exe"
    Delete "$INSTDIR\GetReviews.exe.config"
    Delete "$INSTDIR\Newtonsoft.Json.dll"
    
    Delete "$INSTDIR\Google.Apis.Auth.dll"
    Delete "$INSTDIR\Google.Apis.Auth.PlatformServices.dll"
    Delete "$INSTDIR\Google.Apis.Core.dll"
    Delete "$INSTDIR\Google.Apis.dll"
    Delete "$INSTDIR\Google.Apis.PlatformServices.dll"

    RmDir "$INSTDIR"

    !insertmacro MUI_STARTMENU_GETFOLDER Application $STARTMENU_FOLDER
    Delete "$SMPROGRAMS\$STARTMENU_FOLDER\${APPNAME}.lnk"
    Delete "$SMPROGRAMS\$STARTMENU_FOLDER\${APPNAME} (Recover).lnk"
    Delete "$SMPROGRAMS\$STARTMENU_FOLDER\Uninstall.lnk"
    RmDir "$SMPROGRAMS\$STARTMENU_FOLDER"

    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\GMB-View"

    MessageBox MB_YESNO|MB_ICONEXCLAMATION "Do you wish to remove your ${APPNAME} settings as well?" IDNO done
    ReadRegStr $R0 HKCU "Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders" "AppData"
    RmDir /r "$R0\GMB-View"

done:

SectionEnd
