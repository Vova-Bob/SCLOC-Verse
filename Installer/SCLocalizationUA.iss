#define MyAppName "SCLocalizationUA"
#define MyAppVersion GetFileVersion("..\StarCitizenUA\bin\Release\net9.0-windows\win-x64\publish\StarCitizenUA.exe")
#define MyAppPublisher "Vova-Bob"
#define MyAppExeName "StarCitizenUA.exe"
#define MyAppIcoName "app_icon.ico"
#define PublishDir "..\StarCitizenUA\bin\Release\net9.0-windows\win-x64\publish"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputBaseFilename=SCLocalizationUA_Setup
SetupIconFile=..\StarCitizenUA\{#MyAppIcoName}
UninstallDisplayIcon={app}\{#MyAppIcoName}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
PrivilegesRequired=admin
WizardStyle=modern
DisableWelcomePage=yes
DisableProgramGroupPage=yes
Uninstallable=yes
CreateUninstallRegKey=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppIcoName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppIcoName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
