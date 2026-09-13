; Inno-Setup-Skript fuer Windows-Wartung (dunkles, gebrandetes Theme)
; Build:  ISCC.exe /DMyAppVersion=5.5 installer\WindowsWartung.iss

#define MyAppName "Windows-Wartung"
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppExe "WindowsWartung.exe"
#define MyAppPublisher "Jonas"

; x64compatible gibt es erst seit Inno Setup 6.3 (Mai 2024). Eine aeltere Fassung kennt den
; Bezeichner nicht und braeche mit einer unklaren Meldung ab; die CI holt per choco immer die
; neueste Fassung, lokal ist Inno nicht installiert (installer\build-installer.ps1 findet jede 6.x).
#if Ver < EncodeVer(6,3,0)
  #error Inno Setup 6.3 oder neuer ist noetig (ArchitecturesAllowed=x64compatible)
#endif

[Setup]
AppId={{4D9A7C2E-3B1F-4E8A-9C6D-1A2B3C4D5E6F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
; x64compatible statt x64: "x64" heisst seit Inno 6.3 "x64os" und passt nur auf x64-Windows.
; Windows 11 auf ARM64 (Snapdragon) fuehrt die x64-EXE per Emulation aus, das ZIP mit derselben
; EXE laeuft dort - der Installer verweigerte aber vor jeder Meldung (13.09.2026).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
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

[Dirs]
; Laufzeitdaten liegen seit 8.1 maschinenweit unter %ProgramData%\WindowsWartung (Protokolle,
; Verlauf, Zeitplan, Sicherungen, app.log). Rechte-Modell B: die Oberflaeche laeuft OHNE
; Administratorrechte, der Helfer und die geplante Wartung laufen erhoeht. Was der erhoehte
; Helfer anlegt, gehoert der Administratorengruppe; ohne diese Regel koennte der nicht
; erhoehte Host den Verlauf nicht mehr tauschen (File.Replace braucht Loeschrecht) und
; das Protokoll nicht fortschreiben. users-modify gibt BUILTIN\Users Aendern, vererbt auf
; Unterordner und Dateien; der Helfer setzt dasselbe bei jedem Start nach (Ablage.RechteSichern),
; der Installer ist nur der erste, der es tut. Beim Deinstallieren bleibt der Ordner stehen,
; solange Daten darin liegen (Inno-Standard: nur ein leerer Ordner wird entfernt).
Name: "{commonappdata}\WindowsWartung"; Permissions: users-modify

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
; postinstall startet standardmaessig als der urspruengliche (nicht erhoehte) Nutzer
; (runasoriginaluser). Seit 8.1 laeuft die App asInvoker, also ohne UAC-Dialog beim Start;
; shellexec bleibt, damit der Start ueber die Shell des Nutzers laeuft wie ein Doppelklick.
; Bis 8.0 (requireAdministrator) verhinderte shellexec den Fehler 740 bei CreateProcess.
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
    else if C is TRichEditViewer then
    begin
      TRichEditViewer(C).Color := clSurf;
      TRichEditViewer(C).Font.Color := clTxt;
    end
    else if C is TBitmapImage then TBitmapImage(C).BackColor := clBg
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
