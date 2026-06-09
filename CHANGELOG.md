# Changelog

## [5.9] - 2026-06-09

### Behoben
- **„CHKDSK planen" → „Zugriff verweigert" behoben.** Der Befehl ist interaktiv (Rückfrage beim System-Laufwerk) und lief bisher headless ohne Konsole, sodass die Bestätigung nie ankam. Läuft jetzt in einem eigenen, sichtbaren (elevated) Fenster, in dem die Rückfrage mit J/Y bestätigt wird – auch locale-unabhängig.

### Geändert
- **Admin-Status wird echt geprüft.** Die „Als Administrator"-Anzeige war fest verdrahtet; sie zeigt jetzt den tatsächlichen Rechtestatus und warnt rot, falls ohne Adminrechte gestartet.

## [5.8] - 2026-06-09

### Behoben
- **Setup**: weißer Rahmen um das Banner auf Willkommens-/Abschlussseite entfernt – das Bild ist jetzt flach im App-Dunkel gehalten und der Bildflächen-Hintergrund (`TBitmapImage.BackColor`) dunkel; dazu ein dezenter Logo-Schein.

## [5.7] - 2026-06-09

### Behoben
- **Setup**: das Zusammenfassungsfeld auf der Seite „Bereit zur Installation" war noch weiß – ist jetzt ebenfalls dunkel (`TRichEditViewer` mit eingefärbt).

## [5.6] - 2026-06-09

### Geändert
- **Setup im App-Look** – der Installer ist jetzt dunkel gestaltet (Akzentfarbe, helle Schrift, gebrandetes Logo-Banner) statt Standard-Windows-Wizard. (Inno-Setup-Kompilierfehler aus 5.5 behoben.)

## [5.4] - 2026-06-09

### Behoben
- **Update-Erinnerung** – „Später" bzw. das Wegklicken des Hinweises blendet ihn jetzt nur noch für die aktuelle Sitzung aus; beim nächsten Start erscheint er wieder. Vorher hat das „×" die Version dauerhaft übersprungen.

## [5.3] - 2026-06-09

### Geändert
- **Update-Download** fühlt sich jetzt wie ein echtes Update an: der Fortschrittsbalken läuft bewusst über einige Sekunden (parallel zum tatsächlichen Download), danach „Wird entpackt …" und Neustart – statt sofort durchzuspringen.

## [5.2] - 2026-06-09

### Neu
- **Oberfläche skalierbar** – in den Einstellungen lässt sich die UI-Größe von 90 % bis 175 % wählen; skaliert die komplette Oberfläche inklusive Schrift, wird gespeichert und schon vor dem Anzeigen angewandt. Hilft bei Brille oder kleiner Schrift.

## [5.1] - 2026-06-09

### Geändert
- **Optik aufpoliert** – mehr Tiefe und Material (feine Lichtkanten auf allen Flächen), ruhigerer Hintergrund mit feinem Korn, dezente Akzent-Glows auf Logo, aktiver Navigation und Primärbuttons, strafferer Typo-Satz und edlere Glas-Konsole. Keine Funktionsänderung.

## [5.0] - 2026-06-09

### Neu
- **Autostart-Manager** – zeigt alle Autostart-Programme (Registry-Run + Autostart-Ordner) und schaltet sie per Schalter an/aus, umkehrbar über Windows' eigenen Mechanismus (wie der Task-Manager)
- **Windows-Benachrichtigungen** – Mitteilung im Info-Center, wenn eine Aktion fertig ist oder fehlschlägt (während das Fenster im Hintergrund läuft); Tray-Icon (Doppelklick holt das Fenster nach vorne)
- **Einstellungen** – wählbare Akzentfarbe, Konsole beim Start offen/zu, „immer vor dem Ausführen fragen" und Benachrichtigungen an/aus

## [4.9] - 2026-06-09

### Behoben
- **Scrollen** – zu lange Inhalte lassen sich jetzt scrollen (vorher abgeschnitten) und verschwinden nicht mehr hinter der Ausgabe-Konsole
- **Fenstergröße** – das rahmenlose Fenster lässt sich an allen Kanten und Ecken größer/kleiner ziehen; kleinere Mindestgröße

## [4.8] - 2026-06-09

### Behoben (Installer)
- **Fehler 740** beim „Windows-Wartung ausführen" am Setup-Ende behoben (Start jetzt per `shellexec`, löst die UAC-Abfrage korrekt aus)
- **„Neuen Ordner anlegen"**-Button im Zielordner-Auswahldialog ergänzt

## [4.7] - 2026-06-09

