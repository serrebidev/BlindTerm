#define AppName "BlindTerm"
#define AppPublisher "SerrebiProjects"
#define AppExeName "BlindTerm.App.exe"
#ifndef AppVersion
  #define AppVersion "0.1.5"
#endif

[Setup]
AppId={{B3C6A1C0-3BA0-49D9-A98D-100000000001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\BlindTerm
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=BlindTerm-Setup-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern

[Files]
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Dirs]
Name: "{app}"

[InstallDelete]
Type: files; Name: "{app}\*.old"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Hand the default terminal back to Windows before the executable it points at goes away.
; A stale registration is not fatal -- the console host takes the session back on its own --
; but leaving one behind means every console pays for a failed activation first.
Filename: "{app}\{#AppExeName}"; Parameters: "--reset-default-terminal"; Flags: runhidden; RunOnceId: "ResetDefaultTerminal"
