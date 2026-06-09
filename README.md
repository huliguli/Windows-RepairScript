# Windows-Wartung

Eine native Windows-Wartungs- und Reparatur-Toolbox mit moderner Oberfläche. Bündelt die Befehle, die man sonst einzeln in der Eingabeaufforderung eintippt – DISM, SFC, Netzwerk-Reset, Aufräumen, Diagnose – in einem aufgeräumten Tool mit Kategorien, Live-Ausgabe und optionalem Wiederherstellungspunkt.

Die Oberfläche ist in HTML/CSS gebaut und läuft in einem schlanken **WebView2**-Host; die eigentliche Logik steckt in C#. Kompiliert wird mit dem in Windows enthaltenen Compiler – **kein .NET SDK nötig**.

## Funktionen

**Reparieren** — Komplett-Reparatur (DISM + SFC), DISM RestoreHealth, SFC scannow & nur prüfen, WinSxS aufräumen, Komponentenspeicher analysieren, Windows-Update reparieren, CHKDSK planen

**Netzwerk** — Netzwerk-Reset (DNS / Winsock / IP), DNS-Cache leeren, IP-Adresse erneuern, Netzwerk-Diagnose (Ping/Route zu einem Ziel)

**Aufräumen** — Temp-Dateien, Windows-Update-Cache, Papierkorb, Datenträgerbereinigung

**Diagnose** — System-Übersicht, Festplatten-Gesundheit (SMART), Akkubericht, Defender-Schnellscan, RAM-Diagnose, Treiber-Backup

**Energie** — vorhandene Energiesparpläne anzeigen, den aktiven markieren und per Klick wechseln

**Wiederherstellung** — Wiederherstellungspunkt anlegen, vorhandene auflisten und (mit doppelter Bestätigung) auf einen zurücksetzen

**Geplant** — wiederkehrende, automatische Wartung per Aufgabenplanung (täglich/wöchentlich); läuft still im Hintergrund und meldet sich per Benachrichtigung

**Verlauf** — protokolliert jede Ausführung (Zeit, Aktion, Ergebnis, Dauer) zum Nachschlagen

**Autostart** — alle Startprogramme anzeigen und per Schalter an-/abschalten (umkehrbar)

Dazu:

- **Live-Fortschritt** – bei DISM und SFC zeigt ein Fortschrittsbalken den tatsächlichen Stand statt nur „arbeitet …"

- **Dashboard** – Startseite mit Live-Systemzustand (Prozessor/RAM/Festplatte), Gesundheits-Score und konkreten Empfehlungen
- **Erklärungen in einfacher Sprache** – das „?" auf jeder Kachel erklärt laienverständlich, was die Aktion macht
- Modernes, dunkles UI mit eigener Titelleiste, Glas-Effekten, weichen Animationen und Kategorie-Sidebar
- Live-Ausgabe mit Log-Export, **ein-/ausklappbar** – eingeklappt erscheinen kurze Hinweis-Toasts oben rechts
- **Warteschlange** – mehrere Aktionen nacheinander abarbeiten (z. B. Reparatur → Aufräumen → Herunterfahren)
- **Nach Fertigstellung** – wahlweise Herunterfahren oder Neustart mit Countdown (jederzeit abbrechbar)
- Optionaler **Wiederherstellungspunkt** vor jeder Reparatur
- **Auto-Update** – prüft beim Start auf neue Releases; auf Wunsch lädt die App das Update herunter, installiert es und startet sich neu
- **Windows-Benachrichtigungen** bei Abschluss/Fehler (wenn das Fenster im Hintergrund ist), Tray-Icon
- **Einstellungen** – UI-Größe/Skalierung (Barrierefreiheit), Akzentfarbe, Konsole-Verhalten, Benachrichtigungen
- Startet automatisch mit Administratorrechten (UAC)

## Aufbau

```
host/        C#-Host (rahmenloses Fenster + WebView2 + Nachrichtenbrücke)
ui/          Oberfläche in HTML/CSS/JS
src/         gemeinsame Logik (Aktionskatalog, Befehls-Runner) + App-Manifest
libs/        WebView2-DLLs (aus dem NuGet-Paket, eingecheckt – kein SDK nötig)
assets/      App-Icon
tools/       Icon-Generator
installer/   Setup-Skript (Inno Setup)
build.ps1    Build über das eingebaute csc.exe
sfcscript.bat  schlanke Batch-Version (Vorgänger)
```

## Installation

Für Endnutzer am einfachsten: unter **Releases** den **`WindowsWartung-Setup.exe`**-Installer laden und ausführen – legt einen Startmenü-Eintrag an und lässt sich sauber wieder deinstallieren. Alternativ das `WindowsWartung.zip` entpacken und `WindowsWartung.exe` starten (die Dateien daneben müssen mitkopiert bleiben).

## Bauen

Voraussetzung: Windows 10/11. Der nötige Compiler (`csc.exe`) ist Teil des .NET Framework, die WebView2-Runtime ist auf aktuellen Windows-Systemen vorinstalliert.

```powershell
.\build.ps1 -Release
```

oder einfach **`build.cmd` doppelklicken**. Ergebnis in `bin\`: `WindowsWartung.exe` plus die drei WebView2-DLLs und der `ui`-Ordner. Diese vier gehören beim Verteilen **zusammen** (das GitHub-Release packt sie als ZIP).

> Ohne `-Release` entsteht ein Dev-Build ohne Admin-Manifest – praktisch zum Ansehen der Oberfläche, echte Reparaturen brauchen aber den Release-Build (UAC).

## Nutzen

`WindowsWartung.exe` starten → UAC bestätigen → links eine Kategorie wählen → Kachel anklicken. Die Ausgabe läuft unten live mit; **Log speichern** schreibt sie in eine Textdatei.

## Hinweise

- DISM und SFC brauchen je nach System ein paar Minuten – ein Fortschrittsbalken zeigt den Stand.
- Netzwerk-Reset und RAM-Diagnose erfordern anschließend einen Neustart.
- Ausführliche SFC-Ergebnisse stehen wie immer in `C:\Windows\Logs\CBS\CBS.log`.
- Die `.exe` ist nicht signiert – beim ersten Start zeigt Windows SmartScreen ggf. „Der Computer wurde geschützt"; über *Weitere Informationen → Trotzdem ausführen* startet sie.

## Lizenz

MIT – siehe [LICENSE](LICENSE).
