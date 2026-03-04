; CopilotRemap Inno Setup installer script

#define MyAppName "CopilotRemap"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Zorrobyte"
#define MyAppURL "https://github.com/Zorrobyte/CopilotRemap"
#define MyAppExeName "CopilotRemap.exe"

[Setup]
AppId={{E8A3F2B1-7C4D-4E5F-9A2B-1D3C6E8F0A2B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=CopilotRemap-Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
CloseApplicationsFilter=*.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Run at Windows startup"; GroupDescription: "Additional options:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch CopilotRemap"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up the startup shortcut the app may have created via its own toggle
Type: files; Name: "{userstartup}\CopilotRemap.lnk"
; Clean up config directory
Type: filesandordirs; Name: "{userappdata}\CopilotRemap"

[Code]
// Kill the running process before uninstall/upgrade
procedure TaskKill(FileName: string);
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM ' + FileName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    TaskKill('{#MyAppExeName}');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    TaskKill('{#MyAppExeName}');
end;
