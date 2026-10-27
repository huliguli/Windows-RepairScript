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
| Sicherheit | Virenschutz (auch Fremdprogramme), Erkennungsdaten, letzter Scan, Firewall je Netz (eingehend und ausgehend), Benutzerkontensteuerung, SmartScreen, Secure Boot, Konten ohne Kennwort |
| Zustand der Festplatten | Selbstdiagnose des Laufwerks, bei NVMe das Gesundheitsprotokoll (Reserve, Verbrauch, Medienfehler), Temperatur gegen die Grenzen des Herstellers, TRIM, Dateisystem-Zustand, Fehlermeldungen der letzten 90 Tage |
| Freier Speicherplatz | frei und gesamt je Laufwerk, Größe des Papierkorbs (ab 1 GB ein Hinweis mit Maßnahme) |
| Stabilität | Blauschirme mit Fehlercode, Stromausfälle, Programmabstürze je Programm, Hardwarefehler, alles aus dem Ereignisprotokoll der letzten 90 Tage |
| Windows-Updates | ausstehender Neustart, fehlgeschlagene Updates mit Fehlercode, Updates, die sich immer wieder installieren, Alter des letzten Sicherheitsupdates, ausgeblendete Updates |
| Geräte und Treiber | jedes Gerät mit Problemcode, eingestuft in Defekt, vorübergehend oder gewollt; Treiberversion und -datum |
| Arbeitsspeicher und Auslastung | freier Speicher über drei Messungen, Speicherfresser, Anzahl der Programme, die mit dem PC starten |
| Netzwerk | Adapter, Adresse, Router, Namensauflösung, Internet über die Windows-eigene Prüfadresse, Proxy, umgeleitete Adressen in der hosts-Datei und ihr Ort |

**Fragen statt handeln.** Ist etwas abgeschaltet, das Absicht sein kann (ein Gerät, ein
Dienst), fragt das Programm einmal und merkt sich die Antwort. Es repariert nichts, was der
Nutzer selbst so eingerichtet hat.

**Tiefenprüfung** (auf Wunsch, 5 bis 10 Minuten): lässt Windows seine eigenen Dateien und
Bausteine durchsehen. Meldet nur, repariert nichts.

**Beheben**: gibt es nur für das, was ein Befund benennt. 30 Maßnahmen hängen an Karten (17
seit 8.2, 13 seit 8.3), siehe unten. Bei der Reparatur der Windows-Dateien wird vorher ein
Sicherungspunkt angelegt; das Programm prüft, ob er wirklich entstanden ist. Was sich nicht
umkehren lässt (Stufe 3), läuft nur nach einem Häkchen unter „Was verloren geht“; ein Hinweis
ohne Schaltfläche bleibt nur „Vorherigen Treiber wiederherstellen“.

Was sich **nicht** feststellen ließ, wird auch so genannt, samt Grund (fehlende Rechte, keine
Daten). Eine Prüfung ohne Daten wird niemals als Problem ausgegeben, und niemals als „in
Ordnung“. Werte, die nur mit Administratorrechten lesbar sind, stehen nach der Prüfung als
Karte „N Werte brauchen einmal Administratorrechte“ mit einer Schaltfläche; nach dem Klick
werden sie in etwa 10 Sekunden ergänzt.

**Protokoll für zwei Leser.** Jeder Lauf schreibt ein Protokoll: eine Zeile in Alltagssprache
für den Nutzer, das Fachliche (Messwert, Quelle, Schwelle, Befehl, Dauer) daneben für den
Techniker. „Bericht speichern“ legt beides als Textdatei ab.

**Für den Techniker** (seit 8.3, im Fuß des Ergebnisses und in den Einstellungen): jeder
Befund der letzten Prüfung, auch die in Ordnung, mit Messwert, Schwelle, Quelle, Einzelheiten,
Kennungen und Parametern; dazu, was nicht geprüft wurde, die Fehler beim Messen, die 10
jüngsten Protokolle und der Laufzeitordner. Ein Suchfeld filtert alle Zeilen. „Als Text
kopieren“ legt den Bericht in die Zwischenablage, „Systembild speichern“ schreibt es als Datei
(bereinigt wie `--aufzeichnen`). Die Ansicht ändert nichts.

Auf einem fremden Rechner ohne Klick: `WindowsWartung.exe --aufzeichnen bild.json` nimmt das
Systembild auf (Rechnername, Benutzername, Seriennummern und Adressen werden entfernt),
`--pruefen bild.json` schreibt die Befunde daneben.

### Maßnahmen: Beheben, Nachweis, Rückgängig

