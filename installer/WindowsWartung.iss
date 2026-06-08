; Inno-Setup-Skript fuer Windows-Wartung
; Build:  ISCC.exe /DMyAppVersion=4.6 installer\WindowsWartung.iss

#define MyAppName "Windows-Wartung"
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppExe "WindowsWartung.exe"
#define MyAppPublisher "Jonas"

[Setup]
AppId={{4D9A7C2E-3B1F-4E8A-9C6D-1A2B3C4D5E6F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\dist
OutputBaseFilename=WindowsWartung-Setup
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\ui\*"; DestDir: "{app}\ui"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
; shellexec statt CreateProcess -> loest die UAC-Abfrage korrekt aus (sonst Fehler 740 bei requireAdministrator)
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent shellexec

[Code]
// Durchsuchen-Dialog mit "Neuen Ordner anlegen"-Button
procedure DirBrowseClick(Sender: TObject);
var
  Dir: String;
begin
  Dir := WizardForm.DirEdit.Text;
  if BrowseForFolder('Wählen Sie den Zielordner (mit „Neuen Ordner erstellen"):', Dir, True) then
    WizardForm.DirEdit.Text := Dir;
end;

procedure InitializeWizard;
begin
  WizardForm.DirBrowseButton.OnClick := @DirBrowseClick;
end;
