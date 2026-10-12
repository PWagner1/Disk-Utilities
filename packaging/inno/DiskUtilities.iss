; Optional Inno Setup wrapper for the unpackaged suite.
; 1) Download DiskUtilities-win-x64.zip from a GitHub Release
; 2) Extract into packaging\inno\payload\
; 3) Compile this script with Inno Setup 6+

#define MyAppName "Disk Utilities"
#define MyAppVersion "0.0.0"
#define MyAppPublisher "Peter Wagner"
#define MyAppURL "https://github.com/PWagner1/Disk-Utilities"
#define MyAppExeName "SuiteLauncher.exe"

[Setup]
AppId={{A7C3E9F1-2B4D-4E8A-9C1F-DiskUtilitiesSuite}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\Disk Utilities
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir=..\..\artifacts
OutputBaseFilename=DiskUtilities-Setup-x64
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
