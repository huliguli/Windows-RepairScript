# Windows-Wartung

Ein PC-Reparatur-System für Windows 10 und 11, gebaut für Menschen **ohne** PC-Kenntnisse
und für die, die ihnen helfen.

Beim Öffnen sieht man, wie es dem PC geht, und darunter genau eine Schaltfläche:
**PC jetzt prüfen**. Die Prüfung liest in wenigen Sekunden aus, was in diesem PC steckt und
wie es ihm geht. Sie verändert nichts. Danach steht zu jedem Punkt in Alltagssprache, was
gemessen wurde, woher der Wert kommt, ab wann er ein Problem ist und was zu tun ist.

Wer mehr will, findet hinter **Alle Werkzeuge** die Einzelaktionen, die dieses Programm
seit jeher bündelt: die Befehle, die man sonst einzeln in ein schwarzes Fenster tippt.

Die Oberfläche ist HTML/CSS und läuft in einem schlanken **WebView2**-Fenster, die Logik
steckt in C#. Das Ergebnis ist eine kleine `.exe`, die **ohne jede Laufzeit-Installation**
auf jedem Windows 10 und 11 startet.

## Der Hauptweg

**Prüfen** (verändert nichts, dauert Sekunden): Das Programm liest den PC im eigenen Prozess
über die Schnittstellen von Windows aus, ohne ein einziges PowerShell-Fenster zu starten, und
bewertet die Messwerte nach dokumentierten Grenzen:

| Bereich | Was gemessen wird |
| --- | --- |
| Sicherheit | Virenschutz (auch Fremdprogramme), Erkennungsdaten, letzter Scan, Firewall je Netz, Benutzerkontensteuerung, SmartScreen, Secure Boot, Konten ohne Kennwort |
| Zustand der Festplatten | Selbstdiagnose des Laufwerks, bei NVMe das Gesundheitsprotokoll (Reserve, Verbrauch, Medienfehler), Temperatur gegen die Grenzen des Herstellers, TRIM, Dateisystem-Zustand, Fehlermeldungen der letzten 90 Tage |
| Freier Speicherplatz | frei und gesamt je Laufwerk |
| Stabilität | Blauschirme mit Fehlercode, Stromausfälle, Programmabstürze je Programm, Hardwarefehler, alles aus dem Ereignisprotokoll der letzten 90 Tage |
| Windows-Updates | ausstehender Neustart, fehlgeschlagene Updates mit Fehlercode, Updates, die sich immer wieder installieren, Alter des letzten Sicherheitsupdates, ausgeblendete Updates |
| Geräte und Treiber | jedes Gerät mit Problemcode, eingestuft in Defekt, vorübergehend oder gewollt; Treiberversion und -datum |
| Arbeitsspeicher und Auslastung | freier Speicher über drei Messungen, Speicherfresser, Anzahl der Programme, die mit dem PC starten |
| Netzwerk | Adapter, Adresse, Router, Namensauflösung, Internet über die Windows-eigene Prüfadresse, Proxy, umgeleitete Adressen in der hosts-Datei |

**Fragen statt handeln.** Ist etwas abgeschaltet, das Absicht sein kann (ein Gerät, ein
Dienst), fragt das Programm einmal und merkt sich die Antwort. Es repariert nichts, was der
Nutzer selbst so eingerichtet hat.

**Tiefenprüfung** (auf Wunsch, 5 bis 10 Minuten): lässt Windows seine eigenen Dateien und
Bausteine durchsehen. Meldet nur, repariert nichts.

**Beheben**: gibt es nur für das, was ein Befund benennt. Vorher wird ein Sicherungspunkt
angelegt; das Programm prüft, ob er wirklich entstanden ist.

Was sich **nicht** feststellen ließ, wird auch so genannt, samt Grund (fehlende Rechte, keine
Daten). Eine Prüfung ohne Daten wird niemals als Problem ausgegeben, und niemals als „in
Ordnung“.

**Protokoll für zwei Leser.** Jeder Lauf schreibt ein Protokoll: eine Zeile in Alltagssprache
für den Nutzer, das Fachliche (Messwert, Quelle, Schwelle, Befehl, Dauer) daneben für den
Techniker. „Bericht speichern“ legt beides als Textdatei ab.

Auf einem fremden Rechner ohne Klick: `WindowsWartung.exe --aufzeichnen bild.json` nimmt das
Systembild auf (Rechnername, Benutzername, Seriennummern und Adressen werden entfernt),
`--pruefen bild.json` schreibt die Befunde daneben.

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

### Wo steckt der Platz?

Beantwortet die Frage, die nach „Ihr Speicher wird knapp“ als Nächstes kommt. Oben, was das
Programm selbst gefahrlos wegräumen kann, jede Kategorie einzeln mit ihrer Größe:
Papierkorb, temporäre Dateien, heruntergeladene Update-Dateien, Vorschaubilder,
Zwischenspeicher der Browser und der Spiele-Plattformen. Unten die größten Ordner und
Dateien in den eigenen Bereichen, die das Programm **nicht** anfasst.

