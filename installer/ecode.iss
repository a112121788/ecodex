#define MyAppName "ECode"
#define MyAppPublisher "ECode"
#define MyAppExeName "ecode-app.exe"
#define MyCliExeName "ecode.exe"
#define MyAppVersion GetEnv("ECODE_VERSION")
#if MyAppVersion == ""
#define MyAppVersion "1.0.0"
#endif

[Setup]
AppId={{5F31F460-32C4-4B16-BB0A-3B74E5D7E0A1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
SetupIconFile=..\assets\app-icon.ico
DefaultDirName={localappdata}\Programs\ECode
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\publish\inno
OutputBaseFilename=ecode-setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[InstallDelete]
Type: filesandordirs; Name: "{app}\cli"
Type: files; Name: "{app}\ecode.deps.json"
Type: files; Name: "{app}\ecode.dll"
Type: files; Name: "{app}\ecode.exe"
Type: files; Name: "{app}\ecode.pdb"
Type: files; Name: "{app}\ecode.runtimeconfig.json"

[Files]
Source: "..\publish\ecode-win-x64-sc\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\ecode-cli\*"; DestDir: "{app}\cli"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ECode"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\ECode CLI"; Filename: "{app}\cli\{#MyCliExeName}"; WorkingDir: "{app}\cli"
Name: "{autodesktop}\ECode"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ECode"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
