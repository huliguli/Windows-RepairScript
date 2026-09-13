# Bauen, signieren, veröffentlichen, aktualisieren

Das Pendant zur `DEPLOYMENT.md` der Webprojekte, für ein Desktop-Werkzeug. Was hier steht,
ist der Weg, den jedes Release nimmt. Stand: v8.0.0.

## Aufbau

```
kern/        Datenmodell (Systembild, Befund), Regeln, Entscheidungen, Protokoll, JSON
             hängt von nichts ab: kein WinForms, kein WMI, kein Prozessstart (Test wacht darüber)
kern/Regeln/ eine Datei je Bereich, reine Funktionen über dem Systembild, Schwellen mit Quelle
sammler/     füllt das Systembild im eigenen Prozess: WMI, Ereignisprotokoll, Registry, COM, P/Invoke
             startet keinen Prozess (Test wacht darüber)
host/        Fenster, WebView2-Brücke, Hauptweg (CheckFlow), Suchläufe, Update
src/         Werkzeugkasten (Katalog, CommandRunner), Verlauf, Protokoll der App, Signaturbindung
ui/          Oberfläche (HTML/CSS/JS)
tests/       run-tests.ps1 (alle Prüfungen), proben/ (Kernproben), aufzeichnungen/ (Testbilder)
tools/       csc.ps1 (Roslyn-Aufruf), bau-kern.ps1 (Kommandozeile), lauf-proben.ps1
```

## Bauen

Voraussetzung ist nur das .NET SDK (zum Bauen). Zielrahmen bleibt .NET Framework 4.8, das in
Windows 10 und 11 ab Werk steckt; die fertige EXE braucht keine Laufzeit-Installation.

```powershell
.\build.ps1 -Release          # bin\WindowsWartung.exe mit Admin-Manifest
.\build.ps1                   # Dev-Build ohne Manifest (Oberfläche ohne UAC ansehen)
.\tools\bau-kern.ps1          # bin\aufzeichnen.exe: Sammler + Regeln ohne Oberfläche
.\tests\run-tests.ps1         # alle Prüfungen, Rückgabewert 0 = grün
```

Der eingebaute `csc.exe` kann nur C# 5 und bleibt außen vor; `tools\csc.ps1` sucht den
Roslyn-Compiler im SDK und die net48-Referenzassemblies. Neue Quelldateien unter `kern\`,
`kern\Regeln\` und `sammler\Quellen\` werden automatisch gebaut; neue Dateien unter `src\`
oder `host\` müssen in `build.ps1` eingetragen werden.

## Prüfen

`tests\run-tests.ps1` läuft lokal und in der CI. Dazu gehören seit v8:

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

Was sich nur erhöht prüfen lässt (die Tiefenprüfung mit DISM und SFC, die Reparatur), steht
in der Prüfliste für den Betreiber in `docs\UEBERGABE.md`.

Ein Systembild eines fremden Rechners aufnehmen, ohne Oberfläche:

```powershell
.\bin\WindowsWartung.exe --aufzeichnen C:\Temp\bild.json      # redigiert (Rechnername, Nutzer, Seriennummern, MAC, private IP)
.\bin\WindowsWartung.exe --pruefen C:\Temp\bild.json          # Befunde nach bild.json.befunde.txt
.\bin\aufzeichnen.exe --live                                  # dasselbe mit Konsolenausgabe
```

## Signieren

Programm und Installer werden mit `CN=Jonas (Windows-Wartung)` signiert (selbst ausgestellt,
DigiCert-Zeitstempel). Die Selbstaktualisierung nimmt nur Fassungen desselben Herausgebers an;
der Bau bricht ab, wenn der Name abweicht. PFX unter `cert\` (nie im Repo), Passwort in
`secure.md`. Repo-Secrets: `CODESIGN_PFX_BASE64` und `CODESIGN_PASSWORD`. Einzelheiten in
`SIGNING.md`.

## Veröffentlichen

Der Release-Weg ist der annotierte Tag. **Kein `gh release create`.**

1. `CHANGELOG.md`: Abschnitt `## [X.Y.Z] - Datum` pflegen (wird wörtlich zur Release-Beschreibung).
2. `src\AssemblyInfo.cs`: Version anheben (der Workflow bricht ab, wenn Tag und Version nicht passen).
3. Committen, pushen, CI abwarten (jeder Push auf `main` baut und prüft, ohne Release).
4. Trockenlauf: `gh workflow run "Build and Release" --ref main` baut alles samt Tests, Signatur
   und Installer, legt aber kein Release an. `gh run watch --exit-status` bis zum Ende.
5. Tag setzen und pushen:
   ```powershell
   git tag -a v8.0.0 -F tagmsg.txt
   git push origin v8.0.0
   gh run watch --exit-status
   ```
6. Nach dem Lauf: Prüfsummen und Signatur gegen den echten Download vergleichen
   (`Get-AuthenticodeSignature`, `Get-FileHash`), `releases/latest` zeigt kein Entwurf.

Der Workflow legt das Release als Entwurf an, hängt ZIP, Installer und beide `.sha256` an und
schaltet erst dann sichtbar. Ein Update-Klick kann so nie ein halbes Release sehen.

## Aktualisieren

Die App prüft beim Start und stündlich `releases/latest`. Ein Update wird nur installiert,
wenn Prüfsumme (`<Datei>.sha256`, über den Dateinamen zugeordnet) und Herausgeber stimmen.
Wer per Installer installiert hat, wird per Installer aktualisiert; sonst wird das ZIP getauscht,
mit Sicherung der alten Fassung und Rückweg. Ein misslungenes Update meldet sich.

Rückruf eines kaputten Releases: `gh release edit vX.Y.Z --prerelease`, Assets behalten,
Korrektur als neue Patch-Version. Der Updater kennt kein Downgrade.

## Laufzeitdaten beim Nutzer

| Was | Wo |
| --- | --- |
| Entscheidungen des Nutzers („so gewollt“), Protokolle je Lauf, Sicherungen | `%ProgramData%\WindowsWartung\` |
| App-Protokoll (`app.log`), Verlauf, Zoom, WebView2-Daten | `%LOCALAPPDATA%\WindowsWartung\` |

Kein Wert verlässt den Rechner. Das Werkzeug funkt nirgendwohin außer für die Update-Prüfung.
