# Windows-Wartung

Ein Wartungs- und Reparaturwerkzeug für Windows, gebaut für Menschen **ohne** PC-Kenntnisse.

Beim Öffnen sieht man, wie es dem PC geht, und darunter genau eine Schaltfläche:
**PC jetzt prüfen**. Die Prüfung liest nur, sie verändert nichts. Danach steht in
Alltagssprache da, was gefunden wurde, was behoben wurde und was noch zu tun ist.

Wer mehr will, findet hinter **Alle Werkzeuge** die 28 Einzelaktionen, die dieses Programm
seit jeher bündelt: die Befehle, die man sonst einzeln in ein schwarzes Fenster tippt.

Die Oberfläche ist HTML/CSS und läuft in einem schlanken **WebView2**-Fenster, die Logik
steckt in C#. Das Ergebnis ist eine rund 190 KB kleine `.exe`, die **ohne jede
Laufzeit-Installation** auf jedem Windows 11 startet.

## Der Hauptweg

**Prüfen** (verändert nichts): sieben lesende Prüfungen plus die beiden Windows-eigenen
Werkzeuge für Bausteine und Systemdateien:

| Bereich | Was geprüft wird |
| --- | --- |
| Windows-Dateien | ob wichtige Dateien von Windows beschädigt sind |
| Freier Speicherplatz | ob genug Platz für Updates und zum Arbeiten bleibt |
| Sicherheit | Virenschutz, Alter der Erkennungsdaten, Firewall, Verschlüsselung |
| Zustand der Festplatten | Selbstdiagnose, Verschleiß, Betriebsstunden |
| Ordnung auf der Festplatte | ob Windows eine Festplattenprüfung vorgemerkt hat |
| Windows-Updates | ausstehender Neustart, Alter des letzten Updates |
| Stabilität | Abstürze und unerwartete Neustarts der letzten 14 Tage |
| Akku | verbliebene Kapazität gegenüber dem Neuzustand (nur bei Laptops) |

**Beheben**: legt zuerst einen Sicherungspunkt an, holt dann fehlende Windows-Bausteine
nach, ersetzt beschädigte Dateien und räumt bei Bedarf Datenmüll weg.

Was sich **nicht** feststellen ließ, wird auch so genannt. Eine Prüfung ohne Daten wird
niemals als Problem ausgegeben.

## Alle Werkzeuge

**Reparieren**: Rundum-Reparatur, Windows über das Internet reparieren, Windows-Dateien
prüfen und reparieren, nur nachsehen, alte Update-Reste löschen, nachsehen ob Aufräumen
lohnt, Windows-Update von vorn starten, Festplatte beim Neustart prüfen, Drucker wieder zum
Laufen bringen, Uhrzeit richtig stellen, Windows-Suche neu aufbauen

**Netzwerk**: alle Interneteinstellungen zurücksetzen, gemerkte Internet-Adressen
verwerfen, neue Netzwerk-Adresse vom Router holen

**Aufräumen**: temporäre Dateien, heruntergeladene Update-Dateien, Papierkorb,
Windows-Aufräumfenster, Vorschaubilder, Microsoft Store zurücksetzen

**Diagnose**: Überblick über den PC, Festplatten auf Verschleiß prüfen, Akkubericht,
kurzer Virenscan, Arbeitsspeicher prüfen, Abstürze anzeigen, Netzwerk-Daten, Startdauer

Dazu: **Sicherungspunkte** anlegen und zurücksetzen, **automatische Wartung** nach Zeitplan,
**Verlauf**, **Startprogramme** an- und abschalten, **vorinstallierte Apps** entfernen
(nur bekannte, unbedenkliche), **Energieplan** wählen.

## Grundsätze

- **Alltagssprache.** Jede Kachel heißt nach ihrer Aufgabe, nicht nach ihrem Werkzeug. Der
  Fachname steht klein darunter. Ein Prüfskript wacht darüber, dass das so bleibt.
- **Kein Punktestand.** Eine nicht nachrechenbare Zahl ist die Masche unseriöser
  „PC-Reiniger“. Hier steht der Zustand je Bereich in Worten, mit dem echten Messwert.
- **Keine Registry-Reinigung.** Microsoft unterstützt sie ausdrücklich nicht.
- **Rückgängig vor Rückfrage.** Vor jedem Eingriff wird ein Sicherungspunkt angelegt, auch
  bei den riskanten Aktionen. Bestätigungsdialoge gibt es nur, wo etwas unumkehrbar ist.
- **Keine erfundenen Diagnosen.** Gedeutet werden nur offiziell dokumentierte Fehlercodes
  und Meldungstexte. Unbekanntes bleibt unkommentiert.

## Aufbau

```
host/        C#-Host: Fenster, WebView2, Nachrichtenbrücke, Update
host/CheckFlow.cs   der Hauptweg (prüfen und beheben)
ui/          Oberfläche in HTML/CSS/JS
src/         Aktionskatalog, Prüfungen, Befehls-Runner, Protokoll
tests/       Prüfskript für Sprache, Optik und Sicherheitszusagen
libs/        WebView2-DLLs (eingecheckt)
installer/   Setup-Skript (Inno Setup)
build.ps1    Bau über den Roslyn-Compiler des .NET SDK
```

## Installation

Für Endnutzer am einfachsten: unter **Releases** die **`WindowsWartung-Setup.exe`** laden
und ausführen. Alternativ das `WindowsWartung.zip` entpacken und `WindowsWartung.exe`
starten (die Dateien daneben müssen mitkopiert bleiben).

## Bauen

Voraussetzung: Windows 10/11 und das **.NET SDK** (nur zum Bauen). Der Zielrahmen bleibt
.NET Framework 4.8, das ab Werk in Windows steckt.

```powershell
.\build.ps1 -Release
.\tests\run-tests.ps1
```

Ohne `-Release` entsteht ein Build ohne Admin-Manifest, praktisch zum Ansehen der
Oberfläche. Echte Reparaturen brauchen den Release-Build.

Der in Windows eingebaute Compiler wird bewusst nicht mehr verwendet: er beherrscht nur
C# 5 und lehnt jede höhere Sprachversion mit `CS1617` ab.

### Belegaufnahmen

```powershell
.\bin\WindowsWartung.exe --shot bild.png --view light,tools --shotwait 4000
```

`--view` versteht `light`, `dark`, eine Ansicht (`tools`, `history`, `settings` …) und
`check` für einen automatisch gestarteten Prüflauf. Kommagetrennt kombinierbar.

## Hinweise

- Die Prüfung braucht je nach System 5 bis 10 Minuten. Der PC bleibt benutzbar.
- Zurücksetzen der Interneteinstellungen und die Speicherprüfung brauchen danach einen
  Neustart.
- Programm und Installer sind signiert, allerdings mit einem selbst ausgestellten
  Zertifikat (`CN=Jonas (Windows-Wartung)`). Das macht Manipulationen erkennbar und ist die
  Grundlage dafür, dass die Selbstaktualisierung nur Fassungen desselben Herausgebers
  annimmt. Es ersetzt **kein** Zertifikat einer anerkannten Stelle: beim ersten Start zeigt
  Windows weiterhin „Der Computer wurde geschützt“; über *Weitere Informationen → Trotzdem
  ausführen* startet das Programm.
- Läuft etwas schief: Einstellungen → **Protokoll öffnen**.

## Lizenz

MIT, siehe [LICENSE](LICENSE).
