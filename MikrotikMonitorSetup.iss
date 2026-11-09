#define MyAppName "AIMonitor+"
#define MyAppVersion "2.0.1"
#define MyAppPublisher "Antmanza Labs"
#define MyAppURL "https://www.antmanza.co.za"
#define MyAppExeName "SimpleMikrotikMonitor.exe"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
AppId={{A1B2C3D4-E5F6-4A5B-9C8D-7E6F5A4B3C2D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
; Users accept the terms (which include the MIT licence) during install
LicenseFile=TERMS.md
OutputBaseFilename=AIMonitorPlusSetup-v{#MyAppVersion}
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
; Use the logo for the installer
SetupIconFile=logo.ico
; Tell Windows to refresh cached icons after install, so an upgrade over v1 shows the new logo
ChangesAssociations=yes

[InstallDelete]
; v1 shipped a second copy of the app in {app}\win-x64 (old icon, old code). Remove it on upgrade.
Type: filesandordirs; Name: "{app}\win-x64"

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Settings live in %APPDATA%\AIMonitorPlus (encrypted). Never ship a settings file or logs next to the exe.
Source: "publish\win-x64\*"; DestDir: "{app}"; Excludes: "appsettings.json,*.pdb,logs\*,router_logs\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "TERMS.md"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion

[Icons]
; aimonitor.ico (from the publish folder) is a new path, so the v1 logo cached for logo.ico can't come back
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\aimonitor.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\aimonitor.ico"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Applications\{#MyAppExeName}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\aimonitor.ico"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// v1 left a read-only logo.ico in the program folder. Clear the flag and remove it so only aimonitor.ico remains.
procedure CurStepChanged(CurStep: TSetupStep);
var
  OldLogo: String;
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    OldLogo := ExpandConstant('{app}\logo.ico');
    if FileExists(OldLogo) then
    begin
      Exec(ExpandConstant('{sys}\attrib.exe'), '-R "' + OldLogo + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      DeleteFile(OldLogo);
    end;
  end;
end;
