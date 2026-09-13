# Bauen, signieren, veröffentlichen, aktualisieren

Das Pendant zur `DEPLOYMENT.md` der Webprojekte, für ein Desktop-Werkzeug. Was hier steht,
ist der Weg, den jedes Release nimmt. Stand: v8.1.0 (Rechte-Modell B: Oberfläche ohne
Administratorrechte, Helfer mit Rechten nur auf Anforderung).

## Aufbau

```
kern/        Datenmodell (Systembild, Befund, Plan), Regeln, Entscheidungen, Protokoll, JSON
             hängt von nichts ab: kein WinForms, kein WMI, kein Prozessstart (Test wacht darüber)
kern/Regeln/ eine Datei je Bereich, reine Funktionen über dem Systembild, Schwellen mit Quelle
sammler/     füllt das Systembild im eigenen Prozess: WMI, Ereignisprotokoll, Registry, COM, P/Invoke
             startet keinen Prozess (Test wacht darüber); Admin-Quellen tragen nicht erhöht "zugriff" ein
helfer/      der erhöhte Teil, dieselbe EXE mit --helfer (kein WinForms, kein WebView2; Test wacht darüber):
             Pipe.cs (Named Pipe, Sicherheitsbeschreibung nur für Aufrufer-SID und SYSTEM),
             Nachricht.cs (Anfrage/Antwort als JSON-Zeilen), Katalog.cs (Kennung → Maßnahme mit Prüfung),
             Ausfuehrung.cs (Plan Schritt für Schritt, Protokoll), Werkzeuge.cs (Prozessstart ohne Shell,
             ein Job-Objekt je Schritt, Wachhund), Messung.cs (erhöhtes Systembild), Helfer.cs (Einstieg, drei Modi)
host/        Fenster, WebView2-Brücke, Hauptweg (CheckFlow), Suchläufe (ScanFlow), Update;
             Ausfuehrer.cs (IAusfuehrer: lokal im eigenen Prozess oder über die Pipe),
             HelferClient.cs (Start per runas, Wiederverwendung, --pipe-Abnahmeweg),
             Selbstpruefung.cs (eigene Signatur per WinVerifyTrust, ui\ gegen ui-hashes.txt)
src/         Werkzeugkasten (Katalog), Schritte.cs (Schrittbaukasten mit Parameterprüfung, von Host
             und Helfer benutzt), CommandRunner (baut Pläne), Verlauf, Protokoll der App, Signaturbindung
ui/          Oberfläche (HTML/CSS/JS)
tests/       run-tests.ps1 (alle Prüfungen), proben/ (Kernproben), aufzeichnungen/ (Testbilder)
tools/       csc.ps1 (Roslyn-Aufruf), bau-kern.ps1 (Kommandozeile), lauf-proben.ps1
installer/   Setup-Skript (Inno Setup), setzt die Rechte auf dem Datenordner
```