**Karte „Beheben“.** Jede Karte, deren Befund eine Maßnahme kennt, trägt die Schaltfläche
„Beheben: <Name>“ (bei zwei oder drei Maßnahmen entsprechend viele Schaltflächen). 30
Maßnahmen hängen an Karten. Seit 8.2 (Stufe 1 und 2): DNS-Cache leeren, Adresse neu vom Router
anfordern, Namensserver auf automatisch zurücksetzen, Virensignaturen aktualisieren, Schnellscan
ausführen, Firewall einschalten, SmartScreen auf „Warnen“ stellen, Benutzerkontensteuerung
wieder einschalten, TRIM einschalten, Laufwerk prüfen (nur Scan), Wiederherstellungspunkt
anlegen, Startprogramm deaktivieren, Geplante Aufgabe deaktivieren, Startart eines Dienstes
ändern, Update-Dienst wieder einschalten, Update verbergen, Gerät wieder einschalten (nur nach
der Antwort „wieder einschalten“ auf die Frage zum Gerät). Seit 8.3 dazu drei der Stufe 2
(hosts-Datei bereinigen, Eingehende Verbindungen wieder blockieren, Anmeldeprogramm
zurücksetzen) und zehn der Stufe 3: Winsock zurücksetzen, IP-Stack zurücksetzen, Firewall auf
Werkseinstellung zurücksetzen, Dateisystem kurz reparieren (Spot-Fix), Dateisystem vollständig
reparieren, Treiber neu installieren, Papierkorb leeren, Arbeitsspeicher-Test planen, Windows
Update zurücksetzen, Reparatur der Windows-Dateien. Ein Hinweis ohne Schaltfläche bleibt nur
„Vorherigen Treiber wiederherstellen“ (Geräte-Manager).

**Vorschau.** Der Klick öffnet zuerst eine Vorschau: Was passiert, Was bleibt, Risiko, dazu
Sicherung, Rückweg, Dauer, der Hinweis auf einen nötigen Neustart und die Nachfrage von
Windows. Erst „Jetzt ausführen“ startet; läuft gerade etwas anderes oder fehlt dem Befund
ein Wert, steht der Grund in der Vorschau, und es gibt nur „Schließen“.

**Stufe 3 nur mit Häkchen.** Jede Maßnahme der Stufe 3 (nicht umkehrbar) zeigt in der Vorschau
den Kasten „Was verloren geht“ mit einem Satz zu den Folgen (beim Papierkorb mit Zahl der
Dateien und GB) und das Kontrollkästchen „Ich habe verstanden, was verloren geht“. „Jetzt
ausführen“ bleibt gesperrt, bis es gesetzt ist; ohne das Häkchen lehnt das Programm den Start
ab. Die Schaltfläche an der Karte trägt ein Warnsymbol. Dasselbe gilt für „Windows auf einen
früheren Stand zurücksetzen“ in der Ansicht „Sicherungspunkte“. Keine Maßnahme der Stufe 3
läuft in der geplanten Wartung. Ein nötiger Neustart allein löst den Kasten nicht aus: braucht
eine Maßnahme einen Neustart (Winsock, IP-Stack, Speichertest, Reparatur des Windows-Laufwerks,
Update-Reset empfohlen, Benutzerkontensteuerung in Stufe 2), sagt es die Vorschau, und das
Programm startet ihn nicht selbst.

**Nachweis.** Nach der Maßnahme liest das Programm den PC erneut und prüft am neuen Bild,
ob sie gewirkt hat. Die Nachweiskarte oben im Ergebnis kennt drei Ergebnisarten: bestanden,
nicht bestanden (der alte Zustand gilt nachweislich noch) und nicht messbar (der Wert ließ
sich nicht lesen; das zählt nie als bestanden). Beim DNS-Cache belegt der Nachweis nur die
Ausführung ohne Fehler, weil Windows den Zwischenspeicher nicht im Bild führt.

