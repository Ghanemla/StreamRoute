#define MyAppName "StreamRoute"
#define MyAppVersion "0.1.5"
#define MyAppPublisher "StreamRoute"
#define MyAppExeName "StreamRoute.exe"

[Setup]
AppId={{CB343183-FE0E-4B0A-9EE1-71F593BFC10A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=StreamRoute-Setup
SetupIconFile=..\src\StreamRoute\Assets\StreamRoute.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=0.1.5.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription=Per-application stream audio routing for Windows

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\build\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD_PARTY_NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\StreamRoute"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\StreamRoute"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch StreamRoute"; Flags: nowait postinstall skipifsilent
