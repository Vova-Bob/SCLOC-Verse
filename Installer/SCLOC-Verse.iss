#define MyAppName "SCLOC-Verse"
#define MyAppVersion GetFileVersion("..\SCLOCVerse\bin\Release\net9.0-windows\win-x64\publish\SCLOCVerse.exe")
#define MyAppPublisher "VALDEUS"
#define MyAppExeName "SCLOCVerse.exe"
#define MyAppIcoName "app_icon.ico"
#define PublishDir "..\SCLOCVerse\bin\Release\net9.0-windows\win-x64\publish"

[Setup]
AppId=SCLOCVerse
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputBaseFilename=SCLOC-Verse_Setup
SetupIconFile=..\SCLOCVerse\{#MyAppIcoName}
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
; Іконка застосунку вбудована як WPF-ресурс у збірку (CopyToOutputDirectory=Never),
; тому у publish-вивід вона не потрапляє. Копіюємо її з дерева сирців напряму,
; щоб ярлики Start Menu / Desktop та UninstallDisplayIcon мали валідний IconFilename.
Source: "..\SCLOCVerse\{#MyAppIcoName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppIcoName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppIcoName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