**Rückgängig.** Maßnahmen, die einen Wert ändern, schreiben die alten Werte vorher in eine
Sicherungsdatei unter `C:\ProgramData\WindowsWartung\sicherungen\massnahmen`; scheitert die
Sicherung, wird nichts geändert. Nachweiskarte und Verlauf zeigen dann „Rückgängig“: nach
einer Nachfrage schreibt der Helfer die Werte zurück, misst erneut und weist auch das nach.
Die Dateien gehören dem Programm (Benutzer lesen nur); der Helfer spielt nur eine Datei
zurück, die er selbst geschrieben hat, und nur eine, die unter dem eigenen Konto entstand.
Seit 8.3 gibt es auch Sicherungen als Datei oder Ordner unter
`sicherungen\massnahmen\export`: der Firewall-Reset exportiert vorher alle Regeln (`.wfw`,
„Rückgängig“ spielt sie zurück), „Treiber neu installieren“ exportiert einen Herstellertreiber
(Zurückspielen von Hand über `pnputil /add-driver`). Ohne „Rückgängig“: der Update-Dienst wird
nie wieder abgeschaltet; Winsock-Reset, Update-Reset, Papierkorb, Dateisystem-Reparatur,
Treiber und die Reparatur der Windows-Dateien haben keinen Rückweg über das Programm, die
Vorschau sagt das; der Speichertest gilt genau für den nächsten Start und löscht sich dann von
selbst.

**Freigaben in der automatischen Wartung.** In den Einstellungen unter „Was das Programm von
selbst beheben darf“ lassen sich sechs Maßnahmen der Stufe 1 freischalten (DNS-Cache leeren,
Virensignaturen aktualisieren, Schnellscan ausführen, Firewall einschalten, Laufwerk prüfen
(nur Scan), Wiederherstellungspunkt anlegen); Voreinstellung: alle aus. Die geplante Wartung
führt eine freigegebene Maßnahme nur aus, wenn die Prüfung sie in diesem Lauf vorschlägt,
misst danach erneut und schreibt den Nachweis ins Protokoll.

## Alle Werkzeuge

27 sichtbare Einträge in vier Kategorien. Drei Einträge aus 8.1 (gemerkte Internet-Adressen
verwerfen, neue Netzwerk-Adresse vom Router holen, kurzer Virenscan) stehen seit 8.2 nicht
mehr hier, sondern als Maßnahme an der passenden Karte der Prüfung, und nur dann, wenn die
Prüfung den passenden Befund liefert: DNS-Cache leeren bei gestörter Namensauflösung, Adresse
neu anfordern bei einer Notadresse 169.254.x.x oder einem fehlenden Router-Eintrag an einem
DHCP-Anschluss, Schnellscan, wenn der letzte mehr als 14 Tage zurückliegt und in dieser Zeit
kein Vollscan lief. Im Werkzeugkasten gibt es seit 8.2 keinen Schnellscan ohne Befund mehr; in
der geplanten Wartung bleiben die Werkzeugsätze „Kurzen Virenscan starten“ und „Gemerkte
Internet-Adressen verwerfen“ wählbar und laufen dann bei jeder Wartung, ohne Befund und ohne
Nachweis. Eine Warteschlange aus einer früheren Fassung läuft damit weiter.

**Reparieren**: Rundum-Reparatur, Windows über das Internet reparieren, Windows-Dateien
prüfen und reparieren, nur nachsehen, alte Update-Reste löschen, nachsehen ob Aufräumen
lohnt, Windows-Update von vorn starten, Festplatte beim Neustart prüfen, Drucker wieder zum
Laufen bringen, Uhrzeit richtig stellen, Windows-Suche neu aufbauen

**Netzwerk**: alle Interneteinstellungen zurücksetzen, Verbindung zu einer Seite testen

**Aufräumen**: temporäre Dateien, heruntergeladene Update-Dateien, Papierkorb,
Windows-Aufräumfenster, Vorschaubilder, Microsoft Store zurücksetzen

**Diagnose**: Überblick über den PC, Festplatten auf Verschleiß prüfen, Akkubericht,
Arbeitsspeicher prüfen, Abstürze anzeigen, Netzwerk-Daten, Startdauer, Gerätetreiber sichern

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
- **Rückgängig vor Rückfrage.** Vor jedem Eingriff steht eine Sicherung: ein Sicherungspunkt
  bei den Werkzeugen und der Reparatur, auch bei den riskanten Aktionen; die gemerkten Werte in
  einer Sicherungsdatei bei den Maßnahmen, die einen Wert ändern (mit „Rückgängig“). Vor
  jeder Maßnahme steht eine Vorschau mit „Jetzt ausführen“; eine zusätzliche Rückfrage gibt es
  nur vor „Rückgängig“ und dort, wo etwas unumkehrbar ist.
- **Keine erfundenen Diagnosen.** Gedeutet werden nur offiziell dokumentierte Fehlercodes
  und Meldungstexte. Unbekanntes bleibt unkommentiert.

## Rechte, Daten, Selbstprüfung