### Geändert
- **Übersicht** zeigt jetzt den freien Speicher **aller fest verbauten Laufwerke** (z. B. C: und D:) statt nur des System-Laufwerks

## [4.6] - 2026-06-09

### Neu
- **Dashboard / Übersicht** – neue Startseite mit Live-Anzeigen für Prozessor, Arbeitsspeicher und Festplatte (Ring-Gauges), System-Steckbrief (Windows-Version, Gerät, Speicher, Laufzeit) sowie einem **Gesundheits-Score (0–100)** mit anklickbaren Empfehlungen
- **Erklärungen in einfacher Sprache** – „?"-Button auf jeder Kachel öffnet eine laienverständliche Erklärung (mit Warnhinweis bei riskanten Aktionen)
- **Installer** (Inno Setup) – Setup mit Startmenü-Eintrag und sauberer Deinstallation; das Release liefert jetzt zusätzlich `WindowsWartung-Setup.exe`

## [4.5] - 2026-06-03

### Behoben
- **Fenster im Vordergrund** – die App holt sich beim Start zuverlässig den Fokus (auch als elevierte/UAC-Anwendung), statt im Hintergrund zu öffnen

## [4.4] - 2026-06-03

### Behoben
- **Update-Erkennung** – die Prüfung auf neue Versionen startet jetzt erst, wenn die Oberfläche bereit ist („ready"-Handshake). Vorher konnte die „Update verfügbar"-Meldung bei kaltem Start verloren gehen, weil sie zu früh an das noch nicht geladene UI geschickt wurde.

## [4.3] - 2026-06-03

### Neu
- **In-App-Update** – „Update verfügbar"-Dialog mit *Jetzt herunterladen / Später*, Download-Fortschrittsbalken, automatischer Datei-Tausch über einen Helfer, Neustart und Erfolgsmeldung „Erfolgreich auf vX.X aktualisiert"

### Geändert
- Der Update-Hinweis lädt jetzt direkt **in der App** statt nur den Browser zu öffnen (Browser bleibt als Fallback)

## [4.2] - 2026-06-03

### Neu
- **Auto-Update** – beim Start prüft die App per GitHub-API auf ein neueres Release und blendet bei Bedarf oben einen Hinweis mit „Herunterladen" ein (inkl. „diese Version überspringen"). Ohne veröffentlichtes Release oder ohne Internet passiert nichts.
- **Code-Signing** – Signatur-Pipeline (ohne SDK): `tools\make-cert.ps1`, `sign.ps1`, `build.ps1 -Sign` und optionales Signieren im Release-Workflow (Secrets). Details in `SIGNING.md`.

## [4.1] - 2026-06-03

### Neu
- **Warteschlange** – mehrere Aktionen aneinanderreihen, umsortieren und der Reihe nach abarbeiten (z. B. Komplett-Reparatur → Temp löschen → Herunterfahren)
- **Nach Fertigstellung** – Nichts / Herunterfahren / Neustart mit eigener Verzögerung, gilt für jeden Lauf; abbrechbares Countdown-Banner

## [4.0] - 2026-06-03

### Neu
- Komplett neue Oberfläche auf **WebView2**-Basis: UI in HTML/CSS/JS, Logik in C#
- Eigene Titelleiste, runde Fensterecken, Glas-Effekte, weiche Animationen, SVG-Icons, Toggle-Switch, schlanke Scrollbalken
- Eigener Bestätigungsdialog statt Windows-MessageBox
- Baut weiterhin **ohne SDK** (csc.exe + eingecheckte WebView2-DLLs)

### Geändert
- Backend (Aktionskatalog, Befehls-Runner) wiederverwendet; Ausgabe geht jetzt als Nachrichten ans UI
- Auslieferung als ZIP (Exe + WebView2-DLLs + `ui`-Ordner) statt Einzel-Exe
- GitHub-Release-Workflow baut Release und lädt das ZIP hoch

### Entfernt
- Alte WinForms-Oberfläche (durch das WebView2-UI ersetzt)

## [3.0] - 2026-06-03

### Neu
- Komplett neu als **native Windows-App** (`.exe`, C#/WinForms) statt reiner Batch
- Grafische Oberfläche mit dunklem Theme und Kategorie-Sidebar: Reparieren, Netzwerk, Aufräumen, Diagnose
- Rund 20 Wartungsaktionen mit Live-Ausgabe und Log-Export
- Dezente, ein-/ausklappbare Ausgabe-Konsole; bei eingeklappter Konsole sanft eingeblendete Hinweis-Widgets (Toasts) oben rechts
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
