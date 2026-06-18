#ifndef MyAppVersion
  #define MyAppVersion "0.7.4"
#endif
#ifndef SourceExe
  #define SourceExe "..\artifacts\publish\windows\PhoneTransfer.exe"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{7A72FCF2-27D1-47DA-8EB7-E804F9A5DFA9}
AppName=Phone Transfer
AppVersion={#MyAppVersion}
AppVerName=Phone Transfer {#MyAppVersion}
AppPublisher=Alwin Thomas
AppPublisherURL=https://github.com/alwinuae/phone-transfer
AppSupportURL=https://github.com/alwinuae/phone-transfer/issues
AppUpdatesURL=https://github.com/alwinuae/phone-transfer/releases
DefaultDirName={localappdata}\Programs\Phone Transfer
DefaultGroupName=Phone Transfer
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Phone-Transfer-Windows-Setup-v{#MyAppVersion}
SetupIconFile=..\desktop\PhoneFolder.Desktop\Assets\PhoneTransfer.ico
Uninstallable=yes
CreateUninstallRegKey=yes
UninstallDisplayName=Phone Transfer
UninstallDisplayIcon={app}\PhoneTransfer.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany=Alwin Thomas
VersionInfoDescription=Phone Transfer installer
VersionInfoProductName=Phone Transfer
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCopyright=Copyright (C) 2026 Alwin Thomas

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "PhoneTransfer.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\Phone Transfer"; Filename: "{app}\PhoneTransfer.exe"
Name: "{autodesktop}\Phone Transfer"; Filename: "{app}\PhoneTransfer.exe"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Windows\SendTo\Phone Transfer (Wi-Fi)"; Filename: "{app}\PhoneTransfer.exe"; Parameters: "--send-to-phone --mode wifi"
Name: "{userappdata}\Microsoft\Windows\SendTo\Phone Transfer (Online)"; Filename: "{app}\PhoneTransfer.exe"; Parameters: "--send-to-phone --mode online"

[Registry]
Root: HKCU; Subkey: "Software\Classes\*\shell\PhoneTransferSendWifi"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Send to Phone Transfer (Wi-Fi)"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\PhoneTransferSendWifi"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\PhoneTransfer.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\PhoneTransferSendWifi\command"; ValueType: string; ValueName: ""; ValueData: """{app}\PhoneTransfer.exe"" --send-to-phone --mode wifi ""%1"""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\PhoneTransferSendWifi"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Send to Phone Transfer (Wi-Fi)"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\PhoneTransferSendWifi"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\PhoneTransfer.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\PhoneTransferSendWifi\command"; ValueType: string; ValueName: ""; ValueData: """{app}\PhoneTransfer.exe"" --send-to-phone --mode wifi ""%1"""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Drive\shell\PhoneTransferSendWifi"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Send to Phone Transfer (Wi-Fi)"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Drive\shell\PhoneTransferSendWifi"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\PhoneTransfer.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Drive\shell\PhoneTransferSendWifi\command"; ValueType: string; ValueName: ""; ValueData: """{app}\PhoneTransfer.exe"" --send-to-phone --mode wifi ""%1"""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\PhoneTransferSendOnline"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Send to Phone Transfer (Online)"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\PhoneTransferSendOnline"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\PhoneTransfer.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\PhoneTransferSendOnline\command"; ValueType: string; ValueName: ""; ValueData: """{app}\PhoneTransfer.exe"" --send-to-phone --mode online ""%1"""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\PhoneTransferSendOnline"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Send to Phone Transfer (Online)"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\PhoneTransferSendOnline"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\PhoneTransfer.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\PhoneTransferSendOnline\command"; ValueType: string; ValueName: ""; ValueData: """{app}\PhoneTransfer.exe"" --send-to-phone --mode online ""%1"""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Drive\shell\PhoneTransferSendOnline"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Send to Phone Transfer (Online)"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Drive\shell\PhoneTransferSendOnline"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\PhoneTransfer.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Drive\shell\PhoneTransferSendOnline\command"; ValueType: string; ValueName: ""; ValueData: """{app}\PhoneTransfer.exe"" --send-to-phone --mode online ""%1"""; Flags: uninsdeletekey

[Run]
Filename: "{app}\PhoneTransfer.exe"; Description: "Launch Phone Transfer"; Flags: nowait postinstall skipifsilent