**Start ohne Administratorrechte.** Das Programm startet ohne die Nachfrage von Windows.
Prüfen, Ansehen, Verlauf, Bericht speichern: alles läuft mit den normalen Rechten des
angemeldeten Kontos. Erst wenn etwas geändert werden soll (Tiefenprüfung, Beheben, ein
Werkzeug, Aufräumen), fragt Windows einmal nach Administratorrechten; die Erlaubnis gilt dann
für die Sitzung und verfällt nach 10 Minuten ohne Auftrag. „Nein“ im Dialog ist erlaubt: das
Programm sagt das kurz, lässt alles, wie es ist, und das vorhandene Ergebnis bleibt stehen.
Den Teil mit Rechten übernimmt ein Helfer (dieselbe Programmdatei, erhöht gestartet), der
nur einen festen Katalog von Maßnahmen kennt und von der Oberfläche keine Befehle annimmt,
nur Kennungen. Was nicht im Katalog steht, läuft nicht. Wird im Dialog das Kennwort eines
anderen Kontos eingegeben, lehnt der Helfer alles ab, was das Benutzerprofil betrifft
(Aufräumen, Apps entfernen, Werkzeuge, geplante Wartung, Papierkorb leeren, Programm-Update);
Tiefenprüfung, Reparatur und die Ergänzung der Messwerte laufen mit jedem Administratorkonto.

**Datenablage.** Alles, was den PC betrifft, liegt unter `C:\ProgramData\WindowsWartung`:

| Was | Wo |
| --- | --- |
| Protokolle je Lauf, seit 8.3 auch die DISM-Protokolle (`dism-*.log`) und das Protokoll des IP-Stack-Resets | `protokoll\` |
| Verlauf | `verlauf\history.json` |
| Zeitplan der geplanten Wartung mit den Freigaben | `zeitplan.json` |
| Sicherungen (`.reg` der Registrierungs-Suche, verborgene Updates, Werte) | `sicherungen\<art>\` |
| Sicherungsdateien der Maßnahmen (gehören dem Programm, Benutzer lesen nur) | `sicherungen\massnahmen\` |
| Exporte der Maßnahmen (Firewall-Regeln als `.wfw`, Treiberordner), gleiche Rechte | `sicherungen\massnahmen\export\` |
| Gemerkte Antworten | `entscheidungen.json` |
| App-Protokoll | `logs\app.log` |
| Arbeitskopie des Update-Pakets während des Tauschs (nur Administratoren) | `update\` |

Im Benutzerprofil (`%LOCALAPPDATA%\WindowsWartung`) bleiben die Daten der Oberfläche
(WebView2, Zoom), der Download eines Updates (`update\update.zip`) und `pending_update.txt`
bis zum nächsten Start. Nach einem Update liegt die alte Fassung als `.vorher`-Dateien im
Programmordner, bis der nächste Start mit Administratorrechten sie wegräumt. Daten aus
Version 8.0 werden beim ersten Start übernommen; nach einem ZIP-Update von 8.0 gehören die
Dateien noch der Administratorengruppe, bis zum ersten Schritt mit Administratorrechten lassen
sich Antworten dann nicht speichern (der Installer setzt die Rechte selbst). Kein Wert
verlässt den Rechner.

**Selbstprüfung beim Start.** Die Programmdatei prüft ihre eigene Signatur und jede Datei der
Oberfläche gegen eine Liste, die beim Bau eingebettet wird. Wurde eine Datei verändert,
startet die installierte (signierte) Fassung nicht und bittet um eine Neuinstallation. Ein
selbst gebauter, unsignierter Stand läuft weiter und schreibt nur eine Warnung ins Protokoll.
Seit 8.3 gilt: kann die Signatur gar nicht geprüft werden (die Windows-Bibliothek dafür fehlt
oder antwortet nicht), startet die signierte Fassung ebenfalls nicht. Die Windows-Bibliotheken,
die das Programm ruft, kommen nur aus `System32`, nie aus dem Programmordner; eine dort
abgelegte Datei gleichen Namens wird nicht geladen.

## Aufbau

```
kern/        Datenmodell (Systembild, Befund), Regeln je Bereich, Entscheidungen, Protokoll
             hängt von nichts ab: läuft in den Proben ohne Rechte und ohne Fenster
