; Mazraa Windows installer (Inno Setup 6). Built by Unity: Mazraa > Release > Package (passes the defines below).
; Manual: "ISCC.exe /DMyAppVersion=0.9.0 /DSourceDir=..\release\Mazraa_0.9.0_Windows /DOutputDir=..\release Mazraa.iss"
#ifndef MyAppVersion
  #define MyAppVersion "0.9.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\release\Mazraa_" + MyAppVersion + "_Windows"
#endif
#ifndef OutputDir
  #define OutputDir "..\release"
#endif
#define MyAppName "Mazraa"
#define MyAppPublisher "Almadarek Games"
#define MyAppExeName "Mazraa.exe"

[Setup]
; Keep this AppId forever: it lets new versions update the installed game.
AppId={{6F1C2B7E-5A3D-4E8B-9C21-0D4F7A9B3E51}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppPublisher}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile={#SourceDir}\EULA.txt
OutputDir={#OutputDir}
OutputBaseFilename=Mazraa_Setup_{#MyAppVersion}
SetupIconFile=mazraa.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=4
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
