# Changelog

## [6.6] - 2026-07-13

### Neu
- **Fehlermeldungen, die weiterhelfen.** Schlägt ein Schritt fehl, erklärt die Ausgabe jetzt bekannte Fehler in einfacher Sprache und nennt einen konkreten Lösungsweg – z. B. Zugriff verweigert (Virenschutz/Datei in Benutzung), deaktivierter Windows-Dienst (mit Weg über services.msc) oder die DISM-Quellfehler 0x800F081F/0x800F0906/0x800F0954 (Internet prüfen → Windows-Update reparieren → erneut). Gedeutet werden **ausschließlich offiziell dokumentierte** Windows-Fehlercodes – unbekannte Codes bleiben bewusst unkommentiert, statt etwas zu erfinden.
- **SFC/DISM-Ergebnisse verständlich zusammengefasst.** Nach dem Lauf erscheint eine Klartext-Zeile („Verständlich gesagt: …") auf Basis der offiziellen Meldungstexte: alles in Ordnung / repariert (mit Neustart-Empfehlung) / teilweise nicht reparierbar – inklusive des offiziell empfohlenen nächsten Schritts (erst DISM RestoreHealth, dann SFC erneut).
- **Erklärboxen in den Werkzeug-Kategorien.** Reparieren, Netzwerk, Aufräumen und Diagnose starten jetzt mit einer kurzen Laien-Einordnung (was passiert hier, was ist sicher, sinnvolle Reihenfolge).

### Behoben
- **Exit-Code 3010 wird korrekt als Erfolg gewertet.** 3010 bedeutet offiziell „erfolgreich, Neustart erforderlich" – bisher wurde das fälschlich als Fehler angezeigt.
- Negative Fehlercodes werden zusätzlich als Hex-Code angezeigt (z. B. 0x800F081F) – so, wie man sie auch im Netz nachschlagen würde.
- Beim übersprungenen Wiederherstellungspunkt erklärt die Meldung jetzt den Grund (Windows legt standardmäßig höchstens einen Punkt pro 24 Stunden an).
- Der Update-Fehlerdialog nennt die häufigste Ursache (Internetverbindung) statt nur des technischen Fehlertexts.

## [6.5.1] - 2026-07-13

### Behoben
- **Geplante Wartung läuft jetzt auch bei geöffneter App.** Bisher wurde der Termin übersprungen, wenn die App gerade offen war. Jetzt übergibt der Hintergrund-Lauf die Wartung an die offene App: sie wird dort **sichtbar mit Live-Ausgabe** ausgeführt und wie gewohnt im Verlauf protokolliert. Läuft gerade eine andere Aktion, startet die Wartung automatisch, sobald diese fertig ist – zwei gleichzeitige Reparatur-Läufe (DISM/SFC vertragen das nicht) bleiben damit weiterhin ausgeschlossen. Antwortet die offene App nicht (z. B. noch eine ältere Version), läuft die Wartung wie bisher still im Hintergrund – ein Termin fällt nie mehr ersatzlos aus.

## [6.5] - 2026-07-11

### Neu
- **Updates automatisch installieren** – neuer Schalter in den Einstellungen: Wird beim Start eine neue Version gefunden, lädt und installiert sie sich ohne Nachfrage, danach startet das Programm einmal neu. Die Prüfsummen-Kontrolle des Downloads bleibt dabei unverändert aktiv. Standardmäßig ausgeschaltet – wer weiterhin gefragt werden möchte, muss nichts tun.

### Behoben
- **Update-Hinweis ohne Download-Paket** – direkt nach Erscheinen einer neuen Version konnte der Update-Dialog ins Leere laufen („Kein Download-Paket im Release gefunden"), weil das Release schon sichtbar war, während die Dateien noch gebaut wurden. Ein Release wird jetzt erst veröffentlicht, wenn alle Dateien vollständig angehängt sind; zusätzlich überspringt die App ein unvollständiges Release still und prüft beim nächsten Start erneut.

## [6.4] - 2026-06-22

### Neu
- **Autostart ausgebaut** – die Autostart-Ansicht kann jetzt mehr:
  - **Windows-Wartung selbst beim PC-Start starten**: ein Schalter richtet den Selbststart über die Windows-Aufgabenplanung ein (mit Administratorrechten, ohne UAC-Nachfrage – der normale Autostart-Weg blockiert Admin-Programme still).
  - **Eigene Programme hinzufügen**: Buttons öffnen den Autostart-Ordner (für den Benutzer oder alle Benutzer) direkt im Explorer, dazu eine Schritt-für-Schritt-Erklärung in einfacher Sprache (Verknüpfung erstellen → in den Ordner verschieben).
- **8 neue Aktionen** in den Werkzeug-Kategorien:
  - *Reparieren:* **Drucker reparieren** (hängende Druckaufträge leeren + Warteschlange neu starten), **Uhrzeit synchronisieren** (behebt Zertifikats-/Anmeldefehler), **Windows-Suche reparieren** (Suchindex zurücksetzen, Neuaufbau im Hintergrund).
  - *Diagnose:* **Absturz-Historie** (unerwartete Neustarts/Bluescreens aus dem Ereignisprotokoll, verständlich beschriftet), **Netzwerk-Übersicht** (IP, Gateway, DNS je Adapter), **Startzeit-Analyse** (Dauer der letzten Windows-Starts).
  - *Aufräumen:* **Miniaturansichten-Cache leeren** (behebt falsche Vorschaubilder), **Store-Cache leeren** (wsreset).

## [6.3] - 2026-06-22

### Neu
- **Geplante Wartung: Aufgaben frei wählbar** – in „Geplant" lässt sich jetzt einstellen, **was** der automatische Lauf erledigt: 8 stille, ungefährliche Aufgaben zur Auswahl (DISM, SFC, Temp, Papierkorb, WinSxS, Update-Cache, DNS-Cache, Defender-Schnellscan). Solange nichts geändert wird, gilt weiter der bewährte Standard-Satz; „Auf Standard zurücksetzen" ist ein Klick. Der Status zeigt die gewählten Aufgaben an.
- **Flexiblere Zeitpläne** – neben täglich/wöchentlich jetzt auch **monatlich** (Tag 1–31, mit Hinweis bei 29–31) und bei „wöchentlich" **mehrere Wochentage** auf einmal (z. B. Mo + Mi + Fr) über anklickbare Tages-Chips.

### Sicherheit
- Alle neuen Eingaben (Wochentage, Monatstag, Aufgaben) werden ausschließlich über **feste Whitelists** übernommen – in die Aufgabenplanung (`schtasks`) gelangen nie Roh-Eingaben.

## [6.2] - 2026-06-21

### Neu
- **Lebendiges Dashboard** – beim Öffnen der Übersicht zählen die Werte für Prozessor, Arbeitsspeicher, Festplatte und der Gesundheits-Score nun weich von 0 auf ihren Stand hoch (synchron zur Ring-Füllung), und die Kacheln blenden dezent gestaffelt ein. Reine `transform`/`opacity`-Animation, **ohne zusätzliche Bibliothek** – leichtgewichtig auch auf älteren PCs.

### Barrierefreiheit
- **„Bewegung reduzieren" wird respektiert** – ist die gleichnamige Windows-Einstellung aktiv, verzichtet die App jetzt durchgängig auf Animationen und zeigt Inhalte sofort im Endzustand. Das gilt auch für alle bisherigen Effekte (Kacheln, Hinweise, Dialoge, Fortschritt).

## [6.1] - 2026-06-09

### Neu
- **Bloatware-Entferner** – neue Sidebar-Ansicht „Bloatware": listet vorinstallierte Apps (`Get-AppxPackage`), die viele nicht brauchen, gruppiert nach Kategorie. Mehrere auf einmal auswählen und – nach **doppelter** Sicherheitsabfrage – entfernen (`Remove-AppxPackage`). Auf Wunsch wird vorher ein **Wiederherstellungspunkt** angelegt; jeder Lauf landet im Verlauf.

### Sicherheit
- **Strikte Whitelist statt Holzhammer**: Es werden ausschließlich als unbedenklich bekannte Apps (Solitaire, Bing-News/Wetter, Xbox-Apps, Clipchamp, Teams-Consumer, 3D-Viewer, Cortana, OEM-Spiele u. a.) zur Auswahl angeboten. Eine zusätzliche **Blockliste** schützt System-, Shell-, Store-, Runtime- und Defender-Pakete hart – auch gegen versehentliche Katalog-Einträge (Defense-in-Depth).
- **Keine Befehls-Injektion**: Der `PackageFullName` wird vor jeder Verwendung streng auf erlaubte Zeichen `[A-Za-z0-9._-]` geprüft und nur in einfachen Anführungszeichen an PowerShell übergeben; entfernt wird ausschließlich, was die Whitelist erlaubt.

## [6.0] - 2026-06-09

### Neu
- **Reparatur-Verlauf** – jede Ausführung (Zeit, Aktion, Ergebnis, Dauer) wird protokolliert; neue Sidebar-Ansicht „Verlauf" zeigt die letzten Läufe (mit „Leeren"). Gespeichert unter `%LOCALAPPDATA%\WindowsWartung\history.json`.
- **Wiederherstellungspunkt-Verwaltung** – eigene Ansicht: Punkt anlegen (mit Beschreibung), vorhandene auflisten und – nach **doppelter** Sicherheitsabfrage – auf einen Punkt zurücksetzen.
- **Geplante Wartung** – richtet über die Windows-Aufgabenplanung (schtasks, mit höchsten Rechten) einen wiederkehrenden Wartungslauf ein (täglich/wöchentlich + Uhrzeit). Der neue **`--auto`-Modus** führt einen gründlichen, ungefährlichen Satz (DISM RestoreHealth + SFC + Temp + Papierkorb) still im Hintergrund aus und meldet sich per Windows-Benachrichtigung; ist die App gerade geöffnet, wird der Termin ausgelassen.
- **Energieplan-Umschalter** – neue Ansicht „Energie": zeigt die vorhandenen Energiesparpläne, markiert den aktiven und wechselt per Klick (powercfg).
- **Netzwerk-Diagnose** – Ping und Route (tracert) zu einem frei eingebbaren Ziel; die Eingabe wird streng geprüft und ohne Shell ausgeführt (keine Befehls-Injektion).
- **Treiber-Backup** – exportiert alle installierten Treiber in einen über den System-Dialog wählbaren Ordner (pnputil `/export-driver`).
- **Live-Fortschritt für DISM/SFC** – der Prozentwert wird laufend aus der (mit `\r` aktualisierten) Ausgabe gelesen und als Fortschrittsbalken angezeigt, statt dass die App „eingefroren" wirkt.

### Geändert
- Der Screenshot-/Vorschaumodus nutzt einen eigenen WebView2-Datenordner und stört so eine bereits laufende Instanz nicht mehr.

## [5.10] - 2026-06-09

### Sicherheit
- **Update-Download wird per SHA-256 geprüft.** Das Release liefert eine Prüfsumme (`WindowsWartung.zip.sha256`); die App vergleicht den heruntergeladenen Inhalt damit und bricht bei Abweichung ab – bevor mit Adminrechten getauscht wird.

### Geändert
- **Erfolg/Fehler ehrlicher**: Best-effort-Schritte (Dienste stoppen/starten, catroot2 umbenennen …) werten einen erwartbaren Fehlercode nicht mehr als Gesamtfehler – die Windows-Update-Reparatur läuft dadurch robuster durch.
- **Aufräumen meldet Konkretes**: Temp & Update-Cache zeigen die freigegebenen MB, der Papierkorb erkennt „bereits leer".
- **Defender-Schnellscan** mit Fallback: klare Meldung, wenn Defender nicht verfügbar ist (z. B. anderes Antivirus aktiv).

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