Der Host startet seit 8.1 kein Werkzeug **mit Rechten** mehr selbst: kein `dism.exe`, kein
`sfc.exe`, kein `powershell.exe` für Eingriffe (der Test greppt `host\*.cs`, `CommandRunner`
und `AutoRunner`; `Process.Start` dort nur für den runas-Start des Helfers, das Update und
das Öffnen von Ordnern). Ohne Rechte startet der Host weiterhin: `powershell.exe` zum Lesen
der App-Liste (`AppxCleaner.List`), `schtasks.exe` für den Selbststart-Schalter und die
Anzeige des Zeitplans, `powercfg.exe` für den Energieplan, `shutdown.exe` für „Wenn alles
fertig ist“ und `explorer.exe` zum Öffnen von Ordnern. Alles, was Rechte braucht, geht als
**Plan** (Kennungen plus geprüfte Parameter) an den Helfer; der baut die Schritte aus seinem
Katalog selbst und nimmt von außen nie einen Dateinamen mit Argumenten an. Im Helfer hängt
jeder gestartete Prozess an einem **Job-Objekt** (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`);
Wachhund und Abbruch beenden das Job-Objekt, nicht eine PID: das trifft auch Enkelprozesse
wie `DismHost.exe`, die nach dem Ende von DISM noch die Ausgabe halten, und nie einen fremden
Prozess mit wiederverwendeter PID. Startfehler (-1) und Zeitgrenze (-2) zählen immer als
Problem, auch bei Schritten mit `IgnoreExit`.

## Bauen

Voraussetzung ist nur das .NET SDK (zum Bauen). Zielrahmen bleibt .NET Framework 4.8, das in
Windows 10 und 11 ab Werk steckt; die fertige EXE braucht keine Laufzeit-Installation.

```powershell
.\build.ps1                   # bin\WindowsWartung.exe, unsigniert (Dev)
.\build.ps1 -Release          # dieselbe EXE; -Release ändert nur die Abschlusszeile
.\build.ps1 -Release -Sign    # signiert (cert\, Passwort aus WW_CERT_PASSWORD oder Abfrage)
.\tools\bau-kern.ps1          # bin\aufzeichnen.exe: Sammler + Regeln ohne Oberfläche, ohne Helfer
.\tests\run-tests.ps1         # alle Prüfungen, Rückgabewert 0 = grün
```

Drei Dinge, die der Bau seit 8.1 immer tut, unabhängig von `-Release`:

1. **Manifest `src\app.manifest` (`asInvoker`) wird immer eingebettet.** Dev und Release starten
   ohne UAC-Dialog; Administratorrechte holt sich erst der Helfer, wenn eine Maßnahme sie
   braucht. Es gibt keinen „Build ohne Manifest“ mehr, und `-Release` steuert nichts außer der
   Beschriftung `(Release)`/`(Dev)`. Signatur hängt allein an `-Sign`, `/optimize+` ist immer an.
2. **`ui-hashes.txt` wird als Ressource eingebettet.** Vor dem Übersetzen schreibt `build.ps1`
   die SHA-256-Summe jeder Datei unter `ui\` (rekursiv, ohne `shot_*.png`, Pfad mit `/`,
   sortiert) nach `%TEMP%\ui-hashes.txt` und hängt `/resource:<pfad>,ui-hashes.txt` an. Die
   Startprüfung liest die Liste aus der EXE, nie von der Platte.
3. **Alle Quellordner werden per Muster gebaut:** `host\`, `src\`, `helfer\`, `kern\`,
   `kern\Regeln\`, `sammler\`, `sammler\Quellen\`. Neue `.cs`-Dateien brauchen keinen Eintrag;
   nur `sammler\AufzeichnenCli.cs` (zweites `Main`) bleibt ausgeschlossen und gehört zu
   `bau-kern.ps1`.

Der eingebaute `csc.exe` kann nur C# 5 und bleibt außen vor; `tools\csc.ps1` sucht den
Roslyn-Compiler im SDK und die net48-Referenzassemblies.

## Prüfen

`tests\run-tests.ps1` läuft lokal und in der CI (Windows PowerShell 5.1). Dazu gehören:

- **Kernproben** (`tools\lauf-proben.ps1`): die Regeln gegen aufgezeichnete Systembilder in
  `tests\aufzeichnungen\gepflanzt-*.json` (114 handgeschriebene Bilder, über 3.800 Zusicherungen
  in acht Probenklassen), darunter die Fehlerfälle, die ein Reparaturwerkzeug falsch behandeln
  könnte (absichtlich deaktiviertes Gerät, SMART-Warnung, englisches System). Läuft ohne
  Rechte, ohne Fenster, ohne Windows-Zugriff. Die Klasse `DieserPcProben` prüft zusätzlich
  drei echte Aufzeichnungen des Entwicklungsrechners (`dieser-pc-*.json`); die bleiben lokal
  (Software-Inventar), in der CI meldet die Klasse sich als übersprungen.
- **Werkzeugkasten-Probe**: jeder PowerShell-Schritt des Katalogs wird ausgegeben und durch den
  PowerShell-Parser geschickt (ein doppeltes Anführungszeichen im `-Command`-Text beendet den
  Befehl; genau das hatte Aktion 6 bis 8.0.0).
- **Sammlerprobe**: der echte Sammler auf dem Bau-Rechner, redigiert, mit Prüfung, dass
  fehlende Rechte als Fehlereintrag erscheinen und nicht als leere Antwort.
- **Schichtregel** und **Prozessverbot** als Grep über `kern\` und `sammler\`; dazu wird der
  Sammler während der Sammlerprobe auf Kindprozesse beobachtet (er darf keinen haben).

Neu mit 8.1 die Gruppe **„v8.1: Helfer und Rechte“** (13 Prüfungen; die Zahl im Kopf des
Skripts, `$erwartet`, zählt alle Prüfungen und wird am Ende gegen die gelaufenen gehalten):

| Prüfung (Reihenfolge wie im Skript) | Was sie belegt |
| --- | --- |
| bin-Frische | `bin\WindowsWartung.exe` ist jünger als jede Datei unter `src\`, `host\`, `helfer\`, `kern\`, `sammler\`, `ui\` (ohne `shot_*.png`) und als `build.ps1`; sonst fahren die EXE-Proben einen alten Stand und bleiben grün, obwohl die Änderung nie übersetzt wurde (eigener Fehler, kein Hinweis) |
| Manifest | `src\app.manifest` trägt `level="asInvoker"`; `build.ps1` bettet es ohne `if ($Release)` ein; die Version im Manifest passt zur `AssemblyVersion` |
| Schichtregel helfer | kein `System.Windows.Forms`, kein `Microsoft.Web`, kein `CommandRunner`, kein `Shell` in `helfer\*.cs` |
| Host startet keine Werkzeuge | Grep über `host\*.cs`, `src\CommandRunner.cs`, `src\AutoRunner.cs`: kein `DISM.exe`, `sfc.exe`, `powershell.exe`, kein `Process.Start(` außer den benannten Ausnahmen (runas-Start in `HelferClient`, `Shell.cs`, WebView2-Link, Update-Batch) |
| Trockenlauf aller Kennungen | eine Plandatei mit jeder Kennung des Katalogs und gültigen Parametern → `--helfer --plan <datei> --trocken` endet mit Exit 0, je Kennung eine `schritt`-Zeile in `lauf-<planId>.jsonl`, währenddessen kein Kindprozess (Beobachtung mit gebundenem Handle); eine vorher angelegte Wächterdatei in `%TEMP%` ist danach noch da (die In-Process-Eingriffe von `speicher.aufraeumen` fassen im Trockenlauf nichts an), und die Folgenummer der Wiederherstellungspunkte hat sich nicht bewegt |
| Ablehnung | 13 Fälle → jeweils Exit 2 mit Protokollzeile `abgelehnt`, kein Prozess: Kennung `format.c`; `werkzeug` mit `id: "999"` und mit `sicherung: "ja"`; `netz.diagnose` mit `ziel: "a b"`; `wiederherstellungspunkt.anlegen` mit Sonderzeichen in `beschreibung`; `wiederherstellungspunkt.zurueck` mit `folge: "0"`; `wartung.auto` mit `schluessel: "format"`; `apps.entfernen` mit einem kritischen Paket; `treiber.sichern` mit `ordner` unter `%WINDIR%`; `zeitplan.anlegen` mit `modus: "x"`; `speicher.aufraeumen` mit `schluessel: "downloads"`; `registrierung.entfernen` mit `kennungen: "ab;c/d"`; leerer Plan |
| Plan-Id mit Pfadzeichen | `--plan` mit einer Id aus `..` und `\` ersetzt die Id, nichts landet außerhalb von `protokoll\` |
| Pipe-Probe | Helfer mit `--helfer --pipe ww-test-<guid> --sid <eigene SID>` (nicht erhöht erlaubt), Client über `NamedPipeClientStream`: `ping` → `ergebnis`, `unsinn` → `abgelehnt`, `ende` → Prozess endet binnen 5 s; erhöht (CI-Runner) zusätzlich `messen` → `systembildJson` mit `erhoeht: true` |
| Pipe-Zugriffsschutz | (a) `--sid S-1-1-0` (Gruppe „Jeder“, kein Konto) → Exit 7 binnen 10 s, keine Pipe unter `\\.\pipe\`; (b) `--sid` mit einer wohlgeformten fremden Konto-SID: der Verbindungsversuch des eigenen Kontos scheitert an der Pipe-Beschreibung (`UnauthorizedAccessException`), bevor der Helfer eine Zeile liest; nur wenn die Suite als SYSTEM läuft (in der Beschreibung enthalten), greift der zweite Riegel: erste Anfrage `abgelehnt`, Exit 4 |
| PipeSicherheit | `tests\HelferProbe.cs` ruft `Pipe.PipeSicherheit(sid)`: genau zwei Regeln (SID + SYSTEM), keine für Jeder, Benutzer oder Authentifizierte; dazu `Users`-Rechte, einmalige Übernahme, Lauf-Id als Dateiname |
| Startprüfung | `--selbstpruefung <datei>` → Exit 0; eine Kopie von `ui\app.js` um ein Byte geändert → Exit 6 (unsigniert) bzw. 5 (signiert); Exit 5 misst nur die signierte EXE, in der CI läuft der Test vor der Signatur (die signierte EXE misst der Release-Workflow selbst, siehe „Veröffentlichen“); eine fremde Datei `bin\ui\probe-fremd.js` ist nur ein Hinweis (Exit 0), ist nach der Probe nachweislich wieder weg, und `app.js` ist unverändert |
| Laufzeitordner | `Ablage.RechteSichern()` und die Übernahme alter Dateien gegen einen Temp-Ordner (kein Zugriff auf ProgramData im Test); Grep auf `UebernahmeAbschliessen` (die alte Datei wird auch dann auf `*.uebernommen` umbenannt, wenn die neue schon existiert); der Installer setzt `users-modify` |
| Kein RunJobs/Job | Grep `RunJobs` und `class Job` über `host\` und `src\` = 0: jeder Lauf mit Rechten ist ein Plan |

Kommandozeilen, die dabei benutzt werden (alle ohne Fenster, Ergebnis im Exit-Code und in
`logs\app.log`):

```powershell
.\bin\WindowsWartung.exe --helfer --plan C:\Temp\plan.json --trocken   # Exit = PlanErgebnis.Exit (0 ok, 1 Problem, 2 abgelehnt, 3 Ausnahme, 4 abgebrochen); 7 = Argumente
.\bin\WindowsWartung.exe --helfer --messen C:\Temp\bild.json           # erhöhte Messung, unredigiert (nur aus erhöhter Shell sinnvoll)
.\bin\WindowsWartung.exe --helfer --pipe ww-test-1 --sid S-1-5-21-…    # Pipe-Server für einen Client derselben SID
.\bin\WindowsWartung.exe --selbstpruefung C:\Temp\selbst.txt           # Signatur + ui-Liste, Exit 0/5/6; 3 = Prüfung abgebrochen oder Ausgabedatei nicht schreibbar (Grund in app.log)
.\bin\WindowsWartung.exe --aufzeichnen C:\Temp\bild.json               # redigiert (Rechnername, Nutzer, Seriennummern, MAC, private IP)
.\bin\WindowsWartung.exe --pruefen C:\Temp\bild.json                   # Befunde nach bild.json.befunde.txt
.\bin\aufzeichnen.exe --live                                           # Sammler + Regeln mit Konsolenausgabe
```

Die EXE ist eine Fensteranwendung: PowerShell wartet nicht von selbst, also
`Start-Process -Wait -PassThru` und `$null = $proc.Handle` direkt nach dem Start, sonst bleibt
`ExitCode` leer.

**Abnahmeweg ohne UAC-Dialog** (für die Oberfläche mit echtem Helfer, aus einer Sitzung, in
der ein Klick im UAC-Dialog nicht möglich ist): eine erhöhte Shell startet den Helfer, die
Oberfläche verbindet sich nicht erhöht mit ihm. Der Pipe-Name ist frei wählbar (Buchstaben,
Ziffern, `. - _`, 3 bis 100 Zeichen), die SID ist die des Kontos, unter dem beide laufen.

```powershell
# erhöhte Shell (Helfer lebt, bis "ende" kommt oder 10 min nichts kommt):
$sid = ([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
.\bin\WindowsWartung.exe --helfer --pipe ww-abnahme --sid $sid

# nicht erhöhte Shell (Oberfläche, Startzeile nennt die Abnahme-Pipe):
.\bin\WindowsWartung.exe --pipe ww-abnahme
```

Die Oberfläche verbindet sich beim ersten Auftrag, der Rechte braucht (10 s Wartezeit auf
die Pipe, dann `ping`), und nutzt den Helfer für jede Maßnahme, ohne einen eigenen zu
starten; die Karte „N Werte brauchen einmal Administratorrechte“ erscheint nach dem ersten
Prüfen wie sonst, „Mit Administratorrechten ergänzen“ verbindet dann ohne Dialog.
Beim Schließen des Fensters schickt sie `ende`, der Helfer endet: für einen zweiten Lauf
der Oberfläche den Helfer in der erhöhten Shell neu starten. Ein nicht erhöht gestarteter
Helfer wird angenommen, misst aber nur, was ohne Rechte geht (die Zählung bleibt ehrlich).

Was sich nur mit einem echten Klick im UAC-Dialog prüfen lässt (Start des Helfers aus der
Oberfläche, Ablehnung, die Ergänzung der Messwerte, die Tiefenprüfung mit DISM und SFC, die
Reparatur, Umstellung der Selbststart-Aufgabe), steht als Prüfliste für den Betreiber in
`docs\UEBERGABE.md`.

## Signieren

Programm und Installer werden mit `CN=Jonas (Windows-Wartung)` signiert (selbst ausgestellt,
DigiCert-Zeitstempel). Die Selbstaktualisierung nimmt nur Fassungen desselben Herausgebers an;
der Bau bricht ab, wenn der Name abweicht. PFX unter `cert\` (nie im Repo), Passwort in
`secure.md`. Repo-Secrets: `CODESIGN_PFX_BASE64` und `CODESIGN_PASSWORD`. Einzelheiten in
`SIGNING.md`.

**Herausgeber-Bindung = Name und öffentlicher Schlüssel.** `src\UpdateTrust.cs` vergleicht
seit 8.1 nicht nur den Zertifikatsnamen, sondern auch den öffentlichen Schlüssel des
Signaturzertifikats (`GetPublicKeyString`) mit dem der laufenden Fassung. Der Name allein
schützt bei einem selbst ausgestellten Zertifikat gegen niemanden: `tools\make-cert.ps1`
erzeugt ihn als Vorgabe, wer das Release austauschen kann, könnte ihn nachstellen. Folge für
den Betrieb: **ein neues Schlüsselpaar sperrt jede installierte Fassung vom Update aus** (sie
lehnt die neue Datei als „anderer Herausgeber“ ab, ein Wechsel geht dann nur über eine
Neuinstallation von Hand). Das Zertifikat wird darum nur mit demselben Schlüssel erneuert
(das Schlüsselpaar aus der vorhandenen PFX weiterverwenden, nie `make-cert.ps1` ohne
Parameter für ein Nachfolgezertifikat); die PFX-Datei ist damit das eigentliche Geheimnis,
nicht der Name.

Die Signatur hat seit 8.1 eine zweite Aufgabe: die **Startprüfung** (`host\Selbstpruefung.cs`).
Beim Start prüft die EXE ihre eigene Signatur per `WinVerifyTrust` und jede Datei unter `ui\`
gegen die eingebettete `ui-hashes.txt`. Eine signierte EXE mit ungültiger Signatur oder einer
veränderten oder fehlenden Oberflächendatei startet nicht („Die Programmdateien wurden
verändert. Bitte installieren Sie Windows-Wartung neu.“, Exit 5); das gilt auch für `--helfer`,
`--auto`, `--aufzeichnen` und `--pruefen`. Eine unsignierte EXE (Dev-Bau) schreibt bei
Abweichung nur eine Warnung ins Protokoll. Geprüft wird Unversehrtheit, nicht Kettenvertrauen:
ein nicht vertrauter Stamm (das selbst ausgestellte Zertifikat auf einem Kundenrechner), ein
abgelaufenes Zertifikat oder eine Richtlinie zählen als „signiert, intakt“; nur Digest- und
Signaturfehler zählen als ungültig. Zusätzliche Dateien unter `ui\` sind nur ein Hinweis im
Protokoll, weil Updater und Installer `ui\` nicht spiegeln. Bytes, die hinter die Signatur
angehängt werden, lassen `WinVerifyTrust` „unsigniert“ melden; die EXE gilt dann als Dev-Bau
und startet mit Warnung (Eigenschaft von Authenticode).

## Veröffentlichen

Der Release-Weg ist der annotierte Tag. **Kein `gh release create`.**

1. `CHANGELOG.md`: Abschnitt `## [X.Y.Z] - Datum` pflegen (wird wörtlich zur Release-Beschreibung).
2. `src\AssemblyInfo.cs`: **beide** Versionen anheben, `AssemblyVersion` und
   `AssemblyFileVersion` (der Workflow bricht ab, wenn Tag, FileVersion und AssemblyVersion
   nicht alle drei übereinstimmen; die App vergleicht beim Update die AssemblyVersion, die CI
   las bis 8.0 nur die FileVersion, ein Auseinanderlaufen ließe die App sich selbst als Update
   anbieten). Der Tag ist **dreiteilig** (`v8.1.0`, nie `v8.1`): ein zweiteiliger Tag käme
   durch eine Präfixprüfung, würde aber von der App als kleiner gewertet und nie angeboten;
   die CI lehnt ihn ab.
3. Committen, pushen, CI abwarten (jeder Push auf `main` baut und prüft, ohne Release).
4. Trockenlauf: `gh workflow run "Build and Release" --ref main` baut alles samt Tests, Signatur
   und Installer, legt aber kein Release an. `gh run watch --exit-status` bis zum Ende.
5. Tag setzen und pushen:
   ```powershell
   git tag -a v8.1.0 -F tagmsg.txt
   git push origin v8.1.0
   gh run watch --exit-status
   ```
6. Nach dem Lauf: Prüfsummen und Signatur gegen den echten Download vergleichen
   (`Get-AuthenticodeSignature`, `Get-FileHash`), `releases/latest` zeigt kein Entwurf.

Der Workflow legt das Release als Entwurf an, hängt ZIP, Installer und beide `.sha256` an und
schaltet erst dann sichtbar. Ein Update-Klick kann so nie ein halbes Release sehen.

Nach dem Signieren startet die CI die signierte EXE zweimal selbst (Schritt „Startpruefung
der signierten EXE“): `bin\WindowsWartung.exe --selbstpruefung $env:RUNNER_TEMP\selbstpruefung.txt`
muss mit Exit 0 enden und `uiStimmt=true`, `signiert=true`, `signaturGueltig=true` schreiben;
danach wird `bin\ui\app.js` um ein Byte verlängert, der zweite Lauf muss Exit 5 liefern
(unsigniert 6), und der SHA-256 von `app.js` muss danach wieder der alte sein, sonst wanderte
die veränderte Datei ins ZIP. Der Runner kennt den Stamm des Zertifikats nicht, das ist genau
der Kundenpfad (`CERT_E_UNTRUSTEDROOT`, zählt als intakt); die Tests davor laufen immer
unsigniert und messen diesen Zweig nie. Eine Änderung an `IstKettenfehler`, an `sign.ps1`
oder ein Zertifikatswechsel fällt so in der CI auf, nicht erst bei jedem Kunden mit Exit 5.

Der Installer erlaubt `x64compatible` (Inno Setup 6.3+): Windows 11 auf ARM64 nimmt das Setup
an und führt die x64-EXE über die x64-Emulation aus; `ArchitecturesAllowed=x64` hieße dort
„nur x64-Windows“ und lehnte die Installation ab. Ob WebView2 unter dieser Emulation
vollständig läuft, ist **ungetestet** (kein ARM64-Gerät vorhanden); das ZIP startet dort
dieselbe EXE.

## Aktualisieren

Die App prüft beim Start und stündlich `releases/latest`. Ein Update wird nur installiert,
wenn Prüfsumme (`<Datei>.sha256`, über den Dateinamen zugeordnet) und Herausgeber (Name und
öffentlicher Schlüssel des Zertifikats, siehe „Signieren“) stimmen.
Wer per Installer installiert hat, wird per Installer aktualisiert; sonst wird das ZIP getauscht,
mit Sicherung der alten Fassung und Rückweg. Ein misslungenes Update meldet sich.

Seit 8.1 läuft die App nicht erhöht, das Update braucht aber Schreibrecht unter
`Program Files`: beide Wege starten darum mit `Verb = runas` und zeigen einen UAC-Dialog. Der
Installer-Weg nennt darin den Namen des Programms; der ZIP-Weg startet die Austausch-Batch
über `cmd.exe`, der Dialog nennt also den Befehlsprozessor. Vor dem Austausch beendet die App
einen laufenden Helfer (dieselbe EXE), sonst kann `robocopy` sie nicht ersetzen; danach startet
die Batch die neue Fassung über `explorer.exe`, damit sie nicht erhöht läuft. Ein sauberer
Weg über den Helfer (`update.anwenden`) ist für 8.2 vorgesehen.

Rückruf eines kaputten Releases: `gh release edit vX.Y.Z --prerelease`, Assets behalten,
Korrektur als neue Patch-Version. Der Updater kennt kein Downgrade.

## Laufzeitdaten beim Nutzer

Seit 8.1 liegt alles, was den PC betrifft, maschinenweit; im Profil bleiben nur die Daten der
Oberfläche. Vorhandene Dateien aus 8.0 werden beim ersten Start einmalig übernommen (kopiert,
Original auf `*.uebernommen` umbenannt).

| Was | Wo |
| --- | --- |
| Entscheidungen des Nutzers („so gewollt“) | `%ProgramData%\WindowsWartung\entscheidungen.json` |
| Protokolle je Lauf (`lauf-*.jsonl`, zwei Leser) | `%ProgramData%\WindowsWartung\protokoll\` |
| Verlauf | `%ProgramData%\WindowsWartung\verlauf\history.json` (+ `.alt`) |
| Zeitplan der geplanten Wartung | `%ProgramData%\WindowsWartung\zeitplan.json` |
| Sicherungen (`.reg` der Registrierungs-Suche, verborgene Updates, Werte) | `%ProgramData%\WindowsWartung\sicherungen\<art>\` |
| App-Protokoll (Host, Helfer, geplante Wartung) | `%ProgramData%\WindowsWartung\logs\app.log` |
| WebView2-Datenordner, Zoom (`zoom.txt`) | `%LOCALAPPDATA%\WindowsWartung\` |
| Update-Arbeitsordner (`update.zip` oder `setup.exe`, Sicherung `vorher\`, `ww_update.cmd`, bei Fehlschlag `fehler.txt`) | `%LOCALAPPDATA%\WindowsWartung\update\` (wird beim nächsten Update geräumt, der Ordner selbst bleibt) |
| Merker eines laufenden Updates (`pending_update.txt`, bis zum nächsten Start) | `%LOCALAPPDATA%\WindowsWartung\` |

Fällt ProgramData weg (kein Schreibrecht), weicht die App auf `%LOCALAPPDATA%` aus und sagt das
im Protokoll.

**Rechte auf dem Ordner.** Dateien, die der erhöhte Helfer oder die geplante Wartung anlegt,
gehören sonst der Administratorengruppe, und der nicht erhöhte Host könnte sie nicht mehr
ersetzen (`File.Replace` braucht Löschrecht). Darum bekommt `%ProgramData%\WindowsWartung`
`BUILTIN\Users` Ändern, vererbt auf Unterordner und Dateien, an drei Stellen:

1. der Installer (`[Dirs] … Permissions: users-modify`, gilt auch, wenn der Ordner schon da ist),
2. der Helfer bei jedem Start (`Ablage.RechteSichern()`, erhöht), die geplante Wartung und
   der erhöht gestartete Host (`Program.cs`),
3. der Host, wenn er den Ordner selbst anlegt (als Besitzer darf er die Rechte setzen, auch ohne
   Erhöhung; ein fremder Ordner bleibt unangetastet, Rückgabe `false`, nie eine Ausnahme),
4. die Austausch-Batch des ZIP-Updates (läuft erhöht): `icacls "%ProgramData%\WindowsWartung"
   /grant *S-1-5-32-545:(OI)(CI)M /T`.

**ZIP-Update von 8.0:** Keine der vier Stellen greift dort beim ersten Start. 8.0 legte
`entscheidungen.json` erhöht an (Besitzer Administratoren, `Users` nur Lesen), die Batch von
8.0 kennt das `icacls` noch nicht, und der Host setzt Rechte nur auf einem Ordner, den er selbst
anlegt. Bis zum ersten Lauf mit Rechten (Ergänzen, Werkzeug, Tiefenprüfung: `RechteSichern`
im Helfer) schlägt `File.Replace` auf die Antworten fehl; die Oberfläche sagt dann „Die
Antworten gehören noch der vorigen Fassung; nach dem nächsten Schritt mit Administratorrechten
klappt es“. Der Installer-Weg hat das Problem nicht (Stelle 1).

**Abzweigungen unter ProgramData.** `Users` Ändern auf dem ganzen Baum heißt: jedes lokale
Konto darf `logs\`, `verlauf\` oder `sicherungen\` löschen und als Junction neu anlegen. Die
erhöhten Schreiber (Helfer, geplante Wartung, erhöhter Host) folgen einer Abzweigung nicht:
`Ablage.IstAbzweigung(pfad)` prüft jede Komponente unterhalb von `Maschinenweit()` auf
`ReparsePoint`, `Ablage.Unterordner` liefert dann `null`, der Protokoll-Konstruktor schreibt
keine Datei (Zeilen bleiben im Speicher), die Registrierungs-Sicherung legt keine `.reg` in eine
Abzweigung, `treiber.sichern` lehnt einen Zielordner mit Abzweigung im Pfad ab. Ein
fehlgeschlagenes **Löschen** der Schreibprobe schickt den Host nicht mehr in den
Ausweichordner, nur ein fehlgeschlagenes Schreiben.

Kein Wert verlässt den Rechner. Das Werkzeug funkt nirgendwohin außer für die Update-Prüfung.