sammler/     füllt das Systembild im eigenen Prozess (WMI, Ereignisprotokoll, Registry, COM)
helfer/      der Teil mit Administratorrechten: Named Pipe, Katalog der Maßnahmen, Ausführung, Messung
host/        C#-Host: Fenster, WebView2, Nachrichtenbrücke, Hauptweg (CheckFlow), Helfer-Start, Startprüfung, Update
ui/          Oberfläche in HTML/CSS/JS
src/         Werkzeugkasten (Aktionskatalog, Schrittbaukasten, Plan-Runner), Verlauf, Signaturbindung
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
.\build.ps1
.\tests\run-tests.ps1
```

Jeder Bau bettet das Manifest (`asInvoker`) und die Prüfliste der Oberflächendateien ein;
`-Release` ändert nur die Abschlusszeile, `-Sign` signiert. Ein selbst gebauter Stand kann
alles, was die signierte Fassung kann, und fragt genauso erst bei einer Änderung nach
Administratorrechten. Einzelheiten in `DEPLOYMENT.md`.

Der in Windows eingebaute Compiler wird bewusst nicht mehr verwendet: er beherrscht nur
C# 5 und lehnt jede höhere Sprachversion mit `CS1617` ab.

### Belegaufnahmen

```powershell
.\bin\WindowsWartung.exe --shot bild.png --view light,tools --shotwait 4000
```

`--view` versteht `light`, `dark`, eine Ansicht (`tools`, `history`, `settings`, `storage`,
`registry`, `schedule`, seit 8.3 `techniker` …) und `check` für einen automatisch gestarteten
Prüflauf; seit 8.2 dazu `check,massnahme:<kennung>` (führt nach der Prüfung die erste Karte
mit dieser Maßnahme aus, über die Vorschau; bei Stufe 3 setzt es das Häkchen selbst, der
Eingriff ist dann echt) und `check,massnahme:<kennung>,rueckweg` (klickt danach
„Rückgängig“); seit 8.3 `check,techniker` (öffnet nach der Prüfung die Techniker-Ansicht).
Maßnahmen brauchen einen Helfer, also den Abnahmeweg aus `DEPLOYMENT.md`. Kommagetrennt
kombinierbar. Die beiden Suchläufe starten beim Öffnen ihrer Ansicht von selbst, deshalb
dort `--shotwait` großzügig setzen. Das Argument `--vorgaenger <pid>` setzt nur das Programm
selbst beim Neustart nach einem Update (die neue Fassung wartet bis 30 s auf das Ende der
alten).

## Hinweise

- Die Prüfung braucht wenige Sekunden. Die Tiefenprüfung der Windows-Dateien dauert 5 bis
  10 Minuten; der PC bleibt dabei benutzbar.
- Das Programm sendet nichts nach außen. Die einzige Verbindung ins Internet ist die
  Update-Prüfung bei GitHub und, bei der Netzwerkprüfung, die Prüfadresse von Windows selbst.
- Zurücksetzen der Interneteinstellungen und die Speicherprüfung brauchen danach einen
  Neustart; ebenso die Maßnahmen Winsock zurücksetzen, IP-Stack zurücksetzen,
  Arbeitsspeicher-Test planen und die Reparatur des Windows-Laufwerks (die läuft beim
  nächsten Start vor der Anmeldung). Windows Update zurücksetzen empfiehlt einen.
- Ein Update fragt einmal nach Administratorrechten, weil die Programmdateien unter
  `Program Files` liegen. Beide Wege nennen im Dialog das Programm (Installer:
  `WindowsWartung-Setup.exe`; ZIP seit 8.3: `WindowsWartung.exe`, der Helfer prüft Prüfsumme,
  Herausgeber, Version und Inhalt und tauscht die Dateien). Während des Tauschs (Sekunden)
  lässt sich das Fenster nicht schließen; danach startet das Programm von selbst neu.
  Empfohlen bleibt der Installer.
- Die Tiefenprüfung und die Reparatur rufen DISM seit 8.3 als Programmbaustein
  (`dismapi.dll`), nicht mehr als `dism.exe`; ihr Urteil hängt nicht an der Anzeigesprache.
- Programm und Installer sind signiert, allerdings mit einem selbst ausgestellten
  Zertifikat (`CN=Jonas (Windows-Wartung)`). Das macht Manipulationen erkennbar und ist die
  Grundlage dafür, dass die Selbstaktualisierung nur Fassungen desselben Herausgebers
  annimmt (gleicher Name **und** gleicher öffentlicher Schlüssel des Zertifikats; ein
  Zertifikat mit gleichem Namen, aber neuem Schlüssel wird abgelehnt). Es ersetzt **kein**
  Zertifikat einer anerkannten Stelle: beim ersten Start zeigt
  Windows weiterhin „Der Computer wurde geschützt“; über *Weitere Informationen → Trotzdem
  ausführen* startet das Programm.
- Läuft etwas schief: Einstellungen → **Protokoll öffnen**.

## Lizenz

MIT, siehe [LICENSE](LICENSE).
