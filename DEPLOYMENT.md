# Bauen, signieren, veröffentlichen, aktualisieren

Das Pendant zur `DEPLOYMENT.md` der Webprojekte, für ein Desktop-Werkzeug. Was hier steht,
ist der Weg, den jedes Release nimmt. Stand: v8.3.0 (Maßnahmen Stufe 3 an der Karte mit
Bestätigung, DISM über die DISM-API, Programm-Update als Plan `update.anwenden` über den Helfer,
Techniker-Ansicht; Maßnahmen Stufe 1 und 2 aus 8.2 und Rechte-Modell B aus 8.1 unverändert).

## Aufbau

```
kern/        Datenmodell (Systembild, Befund, Plan), Regeln, Entscheidungen, Protokoll, JSON
             hängt von nichts ab: kein WinForms, kein WMI, kein Prozessstart (Test wacht darüber)
             Massnahmen.cs (Katalogkopf: Massnahme, Nachweis mit drei Ergebnisarten, Finde/Alle/Freischaltbare),
             Massnahmen.Katalog.cs (die Tabelle je Kennung: Stufe, Texte, Sicherung, Rückweg, Parameter,
             Nachweis als reine Funktion über dem NEUEN Systembild)
kern/Regeln/ eine Datei je Bereich, reine Funktionen über dem Systembild, Schwellen mit Quelle;
             hängen Kennungen und Pflichtparameter (Befund.Parameter) an Befunde
sammler/     füllt das Systembild im eigenen Prozess: WMI, Ereignisprotokoll, Registry, COM, P/Invoke
             startet keinen Prozess (Test wacht darüber); Admin-Quellen tragen nicht erhöht "zugriff" ein
helfer/      der erhöhte Teil, dieselbe EXE mit --helfer (kein WinForms, kein WebView2; Test wacht darüber):
             Pipe.cs (Named Pipe, Sicherheitsbeschreibung nur für Aufrufer-SID und SYSTEM),
             Nachricht.cs (Anfrage/Antwort als JSON-Zeilen), Katalog.cs (Kennung → Maßnahme mit Prüfung),
             Ausfuehrung.cs (Plan Schritt für Schritt, Protokoll), Werkzeuge.cs (Prozessstart ohne Shell,
             ein Job-Objekt je Schritt, Wachhund), Messung.cs (erhöhtes Systembild), Helfer.cs (Einstieg, drei Modi)
helfer/Massnahmen/  die M3- und M4-Kennungen, eine Datei je Bereich: Netz.cs (DNS-Cache, DHCP, Namensserver,
             seit 8.3 hosts, Winsock, IP-Stack), Sicherheit.cs (Signaturen, Schnellscan, Firewall, SmartScreen,
             UAC, seit 8.3 eingehend blockieren, Firewall-Reset mit .wfw-Export, Fremd-Firewall-Vorbedingung),
             Datentraeger.cs (TRIM, Scan, Optimieren, Punkt, seit 8.3 Spot-Fix und Offline-Reparatur),
             Leistung.cs (Autostart, Aufgabe, Dienst, seit 8.3 Winlogon), Updates.cs (Update-Dienst, verbergen,
             seit 8.3 update.zuruecksetzen), Hardware.cs (Gerät, seit 8.3 Treiber neu mit pnputil-Export);
             neu 8.3: Speicher.cs (Papierkorb, SHEmptyRecycleBin), Stabilitaet.cs (Speichertest, bcdedit),
             System.cs (DismApi als P/Invoke auf dismapi.dll, Tiefenprüfung, Reparatur mit SFC, Punkt zurück
             über WMI), Programm.cs (update.anwenden: Prüfsumme, Herausgeber, Version, Startprüfung, Tausch
             mit .vorher, Aufräumen); Rueckweg.cs (Kennung rueckweg), Auto.cs (wartung.massnahmen mit Freigaben),
             Gemeinsam.cs (Sicherungsdatei mit Besitzer/DACL/Handle-Prüfung, seit 8.3 Export-Ordner und
             ExportSchuetzen/ExportOeffnen, Wmi2, Com, Reg, Nachweise), MassnahmenM3.cs (Sammelstelle);
             kein PowerShell, kein Process.Start, kein dism.exe (Test wacht darüber)
host/        Fenster, WebView2-Brücke, Hauptweg (CheckFlow, seit 8.2 auch Maßnahme, Rückweg, Nachweis,
             seit 8.3 Bestätigung der Stufe 3, Techniker-Ansicht, DISM-Urteil aus dism.zustand),
             Suchläufe (ScanFlow), Update (seit 8.3 als Plan über den Helfer, Neustart über Program.cs);
             Ausfuehrer.cs (IAusfuehrer: lokal im eigenen Prozess oder über die Pipe),
             HelferClient.cs (Start per runas, Wiederverwendung, --pipe-Abnahmeweg),
             Selbstpruefung.cs (eigene Signatur per WinVerifyTrust, ui\ gegen ui-hashes.txt; seit 8.3 fail closed)
sammler/Quellen/  eine Datei je Quelle; neu 8.3 Speicherquelle.cs (Papierkorb über SHQueryRecycleBin),
             Sicherheitsquelle.cs liest DefaultOutboundAction, Autostartquelle.cs prüft Aufgaben über die Signatur,
             Netzquelle.cs liest den Ort der hosts-Datei (DataBasePath)
src/         Werkzeugkasten (Katalog), Schritte.cs (Schrittbaukasten mit Parameterprüfung, von Host
             und Helfer benutzt), CommandRunner (baut Pläne), Verlauf, Protokoll der App, Signaturbindung,
             Scheduler.cs (seit 8.3 Sperre Global\WindowsWartung.zeitplan und Zeitbudget-Auffrischen)
ui/          Oberfläche (HTML/CSS/JS)
tests/       run-tests.ps1 (alle Prüfungen), proben/ (Kernproben, seit 8.3 auch SpeicherplatzProben.cs),
             aufzeichnungen/ (Testbilder), HelferProbe.cs (in-process: Pipe, Sicherungsdatei, Export)
tools/       csc.ps1 (Roslyn-Aufruf), bau-kern.ps1 (Kommandozeile), lauf-proben.ps1
installer/   Setup-Skript (Inno Setup), setzt die Rechte auf dem Datenordner
```

Der Host startet seit 8.1 kein Werkzeug **mit Rechten** mehr selbst: kein `dism.exe`, kein
`sfc.exe`, kein `powershell.exe` für Eingriffe (der Test greppt `host\*.cs`, `CommandRunner`
und `AutoRunner`; `Process.Start` dort nur an drei Stellen: runas-Start des Helfers in
`HelferClient.cs`, runas-Start des Installers in `ShellForm.cs` und der nicht erhöhte Neustart
nach dem Update in `Program.cs`, dazu das Öffnen von Ordnern). Die Austausch-Batch des
ZIP-Updates gibt es seit 8.3 nicht mehr. Ohne Rechte startet der Host weiterhin:
`powershell.exe` zum Lesen der App-Liste (`AppxCleaner.List`), `schtasks.exe` für den
Selbststart-Schalter und die Anzeige des Zeitplans, `powercfg.exe` für den Energieplan,
`shutdown.exe` für „Wenn alles fertig ist“ und `explorer.exe` zum Öffnen von Ordnern. Alles,
was Rechte braucht, geht als **Plan** (Kennungen plus geprüfte Parameter) an den Helfer; der
baut die Schritte aus seinem Katalog selbst und nimmt von außen nie einen Dateinamen mit
Argumenten an. Im Helfer hängt jeder gestartete Prozess an einem **Job-Objekt**
(`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`); Wachhund und Abbruch beenden das Job-Objekt, nicht eine
PID: das trifft auch Enkelprozesse wie `DismHost.exe`, die nach dem Ende von DISM noch die
Ausgabe halten, und nie einen fremden Prozess mit wiederverwendeter PID. Startfehler (-1) und
Zeitgrenze (-2) zählen immer als Problem, auch bei Schritten mit `IgnoreExit`.

