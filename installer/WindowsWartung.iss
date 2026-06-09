; Inno-Setup-Skript fuer Windows-Wartung (dunkles, gebrandetes Theme)
; Build:  ISCC.exe /DMyAppVersion=5.5 installer\WindowsWartung.iss

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
WizardImageFile=..\assets\wizard.bmp
WizardSmallImageFile=..\assets\wizard-small.bmp
WizardImageStretch=no

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
const
  clBg   = $00140F0D;  // #0d0f14  Hintergrund
  clSurf = $00261E1B;  // #1b1e26  Eingabefelder/Listen
  clTxt  = $00F0EBE9;  // #e9ebf0  Text hell
  clDim  = $00B0A19A;  // #9aa1b0  Text gedaempft
  clAcc  = $00BFD42D;  // #2dd4bf  Akzent (Teal)

procedure DirBrowseClick(Sender: TObject);
var
  Dir: String;
begin
  Dir := WizardForm.DirEdit.Text;
  if BrowseForFolder('Wählen Sie den Zielordner (mit „Neuen Ordner erstellen"):', Dir, True) then
    WizardForm.DirEdit.Text := Dir;
end;

procedure Recolor(P: TWinControl);
var
  i: Integer;
  C: TControl;
begin
  for i := 0 to P.ControlCount - 1 do
  begin
    C := P.Controls[i];
    if C is TNewStaticText then TNewStaticText(C).Font.Color := clTxt
    else if C is TLabel then TLabel(C).Font.Color := clTxt
    else if C is TNewCheckListBox then
    begin
      TNewCheckListBox(C).Color := clSurf;
      TNewCheckListBox(C).Font.Color := clTxt;
    end
    else if C is TNewEdit then
    begin
      TNewEdit(C).Color := clSurf;
      TNewEdit(C).Font.Color := clTxt;
    end
    else if C is TNewMemo then
    begin
      TNewMemo(C).Color := clSurf;
      TNewMemo(C).Font.Color := clTxt;
    end
    else if C is TPanel then TPanel(C).Color := clBg
    else if C is TBevel then TBevel(C).Visible := False;
    if C is TWinControl then Recolor(TWinControl(C));
  end;
end;

procedure ApplyTheme;
begin
  WizardForm.Color := clBg;
  WizardForm.MainPanel.Color := clBg;
  WizardForm.WelcomePage.Color := clBg;
  WizardForm.InnerPage.Color := clBg;
  WizardForm.SelectDirPage.Color := clBg;
  WizardForm.SelectTasksPage.Color := clBg;
  WizardForm.ReadyPage.Color := clBg;
  WizardForm.InstallingPage.Color := clBg;
  WizardForm.FinishedPage.Color := clBg;
  WizardForm.Bevel.Visible := False;
  Recolor(WizardForm);
  WizardForm.PageNameLabel.Font.Color := clAcc;
  WizardForm.PageDescriptionLabel.Font.Color := clDim;
end;

procedure InitializeWizard;
begin
  WizardForm.DirBrowseButton.OnClick := @DirBrowseClick;
  ApplyTheme;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  ApplyTheme;
end;
