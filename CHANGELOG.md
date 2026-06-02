# Changelog

## [3.0] - 2026-06-03

### Neu
- Komplett neu als **native Windows-App** (`.exe`, C#/WinForms) statt reiner Batch
- Grafische Oberfläche mit dunklem Theme und Kategorie-Sidebar: Reparieren, Netzwerk, Aufräumen, Diagnose
- Rund 20 Wartungsaktionen mit Live-Ausgabe und Log-Export
- Optionaler Wiederherstellungspunkt vor jeder Reparatur
- Startet automatisch mit Adminrechten (UAC), eigenes App-Icon, dunkle Titelleiste
- Build komplett ohne SDK über den eingebauten `csc.exe`

### Bleibt
- `sfcscript.bat` als schlanke Vorgängerversion weiterhin enthalten

## [2.0] - 2026-06-02

### Neu
- Menü zur Auswahl des Reparaturumfangs (Komplett / nur DISM / nur SFC / + CHKDSK)
- Wählbare Aktion nach der Reparatur: nichts, Herunterfahren oder Neustart – mit eigener Verzögerung
- Automatische Adminrechte per UAC (kein Rechtsklick mehr nötig)
- Protokollierung jedes Durchlaufs im Ordner `logs\`
- Neue, farbige Oberfläche (ANSI-Truecolor, Badges, Zahlen-Chips) statt grünem Terminal-Look

### Geändert
- `DISM /CheckHealth` durch das gründlichere `ScanHealth` ersetzt
- Befehle laufen nun unabhängig durch (vorher brach die `&&`-Kette ab, sobald SFC Fehler meldete)
- Neustart (`shutdown -r`) statt festem Herunterfahren – wegen Schnellstart-Problem

## [1.0]
- Erste Version: DISM CheckHealth + RestoreHealth + SFC, danach festes Herunterfahren