**DISM seit 8.3 über die DISM-API, nicht über `dism.exe`.** `helfer\Massnahmen\System.cs`
(`DismApi`) ruft `dismapi.dll` per P/Invoke (`DismInitialize`, `DismOpenSession`,
`DismCheckImageHealth`, `DismRestoreImageHealth`, `DismCloseSession`, `DismShutdown`,
`DismGetLastErrorMessage`, `DismDelete`; Initialisierung je Aufruf unter einer prozessweiten
Sperre, Abbruch über ein Ereignis, Fortschritt über den Rückruf, Protokoll
`<Laufzeitordner>\protokoll\dism-<datum>.log`). Das Urteil ist ein Wert (`dism.zustand`
`healthy|repairable|nonrepairable|fehler`, dazu `dism.hresult`, `dism.dauerS`, bei der
Reparatur `dism.repariert`), keine Textsuche mehr in einer lokalisierten Ausgabe; der Host
(`CheckFlow.EvaluateFiles`) liest nur diese Werte. SFC bleibt ein Prozess (`sfc.exe`,
`Encoding.Unicode`) am Job-Objekt; sein Zustand (`sfc.zustand`
`ohneBefund|repariert|nichtRepariert|fehler`, `sfc.exit`) kommt aus den `[SR]`-Zeilen der
`CBS.log` ab Startzeit des eigenen Laufs, englische Muster, unabhängig von der Konsolensprache.
`DISM.exe` kommt in `helfer\Massnahmen\` und `helfer\Katalog.cs` nicht mehr vor (die Suite
greppt); die Werkzeugkasten-Schritte in `src\ActionCatalog.cs` („Rundum-Reparatur“, „Windows
über das Internet reparieren“, die Update-Reste und der Werkzeugsatz der geplanten Wartung, 7
`Dism(...)`-Schritte) rufen `dism.exe` weiter über den Helfer als Werkzeug.

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
  `tests\aufzeichnungen\gepflanzt-*.json` (handgeschriebene Bilder, über 10.000 Zusicherungen
  in zehn Probenklassen), darunter die Fehlerfälle, die ein Reparaturwerkzeug falsch behandeln
  könnte (absichtlich deaktiviertes Gerät, SMART-Warnung, englisches System). Seit 8.2 dazu
  `MassnahmenProben`: jede Kennung, die eine Regel anhängt, steht im Katalog; jeder Eintrag hat
  Stufe, Art, Texte und Nachweis; Vorher/Nachher-Paare `gepflanzt-massnahmen-<kennung>-*.json`
  (Nachweis über VORHER nicht bestanden, über NACHHER bestanden, die Regeln hängen die Kennung
  nach NACHHER an keinen Problem-Befund; 16 Paare mit 8.2, 26 mit 8.3); `Freischaltbare()` =
  genau sechs; jeder Befund mit Kennung trägt die Pflichtparameter. Seit 8.3: jede Stufe-3-Kennung
  mit `Art == ausfuehren` trägt einen `Verlust` mit mindestens 1 Satz (genau 11 Kennungen), eine
  Sicherung `export` hat nie den Rückweg „von selbst“, für jede Kennung mit Unlesbar-Zweig gibt
  es eine `Unbestimmt`-Probe, und die neue Klasse `SpeicherplatzProben` misst den
  Papierkorb-Befund (Schwelle 1 GB, ok-Befund darunter, null ergibt nichts, Parameter
  `dateien`/`bytes`). Läuft ohne Rechte, ohne Fenster, ohne Windows-Zugriff. Die Klasse
  `DieserPcProben` prüft zusätzlich drei echte Aufzeichnungen des Entwicklungsrechners
  (`dieser-pc-*.json`); die bleiben lokal (Software-Inventar), in der CI meldet die Klasse sich
  als übersprungen.
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
| Host startet keine Werkzeuge | Grep über `host\*.cs`, `src\CommandRunner.cs`, `src\AutoRunner.cs`: kein `DISM.exe`, `sfc.exe`, `powershell.exe`, kein `Process.Start(` außer den benannten Ausnahmen (runas-Start in `HelferClient`, `Shell.cs`, WebView2-Link, runas-Start des Installers in `ShellForm.cs`; seit 8.3 statt der Update-Batch genau ein nicht erhöhter Neustart nach dem Update in `host\Program.cs`) |
| Trockenlauf aller Kennungen | eine Plandatei mit jeder Kennung des Katalogs und gültigen Parametern → `--helfer --plan <datei> --trocken` endet mit Exit 0, je Kennung eine `schritt`-Zeile in `lauf-<planId>.jsonl`, währenddessen kein Kindprozess (Beobachtung mit gebundenem Handle); eine vorher angelegte Wächterdatei in `%TEMP%` ist danach noch da (die In-Process-Eingriffe von `speicher.aufraeumen` fassen im Trockenlauf nichts an), und die Folgenummer der Wiederherstellungspunkte hat sich nicht bewegt |
| Ablehnung | 13 Fälle → jeweils Exit 2 mit Protokollzeile `abgelehnt`, kein Prozess: Kennung `format.c`; `werkzeug` mit `id: "999"` und mit `sicherung: "ja"`; `netz.diagnose` mit `ziel: "a b"`; `wiederherstellungspunkt.anlegen` mit Sonderzeichen in `beschreibung`; `wiederherstellungspunkt.zurueck` mit `folge: "0"`; `wartung.auto` mit `schluessel: "format"`; `apps.entfernen` mit einem kritischen Paket; `treiber.sichern` mit `ordner` unter `%WINDIR%`; `zeitplan.anlegen` mit `modus: "x"`; `speicher.aufraeumen` mit `schluessel: "downloads"`; `registrierung.entfernen` mit `kennungen: "ab;c/d"`; leerer Plan |
| Plan-Id mit Pfadzeichen | `--plan` mit einer Id aus `..` und `\` ersetzt die Id, nichts landet außerhalb von `protokoll\` |
| Pipe-Probe | Helfer mit `--helfer --pipe ww-test-<guid> --sid <eigene SID>` (nicht erhöht erlaubt), Client über `NamedPipeClientStream`: `ping` → `ergebnis`, `unsinn` → `abgelehnt`, `ende` → Prozess endet binnen 5 s; erhöht (CI-Runner) zusätzlich `messen` → `systembildJson` mit `erhoeht: true` |
| Pipe-Zugriffsschutz | (a) `--sid S-1-1-0` (Gruppe „Jeder“, kein Konto) → Exit 7 binnen 10 s, keine Pipe unter `\\.\pipe\`; (b) `--sid` mit einer wohlgeformten fremden Konto-SID: der Verbindungsversuch des eigenen Kontos scheitert an der Pipe-Beschreibung (`UnauthorizedAccessException`), bevor der Helfer eine Zeile liest; nur wenn die Suite als SYSTEM läuft (in der Beschreibung enthalten), greift der zweite Riegel: erste Anfrage `abgelehnt`, Exit 4 |
| PipeSicherheit | `tests\HelferProbe.cs` ruft `Pipe.PipeSicherheit(sid)`: genau zwei Regeln (SID + SYSTEM), keine für Jeder, Benutzer oder Authentifizierte; dazu `Users`-Rechte, einmalige Übernahme, Lauf-Id als Dateiname |
| Startprüfung | `--selbstpruefung <datei>` → Exit 0; eine Kopie von `ui\app.js` um ein Byte geändert → Exit 6 (unsigniert) bzw. 5 (signiert); Exit 5 misst nur die signierte EXE, in der CI läuft der Test vor der Signatur (die signierte EXE misst der Release-Workflow selbst, siehe „Veröffentlichen“); eine fremde Datei `bin\ui\probe-fremd.js` ist nur ein Hinweis (Exit 0), ist nach der Probe nachweislich wieder weg, und `app.js` ist unverändert |
| Laufzeitordner | `Ablage.RechteSichern()` und die Übernahme alter Dateien gegen einen Temp-Ordner (kein Zugriff auf ProgramData im Test); Grep auf `UebernahmeAbschliessen` (die alte Datei wird auch dann auf `*.uebernommen` umbenannt, wenn die neue schon existiert); der Installer setzt `users-modify` |
| Kein RunJobs/Job | Grep `RunJobs` und `class Job` über `host\` und `src\` = 0: jeder Lauf mit Rechten ist ein Plan |

Neu mit 8.2 die Gruppe **„v8.2: Maßnahmen“** (9 Prüfungen, `$erwartet` = 61; Vertrag
`docs\M3-ENTWURF.md` §10.1, die Namen unten sind die Kurzformen der Zeilen im Skript). Nichts
davon greift live in den Rechner ein, mit einer Ausnahme (die letzte), die nur erhöht läuft:

| Prüfung (Reihenfolge wie im Skript) | Was sie belegt |
| --- | --- |
| Trockenlauf der M3-Kennungen | Grundmenge: die Laufzeitmenge `MassnahmenM3.Alle()` (Gruppe `kennungen` der `HelferProbe`, in-process) muss genau die feste Liste im Skript sein (fehlt eine oder ist eine zu viel → rot; seit 8.3 stehen die 13 M4-Kennungen in derselben Liste); jede `Massnahmenbau.Neu("…")` in `helfer\Massnahmen\*.cs` (auch über eine Konstante der Datei) muss in der Laufzeitmenge liegen, sonst ist der Aufruf tot (Gegenprobe per Grep; eine per Objektinitialisierer angemeldete Kennung wie `wartung.massnahmen` sähe der Grep allein nicht). Plan A mit maschinenunabhängigen Parametern (`netz.dnscache.leeren`, `netz.dns.zuruecksetzen` und `netz.dhcp.erneuern` mit dem ersten IPv4-DHCP-Adapter, `sicherheit.signaturen.aktualisieren`, `sicherheit.schnellscan`, `sicherheit.firewall.einschalten profil=2`, `sicherheit.smartscreen.warn`, `sicherheit.uac.standard`, `datentraeger.trim.einschalten`, `volume.scan buchstabe=C`, `volume.optimieren buchstabe=C`, `systemschutz.punkt.anlegen`, `leistung.aufgabe.deaktivieren` mit `\Microsoft\Windows\Defrag\ScheduledDefrag`, `leistung.dienst.startart name=Spooler startart=Manual`, `update.dienst.aktivieren name=wuauserv`, `wartung.massnahmen freigaben=netz.dnscache.leeren`) → `--trocken` Exit 0, je Kennung eine `schritt`-Zeile, kein Kindprozess, keine Datei unter `sicherungen\massnahmen` (vorher/nachher gezählt), Wächterdatei bleibt, Folgenummer der Wiederherstellungspunkte und 5 Registrywerte (SmartScreen, UAC, TRIM und die Drossel der Wiederherstellungspunkte `SystemRestorePointCreationFrequency`, die `systemschutz.punkt.anlegen` im echten Lauf auf 0 setzt) vorher == nachher; weil ein Rechner die Drossel schon auf 0 haben kann (0 → 0 sieht nichts), dazu strukturell: `DrosselAufheben(k)` steht in `helfer\Katalog.cs` genau einmal und hinter dem `return false;` des `if (k.Trocken)`-Blocks von `Wiederherstellungspunkt`. Plan B mit Objekten, die es geben muss (`leistung.autostart.deaktivieren` mit einem vorhandenen Run-Wert, `update.verbergen` mit einer UpdateID aus der WUA-Suche, `geraet.aktivieren` mit einem Gerät mit Code 22, sonst Platzhalter) → Exit 0 oder 1, nie 2, kein Kindprozess, keine Datei |
| Helfer lehnt ungültige M3-Parameter, fremde Sicherungen und nicht freischaltbare Freigaben ab | je Fall Exit 2 mit Protokollzeile `abgelehnt`, kein Prozess: `netz.dhcp.erneuern adapter=x` und `adapter=10 OR 1=1`; `sicherheit.firewall.einschalten profil=3`; `volume.scan buchstabe=C:\..` und `buchstabe=CD`; `leistung.autostart.deaktivieren hive=HKLM ort=Foo name=a` und `name=a;b`; `leistung.aufgabe.deaktivieren pfad=\A"B`, `pfad=\Fremd\..\Update` und `pfad=\Microsoft\Windows\Windows Defender\Probe`; `leistung.dienst.startart name=WinDefend startart=Disabled` und `name=a'b startart=Manual`; `update.dienst.aktivieren name=Spooler`; `update.verbergen updateId=abc`; `geraet.aktivieren instanzId=a'b`; `rueckweg sicherung=..\..\zeitplan.json`, `sicherung=hosts` und eine nicht vorhandene `.json`; `wartung.massnahmen freigaben=` (leer), `freigaben=foo`, `freigaben=wartung.auto`, `freigaben=Bad;Chars`, `freigaben=netz.dhcp.erneuern` (nicht freischaltbar), `freigaben=volume.optimieren` (seit 8.2 nicht freischaltbar). Gegenprobe: `leistung.dienst.startart name=MSSQL$SQLEXPRESS startart=Manual` ist **nicht** Exit 2 (`$` ist im Dienstnamen erlaubt) |
| Sicherungsdatei: nur Besitzer Administratoren, genau die geschützte DACL, ein Name | `tests\HelferProbe.cs` in-process mit `Sicherungsdatei.OrdnerFuerProbe` auf einem Temp-Ordner: erhöht (CI-Runner, lokale Werkzeug-Shell) schreibt `Schreiben` eine Datei mit Besitzer Administratoren, Vererbung aus und genau drei ACEs, `Lesen` gelingt; danach Besitzer auf das eigene Konto → `Lesen` lehnt ab („stammt nicht vom Helfer“); eine zusätzliche ACE für das eigene Konto → abgelehnt; ein Hardlink auf die Datei → beide Namen abgelehnt („mehr als einen Namen“). Nicht erhöht: `Schreiben` wirft `InvalidOperationException`, oder die Datei gehört dem Nutzer und `Lesen` lehnt ab (beides gilt). Ein Name mit Pfadzeichen, `..`, Leerzeichen oder falscher Endung → `ArgumentException`. Der Trockenlauf schreibt keine Datei (zählt die erste Prüfung) |
| Rückweg-Inhalt: untergeschobene Werte werden abgelehnt, gültige angenommen | in-process mit untergeschobenen Dateien im Probe-Ordner (`RueckwegMassnahmen.InhaltPruefen` bzw. `ZurueckNetzPruefen`/`ZurueckSicherheitPruefen`): `update.dienst.aktivieren` mit `vorher.startart=Disabled` → abgelehnt; `leistung.dienst.startart` für `mpssvc` mit `Disabled` → abgelehnt; `sicherheit.firewall.einschalten` mit `FirewallEnabled=2` → abgelehnt, mit `0` und `Profil=2` → gültig; `sicherheit.smartscreen.warn` mit `SmartScreenEnabled=Off;calc` → abgelehnt; `sicherheit.uac.standard` mit `EnableLUA=7` → abgelehnt; `netz.dns.zuruecksetzen` mit `NameServer=8.8.8.8;evil` → abgelehnt, leer → gültig (DHCP); `geraet.aktivieren` mit `instanzId=a'b` → abgelehnt |
| Kein PowerShell und kein Process.Start in helfer\Massnahmen\ | Grep `powershell` (ohne Kommentare) über `helfer\Massnahmen\*.cs` = 0 und Grep `Process.Start` dort = 0: Prozesse (`fsutil.exe`, `schtasks.exe`, `MpCmdRun.exe`) laufen nur über `Werkzeuge.Lauf`, alles andere über WMI, COM und Registry |
| Werkzeugkasten: genau 3 Einträge durch Maßnahmen ersetzt | genau 3 `ErsetztDurch = "…"` in `src\ActionCatalog.cs`, jeder Wert steht als `Kennung = "…"` in `kern\Massnahmen.Katalog.cs`, und `host\ShellForm.cs` filtert die `catalog`-Nachricht mit `string.IsNullOrEmpty(a.ErsetztDurch)` |
| Nachrichten der Maßnahmen stehen wörtlich auf beiden Seiten | die Namen wörtlich auf beiden Seiten: `type = "massnahmeVorschau"` und `"freigaben"` im Host, `case 'massnahmeVorschau'` und `case 'freigaben'` in `ui\app.js`; `type == "massnahmeAusfuehren"`, `"massnahmeRueckweg"`, `"massnahmeAnsicht"`, `"freigabenSetzen"` im Verteiler des Hosts und als Sendungen in `app.js`. Eine Umbenennung auf nur einer Seite fällt so auf; die Prüfung „Host startet keine Werkzeuge“ bleibt daneben bestehen |
| Freischaltbar sind genau sechs Maßnahmen | Grep `Freischaltbar = true` in `kern\Massnahmen.Katalog.cs` = 6, `volume.optimieren` nicht darunter (die Kernprobe (d) misst dasselbe über `Freischaltbare()`) |
| CI live: netz.dnscache.leeren ohne --trocken | nur erhöht, sonst übersprungen mit Zeile: der Plan `netz.dnscache.leeren` **ohne** `--trocken` → Exit 0, `nachweis`-Zeile mit `bestanden = true`, kein Kindprozess (`MSFT_DNSClientCache.Clear` ist WMI); das Laufprotokoll wird danach entfernt. Nichts anderes läuft live |

Neu mit 8.3 die Gruppe **„v8.3: Stufe 3“** (9 Prüfungen, `$erwartet` = 70; Vertrag
`docs\M4-ENTWURF.md` §8.2 und §10.5, die Namen unten sind die des Vertrags, das Skript schreibt
sie in ASCII; die vierte Zeile heißt im Skript „Export-Herkunft … und Rueckweg-Inhalt M4“, weil
sie die 4 Sicherungsfälle aus §8.2 Punkt 2 mitträgt, siehe die Abweichung in der Zeile
„Ablehnungen“). Keine davon greift live in den Rechner ein; jeder Helferlauf hier trägt
`--trocken`, und die Stufe-3-Kennungen laufen in der Suite **nie** ohne `--trocken`:

| Prüfung (Reihenfolge wie im Skript) | Was sie belegt |
| --- | --- |
| Trockenlauf der M4-Kennungen | Grundmenge: jede Kennung aus `Massnahmenbau.Neu(…)` in `helfer\Massnahmen\*.cs` steht in der festen Liste im Skript (20 aus M3 samt `wartung.massnahmen`, 13 aus M4 samt `update.anwenden`; fehlt eine oder ist eine zu viel → rot); das misst die Prüfung 1 der Gruppe v8.2 („Trockenlauf der M3-Kennungen“) über die Laufzeitmenge `MassnahmenM3.Alle()` (Gruppe `kennungen` der `HelferProbe`, der Grep über `Massnahmenbau.Neu` nur als Gegenprobe), seit 8.3 mit den 13 M4-Kennungen in derselben Liste; die v8.3-Prüfung wiederholt die Mengenprüfung nicht. Plan A `--trocken` mit `netz.hosts.bereinigen`, `netz.winsock.zuruecksetzen`, `netz.ip.zuruecksetzen`, `sicherheit.firewall.eingehend.blockieren profil=2`, `sicherheit.firewall.zuruecksetzen`, `volume.spotfix buchstabe=C`, `volume.reparatur buchstabe=C`, `dateien.reparieren`, `leistung.winlogon.zuruecksetzen`, `speicher.papierkorb.leeren`, `stabilitaet.speichertest.planen`, `update.zuruecksetzen`, `wiederherstellungspunkt.zurueck folge=<vorhandene Folgenummer>` (der Trockenlauf liest die Liste und prüft die Nummer, `helfer\Massnahmen\System.cs`; eine Nummer ohne Punkt, etwa `1` auf einem Rechner bei Folgenummer 272, endet mit Exit 1 „Es gibt keinen Wiederherstellungspunkt mit der Nr. …“, deshalb nimmt das Skript eine Nummer aus der Liste). Jede Vorbedingung, die der Helfer aus dem Systemzustand liest, liest das Skript vorher mit demselben Kriterium; ist sie nicht erfüllt, fällt die Kennung weg, und die Ergebniszeile nennt sie: `sicherheit.firewall.zuruecksetzen` bei registrierter Fremd-Firewall (SecurityCenter2 `FirewallProduct` ohne die Windows-Namen aus `IstWindowsFirewallName`; der Helfer lehnte sonst mit Exit 2 ab), `update.zuruecksetzen` bei `wuauserv` auf `Disabled` (Vorbedingung des Helfers, Exit 1) oder wenn die WUA-Suche `IsHidden=1` in einem eigenen Prozess binnen 60 s kein Ergebnis liefert, `volume.spotfix` und `volume.reparatur` ohne festes `MSFT_Volume C:`, `wiederherstellungspunkt.zurueck` ohne lesbare Liste → Exit 0 in unter 180 s, je Kennung eine `schritt`-Zeile, kein bestandener Nachweis, `ende`-Zeile mit `trocken = true`, kein Kindprozess, keine Datei unter `sicherungen\massnahmen` und `export\`, und 16 Lesewerte vorher == nachher: SHA-256 der hosts-Datei; Winlogon `Shell` und `Userinit`; `Enabled`/`DefaultInboundAction`/`DefaultOutboundAction` je Firewall-Profil (Domain, Private, Public); `SoftwareDistribution` und `catroot2` (vorhanden, kein `.old` neu entstanden); `StartMode` von `wuauserv`, `bits` und `cryptsvc`; `bcdedit /enum {bootmgr}` (`bootsequence` gesetzt oder nicht, dazu der ganze Text); Zahl der Papierkorb-Einträge (`Shell.Application`, Namespace 10); das Dirty-Bit von C: (`fsutil dirty query`) und `BootExecute` des Session Managers (die zwei Spuren, die `MSFT_Volume.Repair(OfflineScanAndFix)` für `volume.spotfix` und `volume.reparatur` auf dem Windows-Laufwerk ohne Kindprozess hinterließe). Plan B `geraet.treiber.neu instanzId=<Gerät mit Code weder 0 noch 22, sonst irgendein Code ≠ 0, sonst PCI\VEN_0000&DEV_0000\0>` → Exit 0 oder 1, nie 2, kein Kindprozess, kein bestandener Nachweis |
| Ablehnungen (Exit 2) | 13 Fälle → jeweils Exit 2 mit Protokollzeile `abgelehnt`, keine `anfang`-Zeile, kein Prozess: `sicherheit.firewall.eingehend.blockieren profil=3` und ohne `profil`; `volume.spotfix buchstabe=CD`; `volume.reparatur buchstabe=C:\..`; `geraet.treiber.neu instanzId=a'b` und ohne `instanzId`; `update.anwenden sha256=abc`, ohne `sha256` und mit 63 statt 64 Hex-Zeichen; `wiederherstellungspunkt.zurueck folge=0` und `folge=1 OR 1=1`; `wartung.massnahmen freigaben=speicher.papierkorb.leeren` und `freigaben=netz.winsock.zuruecksetzen` (Stufe 3 ist nie freischaltbar). Gegenprobe: `geraet.treiber.neu instanzId=ACPI\PNP0A08\0 (Probe) #1` (Leerzeichen, Klammern und Raute aus der Zeichenmenge des Kerns) ist **nicht** Exit 2 (Exit 0 oder 1). Abweichung zum Vertrag §8.2 Punkt 2: die vier Fälle mit untergeschobener Sicherungsdatei (`rueckweg` mit `export=..\hosts` bzw. `export=x.wfw`, hosts-Sicherung mit NUL im Inhalt, IP-Sicherung mit `IP=evil`) laufen nicht über den Helfer, sondern in-process in der Zeile „Export-Herkunft und Rückweg-Inhalt M4“ über `InhaltPruefen`, dieselbe Prüfung, die der Helfer vor dem ersten Schritt mit Exit 2 abbricht; eine echte Datei unter dem geschützten Ordner wäre ein Eingriff (wie v8.2 Punkte 3 und 4) |
| Bestätigungspflicht (Host, Grep) | `bestaetigt` im Verteiler von `massnahmeAusfuehren` und `restoreRevert` (`host\ShellForm.cs`, jeweils als `ToBool(m, "bestaetigt")`), in `MassnahmeAusfuehren(kennung, schluessel, bestaetigt)` (`host\CheckFlow.cs`: `Stufe >= Massnahme.StufeUnumkehrbar && !bestaetigt` → `flowError` „Die Bestätigung fehlt“) und in `RestoreRevert(seq, bestaetigt)`; in `ui\app.js` steht `bestaetigt: true` an genau zwei Stellen (`massnahmeStarten` nach dem Kontrollkästchen `data-verlust` der Vorschau, `restoreRevert` nach dem Kontrollkästchen der letzten Nachfrage in `zuruecksetzenModal`), `massnahmeAusfuehren` wird nur aus `massnahmeStarten` gesendet (genau 2 Stellen: eine mit `bestaetigt: true` im Zweig `bestaetigt === true`, eine darunter ohne das Feld; jede weitere Sendestelle wäre ein Weg an der Sperre vorbei); der Knopf hängt am Kästchen (`ok.disabled = !kasten.checked`, dazu `if(!kasten.checked)` vor dem Senden), die Vorschau zeigt den Kasten `modal-verlust` mit `v.verlust` und dem Satz „Ich habe verstanden, was verloren geht“, und die Vorschau-Antwort des Hosts trägt `verlust = m.VerlustMit(…)` |
| Export-Herkunft und Rückweg-Inhalt M4 (`HelferProbe`, in-process, Gruppen `export` und `m4rueckweg`, mindestens 60 Teilprüfungen, erhöht gelaufen 120) | Export-Herkunft: `ExportSchuetzen` und `ExportOeffnen` wie die JSON-Prüfung: Besitzer Administratoren, exakt die geschützte DACL, ein Name (Hardlink abgelehnt), kein Reparse-Punkt (Datei- und Ordner-Symlink abgelehnt), Name nur aus `[A-Za-z0-9._-]` (`ExportNameGueltig` lehnt `..`, `a\b.wfw`, `..\hosts`, einen Namen mit Leerzeichen oder Tabulator ab; `ExportName` folgt dem Muster `yyyyMMdd-HHmmss-<art>-<4hex>`), Ordner-Export (Treiber) samt Inhalt geschützt; erhöht (CI-Runner, Werkzeug-Shell) an echten Dateien im Probe-Ordner, nicht erhöht daran, dass `ExportOrdner` wirft und eine Nutzerdatei abgelehnt wird. Firewall-Sicherung (`sicherheit.firewall.zuruecksetzen`): `export=..\hosts`, `export=x.wfw` (nicht nach Muster), `export=<Name nach Muster, Datei fehlt>`, ohne `export`, `zustand=foo`, und `export=<Name>` nach dem Löschen der Datei → abgelehnt; `export=<vorhandener, geschützter Name>` mit und ohne `zustand` nach Muster → angenommen (das sind die vier aus der Zeile „Ablehnungen“ verschobenen Fälle samt Ergänzungen; in-process über `InhaltPruefen`, dieselbe Prüfung, die der Helfer vor dem ersten Schritt mit Exit 2 abbricht). Rückweg-Inhalt M4: `leistung.autostart.deaktivieren` unter HKCU mit fremder `profilSid` (S-1-5-21-1-2-3-1001), ohne `profilSid`, mit `S-1-5-18` und mit `abc` → abgelehnt, mit der eigenen SID (auch in Kleinschreibung) und HKLM ohne `profilSid` → angenommen; `netz.hosts.bereinigen` mit NUL, U+0001, Escape U+001B, 262145 Zeichen oder ohne `inhalt` → abgelehnt, mit CRLF/Tabulator/Kommentar, leer und genau 262144 Zeichen → angenommen; `netz.ip.zuruecksetzen` mit `IP=evil`, Maske `255.255.255`, Gateway `fe80::1`, DNS `8.8.8.8;evil`, 5 statt 6 Feldern, 2 Adressen zu 1 Maske, ohne Adresse, SettingID ohne Klammern, doppelter SettingID, 11 DNS-Einträgen, 65 Zeilen oder ohne `adapter` → abgelehnt, ein fester Adapter, zwei Adressen mit zwei Masken, leerer `adapter` und zwei Zeilen → angenommen; `sicherheit.firewall.eingehend.blockieren` mit `DefaultInboundAction=2`, `Profil=3` oder ohne `DefaultInboundAction` → abgelehnt, `0` oder `1` mit Profil `2` oder `4` → angenommen; `leistung.winlogon.zuruecksetzen` ohne `Userinit`, mit Steuerzeichen oder Zeilenumbruch in `Shell`, `Shell` mit 261 Zeichen oder `ShellArt=binary` → abgelehnt, beide Werte, beide leer und `ShellArt=expand` → angenommen; die drei Kennungen ohne Rückweg (`netz.winsock.zuruecksetzen`, `update.zuruecksetzen`, `geraet.treiber.neu` mit und ohne Export) → abgelehnt, die Datei bleibt Beleg; beim Treiber nennt der Grund mit Export den Exportordner und `pnputil /add-driver`, ohne Export den Treiberspeicher; dazu `ZurueckNetzBekannt`, `ZurueckSicherheitBekannt` und `ZurueckLeistungBekannt` kennen genau die Kennungen mit Rückweg |
| DISM nur als API | Grep `DISM.exe`/`dism.exe` in `helfer\Massnahmen\` und `helfer\Katalog.cs` = 0; Grep `DllImport("dismapi.dll"` in `helfer\Massnahmen\System.cs` ≥ 5; kein PowerShell und kein `Process.Start` in `helfer\Massnahmen\` (bestehend) |
| Techniker-Nachricht | `type = "techniker"`, `"technikerText"`, `"technikerGespeichert"` im Host, `case 'techniker'` in `ui\app.js`, `technikerAnsicht`/`technikerText`/`technikerSpeichern` im Verteiler des Hosts |
| Update-Weg trocken | `--helfer --plan` mit `update.anwenden sha256=<64 Hex einer vorhandenen Datei>` und `--trocken`, ohne `update.zip` → Exit 1 mit Zeile „update.zip fehlt“ (nie 2, der Parameter war gültig); Grep: `ww_update.cmd` kommt in `host\ShellForm.cs` nicht mehr vor, `StarteErhoeht` nur noch in `UpdateViaInstaller` |
| Zeitplan-Sperre und Zeitbudget | Grep `Global\WindowsWartung.zeitplan` in `src\Scheduler.cs` (Host, Helfer und `--auto` nutzen dieselbe Konstante); `ZeitbudgetAuffrischen` in `helfer\Helfer.cs`; `Sicherungsdatei.OrdnerAbsichern` in `host\Program.cs` |
| DLL-Suchpfad: System32 vor dem Programmordner | EXE-Kopie samt `ui\` und den drei WebView2-DLLs im Testordner, `%SystemRoot%\System32\version.dll` daneben als `dwmapi.dll` und `cryptbase.dll` (beides keine KnownDlls; `wintrust.dll` bewiese nichts), Start mit `--shot <png> --view start --shotwait 3000`, nach 2 s die Modulliste des Prozesses: `dwmapi.dll` und `cryptbase.dll` aus `System32`, `WebView2Loader.dll` aus dem Testordner, Exit 0, PNG > 10 KB; Grep `DefaultDllImportSearchPaths(DllImportSearchPath.System32)` in `src\AssemblyInfo.cs` und kein `AddDllDirectory` in `host\Program.cs` |

Die Prüfung „Kernproben“ verlangt seit 8.3 `, 10 Probenklassen`; die Prüfung „Host startet
keine Werkzeuge selbst“ kennt drei erlaubte `Process.Start`-Stellen (runas Helfer, runas
Installer, Neustart nach dem Update nicht erhöht in `host\Program.cs`, genau eine dort).

Die Prüfungen aus 8.1 (bin-Frische, Manifest, Schichtregel, Trockenlauf und Ablehnungen der
M2-Kennungen, Pipe, Startprüfung, Laufzeitordner) und 8.2 bleiben (61); `$erwartet` = 70
wird am Ende gegen die gelaufenen Prüfungen gehalten, und alle Zeilen des Skripts bleiben ASCII (5.1 liest ANSI,
Umlaute nur als `\u`-Muster in Regexen). Vor einem Suite-Lauf gehören Reste paralleler Bauten
(`bin\<guid>_WindowsWartung.exe`) gelöscht und die EXE frisch gebaut (bin-Frische).

Kommandozeilen, die dabei benutzt werden (alle ohne Fenster, Ergebnis im Exit-Code und in
`logs\app.log`):

```powershell
.\bin\WindowsWartung.exe --helfer --plan C:\Temp\plan.json --trocken   # Exit = PlanErgebnis.Exit (0 ok, 1 Problem, 2 abgelehnt, 3 Ausnahme, 4 abgebrochen); 7 = Argumente
# Plandatei einer M3-Kennung, z. B. Firewall für das Profil Privat (Trockenlauf: bis zur Sicherung, nichts geschrieben):
#   { "id": "probe-firewall", "titel": "Firewall einschalten", "schritte": [ { "kennung": "sicherheit.firewall.einschalten", "parameter": { "profil": "2" } } ] }
# Plandatei einer M4-Kennung der Stufe 3, z. B. Spot-Fix auf C: (Trockenlauf: nur "(trocken)"-Zeilen, kein Repair-Aufruf, keine Sicherung):
#   { "id": "probe-spotfix", "titel": "Spot-Fix", "schritte": [ { "kennung": "volume.spotfix", "parameter": { "buchstabe": "C" } } ] }
# Ohne --trocken greift der Helfer ein; Exit 0 und die nachweis-Zeile im lauf-<id>.jsonl sind der Beleg, sicherung.datei der Rückweg.
# Eine Stufe-3-Kennung ohne --trocken läuft nur nach der Bestätigung im Fenster; auf der Kommandozeile gibt es dafür keinen Abnahmeweg.
.\bin\WindowsWartung.exe --helfer --messen C:\Temp\bild.json           # erhöhte Messung, unredigiert (nur aus erhöhter Shell sinnvoll)
.\bin\WindowsWartung.exe --helfer --pipe ww-test-1 --sid S-1-5-21-…    # Pipe-Server für einen Client derselben SID
.\bin\WindowsWartung.exe --selbstpruefung C:\Temp\selbst.txt           # Signatur + ui-Liste, Exit 0/5/6; 3 = Prüfung abgebrochen oder Ausgabedatei nicht schreibbar (Grund in app.log)
.\bin\WindowsWartung.exe --aufzeichnen C:\Temp\bild.json               # redigiert (Rechnername, Nutzer, Seriennummern, MAC, private IP)
.\bin\WindowsWartung.exe --pruefen C:\Temp\bild.json                   # Befunde nach bild.json.befunde.txt
.\bin\WindowsWartung.exe --vorgaenger 1234                             # nur vom Programm selbst benutzt: Neustart nach dem Update, wartet bis 30 s auf das Ende der PID
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

Seit 8.2 lassen sich auf diesem Weg auch Maßnahmen belegen, ohne Klick im Fenster: `--shot`
mit `--view check,massnahme:<kennung>` prüft, drückt an der ersten Karte mit dieser Kennung
„Beheben“ und in der Vorschau „Jetzt ausführen“ (800 ms Abstand), misst nach und zeigt die
Nachweiskarte; `--view check,massnahme:<kennung>,rueckweg` drückt danach „Rückgängig“ auf der
Nachweiskarte und bestätigt die Nachfrage (nur, wenn eine Sicherung geschrieben wurde und der
Rückweg möglich ist); `--view check,ergaenzen,massnahme:<kennung>` wartet die erhöhte Messung ab,
bevor es den Knopf sucht (Befunde, die erst erhöht entstehen); `--view schedule` zeigt die
Freigaben; seit 8.3 öffnet `--view check,techniker` nach der Prüfung die Techniker-Ansicht
und `--view techniker` sie direkt (leerer Stand mit „Jetzt prüfen“).
`docs\werkzeuge\abnahme-pipe.ps1 -View check,massnahme:<kennung>` fasst Helfer,
LIMITED-Aufgabe und Shot zusammen. Beides greift **echt** in den Rechner ein; harmlos sind
`netz.dnscache.leeren`, `systemschutz.punkt.anlegen` und `sicherheit.signaturen.aktualisieren`.

**Stufe 3 wird auf diesem Weg nicht abgenommen.** `--view check,massnahme:<kennung>` setzt bei
einer Stufe-3-Kennung das Häkchen „Ich habe verstanden, was verloren geht“ selbst und drückt
„Jetzt ausführen“; der Eingriff wäre echt (Winsock-Reset, Papierkorb, Firewall-Reset, Neustart).
Belegt werden die 10 Stufe-3-Maßnahmen an Karten und `wiederherstellungspunkt.zurueck`
ausschließlich über `--helfer --plan <datei> --trocken` (Suite-Gruppe „v8.3: Stufe 3“: je
Kennung eine `schritt`-Zeile, keine Datei, kein Kindprozess, Vorwerte unverändert) und über die
Kernproben (Vorher/Nachher-Paar je Nachweis, `Verlust`-Satz je Kennung). Den Lauf mit Wirkung
nimmt der Betreiber im Fenster ab, je Kennung einmal, Neustart-Fälle eingeschlossen; die
Prüfliste dazu steht in `docs\UEBERGABE.md`.

Was sich nur mit einem echten Klick im UAC-Dialog prüfen lässt (Start des Helfers aus der
Oberfläche, Ablehnung, die Ergänzung der Messwerte, die Tiefenprüfung mit DISM und SFC, die
Reparatur, Umstellung der Selbststart-Aufgabe, jede Maßnahme einmal live mit Nachweis und
Rückweg, das ZIP-Update Ende zu Ende), steht als Prüfliste für den Betreiber in
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

**Seit 8.3 fail closed und nur System32.** Jede Ausnahme um `WinVerifyTrust` (`wintrust.dll`
fehlt, Einstiegspunkt fehlt, jede andere) ist eine Abweichung „Signaturprüfung nicht möglich“
mit Exit 5, keine Warnung mehr; das gilt auch für `update.anwenden` (der Helfer prüft die neue
EXE mit derselben Funktion und lehnt dann ab). Damit eine neben die EXE gelegte DLL diese
Prüfung nicht vorher aushebelt, sucht jeder `DllImport` der Assembly nur in `System32`
(`[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` in
`src\AssemblyInfo.cs`; ohne das Attribut prüfte die CLR zuerst den Ordner der Assembly), und
`host\Program.cs` setzt vor allem anderen `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)`
ohne `AddDllDirectory` für Abhängigkeiten wie `cryptbase.dll` und jedes `LoadLibrary` ohne
Flags (eingetragene Nutzerordner sucht Windows VOR System32, deshalb war die Kombination
`SYSTEM32 | USER_DIRS` mit dem Programmordner wirkungslos; gemessen). `WebView2Loader.dll`
kommt weiter aus dem Programmordner, weil die WebView2-Assembly sie selbst aus ihrem Ordner
lädt. Beleg in der Suite: Prüfung „DLL-Suchpfad: System32 vor dem Programmordner“ mit
`dwmapi.dll` und `cryptbase.dll` (keine KnownDlls; `wintrust.dll` kommt aus `\KnownDlls` und
beweist deshalb keine Suchreihenfolge).

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
   git tag -a v8.3.0 -F docs\werkzeuge\tagmsg-8.3.0.txt
   git push origin v8.3.0
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
Wer per Installer installiert hat, wird per Installer aktualisiert; sonst wird das ZIP über den
Helfer getauscht, die alte Fassung bleibt als `.vorher`-Dateien liegen. Ein misslungenes Update
meldet sich mit dem Satz des Helfers.

Seit 8.1 läuft die App nicht erhöht, das Update braucht aber Schreibrecht unter
`Program Files`: beide Wege zeigen darum einen UAC-Dialog. Der Installer-Weg startet
`setup.exe` mit `Verb = runas` (der Dialog nennt `WindowsWartung-Setup.exe`); das ist seit 8.3
die einzige Stelle mit `StarteErhoeht`. Der **ZIP-Weg läuft seit 8.3 als Plan
`update.anwenden` über den Helfer** (`helfer\Massnahmen\Programm.cs`, M4-Entwurf §3.6, §10.1):

1. Der Host lädt `update.zip` nach `%LOCALAPPDATA%\WindowsWartung\update\` (fester Pfad, der
   Helfer nimmt keinen Pfad als Parameter), prüft die Prüfsumme gegen die Release-`.sha256`
   und schickt `Plan.Neu("Update").Mit("update.anwenden", "sha256", <64 Hex>)` an den Helfer
   (UAC-Dialog nennt `WindowsWartung.exe`); ist der Host selbst erhöht, läuft derselbe Plan im
   eigenen Prozess. Während des Plans nimmt der Host keinen zweiten Lauf an, und das Fenster
   lässt sich nicht schließen (Hinweis statt Rückfrage), weil das Ende der Pipe den Tausch
   mitten im Kopieren abbräche.
2. Der Helfer (`Profilgebunden`, ein fremdes Konto im Dialog → Exit 2) öffnet die Quelle genau
   einmal am Handle, kopiert sie nach `%ProgramData%\WindowsWartung\update\` (Ordner nur
   Administratoren und SYSTEM, vorhandener Inhalt wird gelöscht) und hasht dabei; weicht die
   SHA-256 vom Parameter ab → Exit 1 „Prüfsumme“, nichts geändert. Dann entpacken (Einträge mit
   `..`, absolutem Pfad oder außerhalb des Ziels → Ablehnung), im Stamm nur
   `WindowsWartung.exe`, `WebView2Loader.dll`, `Microsoft.Web.WebView2.Core.dll`,
   `Microsoft.Web.WebView2.WinForms.dll` (DLLs mit gültiger Authenticode-Kette oder dem
   Schlüssel der neuen EXE), unter `ui\` nur Pfade aus der Liste der neuen EXE; jede andere
   Datei → Exit 1, nichts kopiert.
3. Herausgeber der neuen EXE (`UpdateTrust.PruefeHerausgeber`: Name **und** Schlüssel gegen die
   laufende EXE; ein unsignierter Entwicklungsbau bindet nicht, das steht als Zeile im
   Protokoll), `FileVersionInfo` ≥ laufende Fassung (sonst „kein Rückschritt“), Startprüfung
   der neuen Fassung gegen ihre eigene eingebettete `ui-hashes.txt` (Liste in einer eigenen
   AppDomain gelesen, nie `LoadFrom`).
4. Tausch im Programmordner je Datei: Ziel → `<name>.vorher` (ein altes `.vorher` wird
   gelöscht; geht das nicht, `.vorher.<4hex>` und `MoveFileEx` zum Neustart), dann Kopie.
   Windows erlaubt das Umbenennen einer laufenden EXE, nicht das Löschen: Host und Helfer laufen
   danach aus `WindowsWartung.exe.vorher` weiter. Scheitert ein Schritt, gehen alle `.vorher`
   zurück, kopierte Dateien werden gelöscht, Exit 1 „nichts geändert“ bzw. „zurückgerollt“.
   Werte `update.version`, `update.vorher`, `nachweis.bestanden = "1"`.
5. Exit 0: der Host schreibt `pending_update.txt`, beendet den Helfer, zeigt „Das Programm
   startet gleich neu“ und beendet sich; `Program.cs` startet danach die neue EXE mit
   `--vorgaenger <eigene PID>` nicht erhöht (`UseShellExecute`, ohne `runas`; aus einem erhöhten
   Host über `Shell.OeffneImNutzerkontext`). Die neue Fassung wartet bis 30 s auf das Ende der
   alten (Einzelinstanz-Mutex, WebView2-Datenordner) und startet dann normal. Exit ≠ 0:
   `updateError` mit der letzten bad-Zeile des Helfers, kein Merker, die Update-Leiste kommt
   zurück, die alte Fassung läuft weiter.
6. Der nächste erhöhte Helferstart räumt auf (`ProgrammMassnahmen.Aufraeumen`): die Reste des
   Tauschs (`.vorher`, `.vorher.<4hex>`, `.neu.<4hex>`), und zwar nur im Stamm für
   `WindowsWartung.exe` und die drei WebView2-DLLs und unter `ui\` (der Programmordner kann ein
   gemeinsamer Ordner sein; fremde Dateien und Unterordner bleiben unberührt; was sich nicht
   löschen lässt, bleibt ohne Fehler), dazu `%ProgramData%\WindowsWartung\update\`. Ist ein
   Unterordner wie `ui\` eine Verknüpfung auf einen anderen Ort, tauscht der Helfer dort nichts
   (Exit 1, nichts geändert bzw. zurückgerollt).

Trockenlauf: `--helfer --plan` mit `update.anwenden sha256=<hex> --trocken` läuft die Schritte
1 bis 3 und schreibt je Datei nur eine „(trocken)“-Zeile; ohne `update.zip` endet er mit Exit 1
„update.zip fehlt“ (Suite-Prüfung „Update-Weg trocken“). Ende zu Ende (Download, Tausch,
Neustart mit `--vorgaenger`) ist nur mit einem echten Release > 8.3.0 prüfbar (Prüfliste in
`docs\UEBERGABE.md`).

Rückruf eines kaputten Releases: `gh release edit vX.Y.Z --prerelease`, Assets behalten,
Korrektur als neue Patch-Version. Der Updater kennt kein Downgrade.

## Laufzeitdaten beim Nutzer

Seit 8.1 liegt alles, was den PC betrifft, maschinenweit; im Profil bleiben nur die Daten der
Oberfläche. Vorhandene Dateien aus 8.0 werden beim ersten Start einmalig übernommen (kopiert,
Original auf `*.uebernommen` umbenannt).

| Was | Wo |
| --- | --- |
| Entscheidungen des Nutzers („so gewollt“) | `%ProgramData%\WindowsWartung\entscheidungen.json` |
| Protokolle je Lauf (`lauf-*.jsonl`, zwei Leser); seit 8.3 daneben `dism-<datum>.log` der DISM-API und `ip-reset-<datum>-<4hex>.log` von `netsh int ip reset` | `%ProgramData%\WindowsWartung\protokoll\` |
| Verlauf | `%ProgramData%\WindowsWartung\verlauf\history.json` (+ `.alt`) |
| Zeitplan der geplanten Wartung (seit 8.3 jede Lese- und Schreibstelle unter dem Mutex `Global\WindowsWartung.zeitplan`, 5 s; Schreiben unterbleibt bei belegter Sperre) | `%ProgramData%\WindowsWartung\zeitplan.json` |
| Sicherungen (`.reg` der Registrierungs-Suche, verborgene Updates, Werte) | `%ProgramData%\WindowsWartung\sicherungen\<art>\` |
| Sicherungsdateien der Maßnahmen (`<yyyyMMdd-HHmmss>-<kennung>-<4hex>.json` mit `kennung`, `zeit`, `parameter`, `vorher`, seit 8.2 bei HKCU-Werten `parameter.profilSid`), Rückweg über die Kennung `rueckweg` | `%ProgramData%\WindowsWartung\sicherungen\massnahmen\`: eigene, nicht vererbende DACL (SYSTEM und Administratoren Vollzugriff, Benutzer Lesen und Ausführen), Besitzer Administratoren; der Helfer setzt sie bei jedem Schreiben und (wenn der Ordner schon da ist) bei jedem Start, seit 8.3 auch der erhöht gestartete Host, und prüft beim Lesen Besitzer, DACL und Hardlink-Zahl am geöffneten Handle |
| Exporte der Maßnahmen (seit 8.3): `<datum>-firewall-<4hex>.wfw` von `netsh advfirewall export` (Rückweg über `rueckweg`, Import nur nach Herkunftsprüfung am Handle), `<datum>-treiber-<4hex>\` von `pnputil /export-driver` (kein Rückweg über das Programm); im JSON steht nur der Name, nie ein Pfad | `%ProgramData%\WindowsWartung\sicherungen\massnahmen\export\`: Unterordner mit derselben DACL und demselben Besitzer, vom Helfer relativ zum festgenagelten Ordner-Handle angelegt; nach jedem Export von `netsh`/`pnputil` setzt `ExportSchuetzen` Besitzer und DACL auf Datei bzw. Ordner, scheitert das, bricht die Maßnahme VOR dem Eingriff ab |
| App-Protokoll (Host, Helfer, geplante Wartung) | `%ProgramData%\WindowsWartung\logs\app.log` |
| Arbeitskopie des Update-Pakets (seit 8.3): `update.zip`-Kopie und `neu\` entpackt, nur Administratoren und SYSTEM | `%ProgramData%\WindowsWartung\update\` (der nächste erhöhte Helferstart leert den Ordner) |
| WebView2-Datenordner, Zoom (`zoom.txt`) | `%LOCALAPPDATA%\WindowsWartung\` |
| Update-Download (`update.zip` oder `setup.exe`) | `%LOCALAPPDATA%\WindowsWartung\update\` (wird beim nächsten Update geräumt, der Ordner selbst bleibt; seit 8.3 kein `vorher\`, kein `ww_update.cmd` mehr) |
| Merker eines laufenden Updates (`pending_update.txt`, bis zum nächsten Start) | `%LOCALAPPDATA%\WindowsWartung\` |
| Alte Fassung nach einem ZIP-Update (seit 8.3): `WindowsWartung.exe.vorher`, `WebView2Loader.dll.vorher` usw. | Programmordner, bis der nächste erhöhte Helferstart sie löscht |

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
   Erhöhung; ein fremder Ordner bleibt unangetastet, Rückgabe `false`, nie eine Ausnahme).

Die vierte Stelle aus 8.1 und 8.2, die Austausch-Batch des ZIP-Updates mit `icacls`, gibt es
seit 8.3 nicht mehr: `update.anwenden` fasst den Datenordner nicht an, der nächste Helferstart
(Stelle 2) setzt die Rechte wie bisher.

**Ausnahme `sicherungen\massnahmen` (seit 8.2, mit `export\` seit 8.3).** `Users` Ändern auf
dem ganzen Baum gilt nicht für diesen einen Ordner: der Rückweg schreibt die Werte aus einer
Sicherungsdatei erhöht zurück (Firewall aus, `EnableLUA` 0, Dienst-Startart, seit 8.3 auch
hosts-Inhalt, feste IP-Adressen, `DefaultInboundAction`, Winlogon-Werte und den Import einer
`.wfw`), also darf nur eine Datei gelten, die der erhöhte Helfer selbst angelegt hat.
`Sicherungsdatei.Schreiben` (`helfer\Massnahmen\Gemeinsam.cs`)
öffnet den Ordner einmal per Handle, prüft, wohin er wirklich führt, setzt an diesem Handle
Besitzer Administratoren und die eigene DACL (Vererbung aus; SYSTEM und Administratoren
Vollzugriff, Benutzer Lesen und Ausführen; idempotent, nur bei Abweichung) und legt die Datei
relativ zum Handle mit derselben DACL an; scheitert eines davon, wird nichts gesichert und
nichts geändert (`InvalidOperationException`). `Sicherungsdatei.Lesen` lehnt einen
Reparse-Punkt am Pfad ab, öffnet die Datei genau einmal und verlangt an diesem Handle Besitzer
Administratoren oder SYSTEM, exakt die geschützte DACL und genau einen Namen
(`GetFileInformationByHandle`, `nNumberOfLinks` = 1); der Inhalt kommt aus demselben Handle
(`HerkunftPruefen`, eine Funktion für JSON und Export). Ein nicht erhöhter Prozess kann weder
Administratoren als Besitzer eintragen noch diese DACL setzen, deshalb braucht es keinen HMAC.
Der Helfer schützt einen vorhandenen Ordner zusätzlich bei jedem Start (`OrdnerAbsichern`),
seit 8.3 ebenso der erhöht gestartete Host (`Program.cs`, nach `Ablage.RechteSichern`, Fehler nur
als Warnung); der nicht erhöhte Host bildet Pfade dorthin nur mit `Path.Combine` und
`Ablage.IstAbzweigung` und liest eine Sicherungsdatei nur lesend (`Kern.Json.Lesen`), um den
Knopf „Rückgängig“ zu zeigen (nur, wenn `profilSid` fehlt oder die eigene SID ist).

Für Exporte gilt dasselbe Muster (`ExportOrdner`, `ExportSchuetzen`, `ExportOeffnen`): den
Unterordner `export\` legt der Helfer relativ zum festgenagelten Ordner-Handle an; eine Datei
oder einen Ordner, den `netsh` bzw. `pnputil` angelegt hat, bringt `ExportSchuetzen` sofort auf
Besitzer Administratoren und exakt die geschützte DACL (scheitert das, gilt die Sicherung als
nicht angelegt, und die Maßnahme bricht VOR dem Eingriff ab); `ExportOeffnen` prüft beim
Rückweg Name (`^[0-9]{8}-[0-9]{6}-firewall-[0-9a-f]{4}\.wfw$`), Reparse-Punkt und Herkunft am
Handle und reicht den kanonischen Pfad an `netsh advfirewall import`, solange das Handle offen
ist. Der Helfer meldet `rueckweg.moeglich = "0"` für `update.zuruecksetzen`, `geraet.treiber.neu`
und `netz.winsock.zuruecksetzen` (die Datei ist Beleg, kein Rückweg); ein Verlaufseintrag mit
fremder `profilSid` bekommt keinen Knopf, und `rueckweg` lehnt eine solche Datei mit Exit 2 ab.

**ZIP-Update von 8.0:** Keine der vier Stellen greift dort beim ersten Start. 8.0 legte
`entscheidungen.json` erhöht an (Besitzer Administratoren, `Users` nur Lesen), die Batch von
8.0 kennt das `icacls` noch nicht, und der Host setzt Rechte nur auf einem Ordner, den er selbst
anlegt. Bis zum ersten Lauf mit Rechten (Ergänzen, Werkzeug, Tiefenprüfung: `RechteSichern`
im Helfer) schlägt `File.Replace` auf die Antworten fehl; die Oberfläche sagt dann „Die
Antworten gehören noch der vorigen Fassung; nach dem nächsten Schritt mit Administratorrechten
klappt es“. Der Installer-Weg hat das Problem nicht (Stelle 1).

**Abzweigungen unter ProgramData.** `Users` Ändern auf dem ganzen Baum heißt: jedes lokale
Konto darf `logs\`, `verlauf\` oder `sicherungen\` löschen und als Junction neu anlegen
(`sicherungen\massnahmen` selbst nicht mehr, sobald der Helfer es geschützt hat; der
Elternordner `sicherungen\` bleibt für Benutzer änderbar, deshalb prüft der Helfer jede
Sicherungsdatei am geöffneten Handle, siehe oben). Die
erhöhten Schreiber (Helfer, geplante Wartung, erhöhter Host) folgen einer Abzweigung nicht:
`Ablage.IstAbzweigung(pfad)` prüft jede Komponente unterhalb von `Maschinenweit()` auf
`ReparsePoint`, `Ablage.Unterordner` liefert dann `null`, der Protokoll-Konstruktor schreibt
keine Datei (Zeilen bleiben im Speicher), die Registrierungs-Sicherung legt keine `.reg` in eine
Abzweigung, `treiber.sichern` lehnt einen Zielordner mit Abzweigung im Pfad ab. Ein
fehlgeschlagenes **Löschen** der Schreibprobe schickt den Host nicht mehr in den
Ausweichordner, nur ein fehlgeschlagenes Schreiben.

Kein Wert verlässt den Rechner. Das Werkzeug funkt nirgendwohin außer für die Update-Prüfung.