Der Papierkorb ist nie vorangehakt, weil sein Inhalt danach endgültig weg ist. Der Ordner
`Downloads` wird gar nicht erst zum Aufräumen angeboten: dort landen Dateien, die man
behalten möchte. Gemeldet wird der tatsächlich gewonnene Platz, nicht der erhoffte.

### Einträge, die ins Leere zeigen

Sucht Registrierungs-Einträge, die eine Datei nennen, die es nicht mehr gibt. Der Zuschnitt
ist bewusst eng, weil eine falsche Vermutung hier Programme kaputt macht:

- Gemeldet wird **nur Nachweisbares**. Kategorien wie „unbenutzte Dateiendungen“ bleiben
  außen vor, dort wäre „verwaist“ geraten.
- Ein Programm gilt erst als verschwunden, wenn **auch der Weg zum Deinstallieren** ins
  Leere führt. Ein fehlender Installationsordner allein genügt nicht: viele Pakete tragen
  dort den Ordner ein, in den sie sich zum Installieren entpackt haben.
- Aufrufe über `msiexec`, `rundll32` und Konsorten werden übersprungen, ebenso alles auf
  Wechseldatenträgern und Netzlaufwerken.
- Vor dem Entfernen: **Sicherungspunkt** und zusätzlich eine `.reg`-Sicherungsdatei, mit
  der sich jeder Eintrag per Doppelklick zurückholen lässt. Scheitert die Sicherung, wird
  nichts verändert.
- **Nichts ist vorausgewählt**, und die Ansicht verspricht kein höheres Tempo.

## Grundsätze

- **Alltagssprache.** Jede Kachel heißt nach ihrer Aufgabe, nicht nach ihrem Werkzeug. Der
  Fachname steht klein darunter. Ein Prüfskript wacht darüber, dass das so bleibt.
- **Kein Punktestand.** Eine nicht nachrechenbare Zahl ist die Masche unseriöser
  „PC-Reiniger“. Hier steht der Zustand je Bereich in Worten, mit dem echten Messwert.
- **Keine Versprechen, die niemand einlösen kann.** Das Programm behauptet nirgends, den PC
  schneller zu machen. Wo eine Aufräum-Funktion nur Ordnung schafft, steht das auch so da.
- **Nur Nachweisbares.** Das gilt besonders für die Registrierung: gemeldet wird
  ausschließlich, was sich belegen lässt, nie eine Vermutung. Nichts ist dort
  vorausgewählt, und vor dem Entfernen steht eine Sicherung, die sich per Doppelklick
  zurückspielen lässt.
- **Rückgängig vor Rückfrage.** Vor jedem Eingriff wird ein Sicherungspunkt angelegt, auch
  bei den riskanten Aktionen. Bestätigungsdialoge gibt es nur, wo etwas unumkehrbar ist.
- **Keine erfundenen Diagnosen.** Gedeutet werden nur offiziell dokumentierte Fehlercodes
  und Meldungstexte. Unbekanntes bleibt unkommentiert.

## Aufbau

```
kern/        Datenmodell (Systembild, Befund), Regeln je Bereich, Entscheidungen, Protokoll
             hängt von nichts ab: läuft in den Proben ohne Rechte und ohne Fenster
sammler/     füllt das Systembild im eigenen Prozess (WMI, Ereignisprotokoll, Registry, COM)
host/        C#-Host: Fenster, WebView2, Nachrichtenbrücke, Hauptweg (CheckFlow), Update
ui/          Oberfläche in HTML/CSS/JS
src/         Werkzeugkasten (Aktionskatalog, Befehls-Runner), Verlauf, Signaturbindung
tests/       run-tests.ps1, Kernproben (proben/) und aufgezeichnete Testbilder (aufzeichnungen/)
tools/       Compiler-Aufruf, Kommandozeile (aufzeichnen.exe), Probenläufer
libs/        WebView2-DLLs (eingecheckt)
installer/   Setup-Skript (Inno Setup)
build.ps1    Bau über den Roslyn-Compiler des .NET SDK
DEPLOYMENT.md  bauen, signieren, veröffentlichen, aktualisieren
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

`--view` versteht `light`, `dark`, eine Ansicht (`tools`, `history`, `settings`, `storage`,
`registry` …) und `check` für einen automatisch gestarteten Prüflauf. Kommagetrennt
kombinierbar. Die beiden Suchläufe starten beim Öffnen ihrer Ansicht von selbst, deshalb
dort `--shotwait` großzügig setzen.

## Hinweise

- Die Prüfung braucht wenige Sekunden. Die Tiefenprüfung der Windows-Dateien dauert 5 bis
  10 Minuten; der PC bleibt dabei benutzbar.
- Das Programm sendet nichts nach außen. Die einzige Verbindung ins Internet ist die
  Update-Prüfung bei GitHub und, bei der Netzwerkprüfung, die Prüfadresse von Windows selbst.
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
