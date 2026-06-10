#ifndef MyAppVersion
  #define MyAppVersion "0.6.0"
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
AppPublisher=Phone Transfer
AppPublisherURL=https://github.com/alwinuae/phone-transfer
AppSupportURL=https://github.com/alwinuae/phone-transfer/issues
DefaultDirName={localappdata}\Programs\Phone Transfer
DefaultGroupName=Phone Transfer
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Phone-Transfer-Windows-Setup-v{#MyAppVersion}
SetupIconFile=..\desktop\PhoneFolder.Desktop\Assets\PhoneTransfer.ico
UninstallDisplayIcon={app}\PhoneTransfer.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoDescription=Phone Transfer installer
VersionInfoProductName=Phone Transfer

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "PhoneTransfer.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\Phone Transfer"; Filename: "{app}\PhoneTransfer.exe"
Name: "{autodesktop}\Phone Transfer"; Filename: "{app}\PhoneTransfer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PhoneTransfer.exe"; Description: "Launch Phone Transfer"; Flags: nowait postinstall skipifsilent
