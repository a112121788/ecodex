#define MyAppName "ECodex"
#define MyAppPublisher "ECodex"
#define MyAppExeName "ecodex-app.exe"
#define MyCliExeName "ecodex.exe"
#define MyAppUserModelID "ECodex.App"
#define MyAppVersion GetEnv("ECODEX_VERSION")
#if MyAppVersion == ""
#define MyAppVersion "1.0.2"
#endif

[Setup]
AppId={{5F31F460-32C4-4B16-BB0A-3B74E5D7E0A1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
SetupIconFile=..\assets\app-icon.ico
DefaultDirName={localappdata}\Programs\ECodex
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\publish\inno
OutputBaseFilename=ecodex-setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ShowLanguageDialog=no
SetupLogging=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: ".\Languages\ChineseSimplified.isl"

[CustomMessages]
chinesesimplified.CreateDesktopShortcut=创建桌面快捷方式
chinesesimplified.AdditionalShortcuts=附加快捷方式
chinesesimplified.LaunchECodex=启动 ECodex

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopShortcut}"; GroupDescription: "{cm:AdditionalShortcuts}"; Flags: unchecked

[InstallDelete]
Type: filesandordirs; Name: "{app}\cli"
Type: files; Name: "{app}\ecodex.deps.json"
Type: files; Name: "{app}\ecodex.dll"
Type: files; Name: "{app}\ecodex.exe"
Type: files; Name: "{app}\ecodex.pdb"
Type: files; Name: "{app}\ecodex.runtimeconfig.json"

[Files]
Source: "..\publish\ecodex-win-x64-sc\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\ecodex-cli\*"; DestDir: "{app}\cli"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ECodex"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; AppUserModelID: "{#MyAppUserModelID}"
Name: "{group}\ECodex CLI"; Filename: "{app}\cli\{#MyCliExeName}"; WorkingDir: "{app}\cli"
Name: "{autodesktop}\ECodex"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; AppUserModelID: "{#MyAppUserModelID}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchECodex}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
