#define MyAppName "NotifyIsland"
#define MyAppVersion "1.11.0"
#define MyAppPublisher "NotifyIsland"
#define MyAppURL "https://github.com/Leorik69/notifyisland-avalonia"
#define MyAppExeName "NotifyIsland.exe"
#ifndef PublishDir
  #define PublishDir "..\dist\win-x64"
#endif

[Setup]
AppId={{6E3F4A91-8B2D-4C11-9E5A-7F0C1D2A3B4E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=NotifyIsland-Setup-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\Assets\tray.ico
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start with Windows"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,notifyisland.settings.json,notifyisland.weather.json"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: autostart

[Code]
// Defensive kill: Inno Setup's CloseApplications=yes sometimes misses NotifyIsland
// when the running process is hung in a WinForms message loop. Force taskkill /F
// right before the installer copies files, so the new exe can replace the old one
// and the user is guaranteed a fresh process when the [Run] section launches.
function PrepareToInstall(var Refresh: Boolean): Boolean;
begin
  Result := True;
  if Exec('taskkill', '/F /IM {#MyAppExeName} /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    // intentional ignore of ResultCode — taskkill returns 128 if no process matched,
    // which is fine.
  ;
end;

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
