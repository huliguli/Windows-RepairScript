# Changelog

## [8.3.0] - 2026-10-27

Auch die Eingriffe, die sich nicht rückgängig machen lassen (Stufe 3), laufen jetzt an der
Karte: mit Vorschau, einem Kasten „Was verloren geht“ und einem Häkchen, ohne das nichts
startet. Dazu eine Ansicht für den Techniker, das Programm-Update ohne Batch, und DISM über die
Windows-Schnittstelle statt über Konsolentext.

### Neu

- **13 weitere Maßnahmen an der Karte, darunter 10 der Stufe 3.** Was bis 8.2 nur ein Hinweis
  war, hat jetzt eine Schaltfläche: hosts-Datei bereinigen, Winsock zurücksetzen, IP-Stack
  zurücksetzen, Eingehende Verbindungen wieder blockieren, Firewall auf Werkseinstellung
  zurücksetzen, Dateisystem kurz reparieren (Spot-Fix), Dateisystem vollständig reparieren,
  Treiber neu installieren, Anmeldeprogramm zurücksetzen, Papierkorb leeren,
  Arbeitsspeicher-Test planen, Windows Update zurücksetzen und die Reparatur der
  Windows-Dateien (DISM und SFC). Damit hängen 30 Maßnahmen an Karten. Drei davon sind Stufe 2
  mit „Rückgängig“ (hosts-Datei, Eingehende Verbindungen, Anmeldeprogramm); die übrigen 10 sind
  Stufe 3. Als Hinweis ohne Schaltfläche bleibt nur „Vorherigen Treiber wiederherstellen“
  (Geräte-Manager).
- **Stufe 3 nur mit Häkchen.** Bei jeder Maßnahme der Stufe 3 (nicht umkehrbar) zeigt die
  Vorschau unter dem Risiko den Kasten „Was verloren geht“ mit einem Satz, der die Folgen nennt
  (beim Papierkorb mit Zahl der Dateien und GB), und das Kontrollkästchen „Ich habe verstanden,
  was verloren geht“. „Jetzt ausführen“ bleibt gesperrt, bis es gesetzt ist; ohne das Häkchen
  lehnt das Programm den Start ab. Die Schaltfläche an der Karte trägt ein Warnsymbol. Ein
  nötiger Neustart allein löst den Kasten nicht aus („Benutzerkontensteuerung wieder
  einschalten“ ist Stufe 2 mit Neustart und bleibt ohne Kasten); braucht eine Maßnahme einen
  Neustart, sagt die Vorschau „Danach ist ein Neustart nötig; das Programm startet ihn nicht
  selbst.“ Dasselbe Häkchen verlangt jetzt
  auch „Windows auf einen früheren Stand zurücksetzen“ in der Ansicht „Sicherungspunkte“.
  Keine Maßnahme der Stufe 3 läuft in der geplanten Wartung; freischaltbar bleiben die sechs
  Maßnahmen der Stufe 1 aus 8.2.
- **Sicherung als Datei.** „Firewall auf Werkseinstellung zurücksetzen“ exportiert vorher alle
  Regeln in eine Datei (`.wfw`), „Rückgängig“ spielt sie zurück. „Treiber neu installieren“
  exportiert einen Herstellertreiber vorher in einen Ordner (Windows-eigene Treiber brauchen
  keinen Export). Beides liegt unter `C:\ProgramData\WindowsWartung\sicherungen\massnahmen\export`
  und gehört dem Programm wie die Sicherungsdateien. „Windows Update zurücksetzen“ sichert die
  verborgenen Updates vorher und verbirgt sie danach wieder; „Winsock zurücksetzen“ legt den
  alten Katalog als Beleg ab. Wo es keinen Rückweg gibt, sagt es die Vorschau.
- **Ansicht „Für den Techniker“.** Im Fuß des Ergebnisses und in den Einstellungen („Messwerte
  der letzten Prüfung“). Sie zeigt jeden Befund der letzten Prüfung, auch die in Ordnung, mit
  Messwert, Schwelle, Quelle, Einzelheiten, Kennungen und Parametern, dazu, was nicht geprüft
  wurde, die Fehler beim Messen, die 10 jüngsten Protokolle und den Laufzeitordner; ein
  Suchfeld filtert alle Zeilen. „Als Text kopieren“ legt den Bericht in die Zwischenablage,
  „Systembild speichern“ schreibt es als Datei (Rechnername, Benutzername, Seriennummern und
  Adressen entfernt, wie bei `--aufzeichnen`). Nichts wird geändert.
- **Programm-Update ohne Batch.** Beim ZIP-Update prüft der Helfer das Paket selbst
  (Prüfsumme, Herausgeber mit Name und Schlüssel, Version nie kleiner, Startprüfung der neuen
  Fassung gegen ihre eigene Liste, nur die erwarteten Dateien) und tauscht dann die Dateien.
  Der Dialog von Windows nennt dabei `WindowsWartung.exe`, nicht mehr `cmd.exe`. Danach
  startet das Programm von selbst neu, ohne Administratorrechte. Scheitert ein Schritt, wird
  nichts getauscht, und der Dialog nennt den Grund. Der Installer-Weg ist unverändert.
- **DISM über die Windows-Schnittstelle.** Tiefenprüfung und Reparatur rufen DISM als
  Programmbaustein (`dismapi.dll`) statt `dism.exe` und lesen das Ergebnis als Wert (in
  Ordnung, reparierbar, nicht reparierbar, Fehler) statt aus dem Konsolentext. Das Urteil hängt
  damit nicht mehr an der Anzeigesprache. SFC bleibt ein Prozess; sein Ergebnis kommt aus den
  `[SR]`-Zeilen der `CBS.log`. Die Reparatur meldet jetzt getrennt, was DISM und was SFC
  fanden, und ob ein Neustart nötig ist.
- **Papierkorb als Befund.** Liegen 1 GB oder mehr im Papierkorb, meldet „Freier
  Speicherplatz“ das mit Zahl der Dateien und Größe und bietet „Papierkorb leeren“ an
  (Stufe 3, keine Umkehr); darunter steht nur die Zahl. Gezählt wird der Papierkorb des
  angemeldeten Kontos.
- **Firewall: ausgehende Verbindungen.** Blockiert ein Profil alles nach draußen, ist das ein
  Befund mit „Firewall auf Werkseinstellung zurücksetzen“; die Karte „an“ sagt „und lässt
  ausgehende Verbindungen zu“ nur, wenn das in allen 3 Profilen so ist. Ist eine fremde
  Firewall registriert, gibt es das Zurücksetzen nur als Hinweis.
- **Netzwerk: gestörter Stack.** Antworten Router und Namensauflösung, aber weder die
  Testseite von Windows noch TCP 443, ohne dass ein Proxy eingetragen ist, heißt die Karte
  „Netzwerk-Stack von Windows gestört“ und bietet erst „Winsock zurücksetzen“ und dann „IP-Stack
  zurücksetzen“ an (beide mit Neustart).
- **Windows Update: 26 gedeutete Fehlercodes statt 15.** Bei 12 Codes (etwa 0x80070002,
  0x80244022, 0x80070490) bietet die Karte „Windows Update zurücksetzen“ an, bei 6 Codes des
  Komponentenspeichers (etwa 0x800F081F, 0x80073712) die Reparatur der Windows-Dateien.
  Schlagen 2 oder mehr verschiedene Updates wiederholt fehl, gibt es dafür eine Sammelkarte mit
  „Windows Update zurücksetzen“. Ein Programmabsturz mit einem Windows-Baustein
  (`System32`, `SysWOW64`) bietet die Reparatur der Windows-Dateien an.

### Behoben

- **„Firewall einschalten“ neben einer fremden Firewall.** Ist ein anderes Firewall-Produkt
  bei Windows registriert, lehnt der Helfer das Einschalten der Windows-Firewall ab und nennt
  das Produkt; bis 8.2 hätte er sie daneben eingeschaltet, was nicht hilft. Prüfung und
  Maßnahme erkennen die Windows-eigene Firewall an derselben Namensliste.
- **Geplante Aufgaben mit einer Microsoft-signierten Startdatei zählen jetzt als
  Microsoft-Aufgabe,** auch wenn Name oder Autor es nicht sagen; bis 8.2 entschieden nur Pfad
  und Autor, und eine Windows-Aufgabe mit fremdem Namen zählte als fremder Autostart. Pfad
  (`\Microsoft\`) und Autor bleiben als zweites Kriterium; eine Aufgabe ohne lesbare Startdatei
  wird weiter danach beurteilt.
- **Ein Gerät ohne lesbaren Problemcode ist kein Freispruch mehr.** Liefert Windows für ein
  Gerät keinen Code, steht es als eigene Karte „Problemcode nicht lesbar“ ohne Schaltfläche, und
  der Sammelsatz „Alle Geräte laufen ohne Fehlercode“ entfällt; bis 8.2 zählte ein fehlender
  Wert als 0.
- **„Rückgängig“ nur für eigene Sicherungen.** Eine Sicherung, die unter einem anderen Konto
  angelegt wurde (Kennwort eines anderen Administrators im Dialog), zeigt im Verlauf und auf
  der Nachweiskarte keine Schaltfläche mehr; bis 8.2 erschien sie und der Helfer lehnte erst
  nach dem Dialog ab.
- **Zeitbudget einer bestehenden Wartungsaufgabe.** Eine Aufgabe aus 8.1 mit 2 Stunden wird
  beim nächsten Start des Helfers auf 4 Stunden angehoben, sofern sie auf dieses Programm
  zeigt; jeder andere Wert bleibt.
- **Der Zeitplan mit den Freigaben wird nicht mehr von zwei Seiten gleichzeitig geschrieben.**
  Programm, Helfer und die geplante Wartung teilen sich eine Sperre (5 s Wartezeit); ist sie
  belegt, wird nichts geschrieben, und das Protokoll sagt es. „Zeitplan löschen“ meldet, wenn
  die gespeicherte Auswahl wegen der Sperre stehen blieb.
- **Ort der hosts-Datei.** Zeigt Windows für die hosts-Datei auf einen anderen Ordner als den
  Standard, bereinigt das Programm nichts und sagt den Grund; die Prüfung liest den Ort jetzt
  mit. Bis 8.2 galt immer der Standardort.
- **Startprüfung ohne Signaturprüfung startet nicht.** Kann die Signatur der Programmdatei
  nicht geprüft werden (die Windows-Bibliothek fehlt oder antwortet nicht), gilt die signierte
  Fassung als verändert und startet nicht (Exit 5); bis 8.2 lief sie mit Warnung weiter. Die
  Windows-Bibliotheken, die das Programm ruft, kommen jetzt nur noch aus `System32`, nie aus
  dem Programmordner (eine dort abgelegte Datei gleichen Namens wird nicht geladen).
- **Zurücksetzen auf einen Sicherungspunkt ohne PowerShell.** Der Helfer ruft Windows dafür
  direkt (WMI `SystemRestore.Restore`) und startet dann neu; es entsteht kein
  PowerShell-Fenster mehr.
- **Der Sicherungsordner der Maßnahmen wird auch beim erhöhten Start des Programms
  geschützt,** nicht nur beim Start des Helfers.
- **„Kein Wiederherstellungspunkt“, obwohl Windows 11 welche hatte.** Die
  Zeitpunktwiederherstellung legt eigene Punkte an, die die klassische Liste nicht zeigt. Das
  Programm zählt sie jetzt mit, sofern die zugehörige Windows-Aufgabe vorhanden ist, und meldet
  dann einen Rückweg statt einer Warnung.
- **Verworfene Wiederherstellungspunkte werden sichtbar.** Hat Windows in den letzten 30 Tagen
  alle Punkte eines Laufwerks gelöscht, weil der Speicher dafür nicht reichte, erscheint eine
  Warnung mit Laufwerk und Zeitpunkt; wie oft der älteste Punkt verdrängt wurde, steht im Detail.
- **„Keine Netzwerkverbindung“ nach einem kurzen Abriss.** Findet die Prüfung keinen verbundenen
  Anschluss, misst sie bis zu 10 Sekunden nach. Steht die Verbindung dann, heißt es „Verbindung
  war kurz unterbrochen“ statt eines Fehlers.
- **Temporäre Dateien: nur noch ältere als einen Tag.** Werkzeugkasten und geplante Wartung
  löschten bisher alles in den Temp-Ordnern, auch Arbeitsdateien laufender Programme und
  Installationen. Jetzt bleiben jüngere Dateien liegen, und Abzweigungen werden nicht betreten.
- **Programmordner, den jedes Konto ändern darf.** Liegt die Windows-Wartung in einem Ordner, in
  dem Konten ohne Administratorrechte Dateien ändern dürfen (etwa auf einem zweiten Laufwerk),
  meldet die Prüfung das, denn die Datei startet auch mit Administratorrechten. Das
  Installationsprogramm bietet deshalb keine freie Ordnerwahl mehr an und entfernt beim
  Deinstallieren die geplanten Aufgaben der App.

### Gut zu wissen

- **Stufe 3 läuft nie von selbst.** Nicht in der geplanten Wartung, nicht ohne Vorschau, nicht
  ohne Häkchen; läuft gerade eine Prüfung, eine Wartung oder ein Update, bietet die Vorschau nur
  „Schließen“.
- **Winsock zurücksetzen entfernt Zusatzkomponenten von Drittprogrammen** (VPN- und
  Schutzprogramme, die sich in den Netzwerkstapel einhängen); die arbeiten danach nicht mehr,
  bis sie neu installiert sind. Der IP-Stack-Reset setzt feste IP-Adressen, Gateways und
  DNS-Einträge auf Automatisch; „Rückgängig“ schreibt sie je Anschluss aus der Sicherung zurück.
  Beides braucht einen Neustart.
- **Windows Update zurücksetzen leert den Update-Verlauf** in den Windows-Einstellungen
  (0 Einträge), laufende Downloads beginnen von vorn; verborgene Updates werden vorher
  gesichert und danach wieder verborgen, keines geht verloren. Ist der Update-Dienst
  deaktiviert, läuft die Maßnahme nicht (erst „Update-Dienst wieder einschalten“). Neustart
  empfohlen.
- **Papierkorb leeren gilt nur für das eigene Konto.** Wird im Dialog das Kennwort eines
  anderen Kontos eingegeben, lehnt der Helfer ab; es gibt keinen Rückweg, die Vorschau nennt
  Zahl und Größe.
- **Treiber neu installieren:** das Gerät ist 5 bis 30 Sekunden weg (Bild, Netz, Ton); ein
  Herstellertreiber liegt vorher als Export unter `sicherungen\massnahmen\export`, ein
  Zurückspielen geht über `pnputil /add-driver` von Hand. „Vorheriger Treiber“ bleibt ein
  Hinweis auf den Geräte-Manager.
- **Dateisystem reparieren auf dem Windows-Laufwerk** läuft erst beim nächsten Start (vor der
  Anmeldung); auf einem anderen Laufwerk sofort, das Laufwerk ist dabei 5 bis 60 Minuten nicht
  benutzbar. Dateien in beschädigten Bereichen können nach `found.000` wandern oder verloren
  gehen.
- **Der Arbeitsspeicher-Test gilt genau für den nächsten Start** (10 bis 30 Minuten, der PC ist
  solange nicht benutzbar); danach startet Windows normal, der Eintrag löscht sich von selbst.
- **Nach dem Programm-Update liegen `.vorher`-Dateien im Programmordner** (die alte Fassung)
  bis zum nächsten Start mit Administratorrechten; dann räumt der Helfer sie weg. Während des
  Dateitauschs (Sekunden) lässt sich das Fenster nicht schließen; danach startet das Programm
  von selbst neu (Argument `--vorgaenger`, nur für diesen Neustart).
- **Die Werkzeuge im Werkzeugkasten bleiben.** „Windows-Dateien prüfen und reparieren“, „Alle
  Interneteinstellungen zurücksetzen“, „Papierkorb endgültig leeren“ und „Arbeitsspeicher
  beim Neustart prüfen“ laufen dort weiter ohne Befund; die Maßnahmen an der Karte erscheinen nur mit
  passendem Befund. Es sind weiter 27 sichtbare Einträge.

## [8.2.0] - 2026-09-18

Was die Prüfung findet, lässt sich jetzt an Ort und Stelle beheben: mit einer Vorschau vor dem
Klick, einem Nachweis nach dem Lauf und „Rückgängig“ über die angelegte Sicherung. Nichts läuft
ohne Ihren Klick, außer Sie geben es für die geplante Wartung ausdrücklich frei.

### Neu

- **„Beheben“ direkt an der Karte.** Jede Karte, deren Befund eine Maßnahme kennt, trägt die
  Schaltfläche „Beheben: <Name der Maßnahme>“ (bei zwei Maßnahmen zwei Schaltflächen). 17
  Maßnahmen hängen in dieser Fassung an Karten: DNS-Cache leeren, Adresse neu vom Router
  anfordern, Namensserver auf automatisch zurücksetzen, Virensignaturen aktualisieren,
  Schnellscan ausführen, Firewall einschalten, SmartScreen auf „Warnen“ stellen,
  Benutzerkontensteuerung wieder einschalten, TRIM einschalten, Laufwerk prüfen (nur Scan),
  Wiederherstellungspunkt anlegen, Startprogramm deaktivieren, Geplante Aufgabe deaktivieren,
  Startart eines Dienstes ändern, Update-Dienst wieder einschalten, Update verbergen, Gerät
  wieder einschalten. Ein Gerät wird nur eingeschaltet, wenn Sie die Frage dazu vorher mit
  „wieder einschalten“ beantwortet haben. Was das Programm nicht selbst ausführt (Hosts-Datei,
  Treiber, Reparatur des Dateisystems, Speichertest), bleibt ein Hinweis ohne Schaltfläche;
  „Speicherplatz aufräumen“ und „Windows Update öffnen“ öffnen nur die passende Ansicht.
- **Vorschau vor jedem Eingriff.** Der Klick auf „Beheben“ öffnet zuerst eine Vorschau mit
  drei Absätzen (Was passiert, Was bleibt, Risiko), der Zeile „Sicherung, Rückweg, Dauer“, dem
  Hinweis auf einen nötigen Neustart und der Nachfrage von Windows. Erst „Jetzt ausführen“
  startet die Maßnahme; „Abbrechen“ lässt alles, wie es ist. Läuft gerade etwas anderes oder
  fehlt dem Befund ein Wert, steht der Grund in der Vorschau, und es gibt nur „Schließen“.
- **Nachweis nach dem Lauf.** Nach der Maßnahme liest das Programm den PC erneut und prüft am
  neuen Bild, ob sie gewirkt hat. Die Nachweiskarte ganz oben im Ergebnis kennt drei
  Ergebnisarten: **bestanden** (der neue Zustand ist belegt, der Satz nennt den Messwert),
  **nicht bestanden** (der alte Zustand gilt nachweislich noch) und **nicht messbar** (der
  Wert ließ sich nicht lesen, oder das Objekt ist im neuen Bild nicht mehr da; das zählt nie
  als bestanden). Beim DNS-Cache belegt der Nachweis nur die Ausführung ohne Fehler, weil
  Windows den Inhalt des Zwischenspeichers nicht im Bild führt; die Karte sagt das so. Braucht
  die Änderung einen Neustart, steht das auf der Karte. Jeder Nachweis landet auch im Verlauf.
- **„Rückgängig“ über die Sicherung.** Maßnahmen, die einen Wert ändern (Namensserver,
  Firewall, SmartScreen, Benutzerkontensteuerung, TRIM, Startprogramm, Aufgabe, Dienst-Startart,
  verborgenes Update, Gerät), schreiben die alten Werte vor dem Eingriff in eine
  Sicherungsdatei; scheitert die Sicherung, wird nichts geändert. Die Nachweiskarte und der
  Verlauf zeigen dann „Rückgängig“: ein Klick, eine Nachfrage, und der Helfer schreibt die
  gesicherten Werte zurück, misst erneut und weist auch das nach. Die Datei bleibt liegen, ein
  zweites „Rückgängig“ ist möglich.
- **Freigaben für die geplante Wartung.** Die Einstellungen zur automatischen Wartung haben
  den Abschnitt „Was das Programm von selbst beheben darf“ mit sechs Schaltern: DNS-Cache
  leeren, Virensignaturen aktualisieren, Schnellscan ausführen, Firewall einschalten, Laufwerk
  prüfen (nur Scan), Wiederherstellungspunkt anlegen. Voreinstellung: alle aus. Mit Haken führt
  die geplante Wartung die Maßnahme nur aus, wenn die Prüfung sie in diesem Lauf vorschlägt;
  danach misst sie erneut und schreibt den Nachweis ins Protokoll. Der Verlaufseintrag nennt
  die Zahlen („2 Maßnahmen, 2 von 2 Nachweisen bestanden, 1 Sicherung angelegt“). Die
  Werkzeugsätze der Wartung laufen wie bisher bei jeder Wartung.
- **Werkzeugkasten: drei Einträge sind Maßnahmen geworden.** „Gemerkte Internet-Adressen
  verwerfen“, „Neue Netzwerk-Adresse vom Router holen“ und „Kurzen Virenscan starten“ stehen
  nicht mehr im Werkzeugkasten (27 Einträge statt 30); die Prüfung bietet sie als Maßnahme an
  der passenden Karte an, mit Vorschau und Nachweis, und nur, wenn sie den passenden Befund
  liefert: DNS-Cache leeren bei gestörter Namensauflösung, Adresse neu anfordern bei einer
  Notadresse 169.254.x.x oder einem fehlenden Router-Eintrag an einem DHCP-Anschluss,
  Schnellscan, wenn der letzte mehr als 14 Tage zurückliegt und in dieser Zeit kein Vollscan
  lief. Im Werkzeugkasten gibt es damit keinen Schnellscan ohne Befund mehr; in der geplanten
  Wartung bleiben die Werkzeugsätze „Kurzen Virenscan starten“ und „Gemerkte Internet-Adressen
  verwerfen“ wählbar und laufen dann bei jeder Wartung, ohne Befund und ohne Nachweis. Eine
  Warteschlange aus einer früheren Fassung mit einem der drei Einträge läuft weiter.

### Behoben

- **Ein frisch angelegter Wiederherstellungspunkt galt als „nicht angelegt“.** Windows meldet
  das Anlegen sofort als erledigt, trägt den Punkt aber erst einige Sekunden später in die
  Liste ein; das Programm las die Liste zu früh und meldete „Kein Wiederherstellungspunkt
  vorhanden“, obwohl der Punkt da war (auf dem Entwicklungsrechner nach 2 Sekunden). Jetzt
  liest es bis zu 30 Sekunden nach, bis die Nummer gestiegen ist, und nennt die Wartezeit.
- **TRIM wurde als abgeschaltet gemeldet, obwohl es an war,** wenn Windows die Einstellung nur
  für ein anderes Dateisystem (ReFS) abgeschaltet hatte. Gezählt wird jetzt nur die Einstellung
  für NTFS, das Dateisystem Ihrer Laufwerke; Prüfung, Vorschau und Nachweis sprechen von
  derselben Einstellung.
- **Der Hinweis zum abgeschalteten Update-Dienst riet zu „Manuell (Standard)“;** richtig ist
  „Automatisch“, und die Maßnahme „Update-Dienst wieder einschalten“ tut genau das: Startart
  Automatisch und sofort starten.
- **Der Zeitplan wird nie mehr halb geschrieben.** `zeitplan.json` entsteht erst als
  Zwischendatei und wird dann in einem Zug ersetzt. Ist die Datei vorhanden, aber unlesbar,
  werden Freigaben nicht gespeichert, und das Programm sagt es im Protokoll, statt den
  Zeitplan still zu überschreiben.
- **Bei einer Störung des Namensdienstes bietet die Karte „Namensdienst (DNS) antwortet nicht
  richtig“ das Zurücksetzen der Namensserver nur noch an einem Anschluss an, der seine Adresse
  per DHCP bezieht** (IPv4, verbunden); bei jedem anderen Anschluss bleibt dort der Hinweis, die
  Einstellung von Hand zu prüfen. Die Hinweiskarte „Namensserver fest eingetragen“ hat keine
  Schaltfläche.

### Gut zu wissen

- **Der Update-Dienst kennt kein „Rückgängig“.** „Update-Dienst wieder einschalten“ legt eine
  Sicherung als Beleg an, aber das Programm schaltet Windows-Update-Dienste nie wieder ab; wer
  das will, tut es in der Dienstverwaltung von Hand. Die Nachweiskarte sagt das an Stelle der
  Schaltfläche.
- **Sicherungsdateien gehören dem Programm.** Sie liegen unter
  `C:\ProgramData\WindowsWartung\sicherungen\massnahmen`; Benutzer können sie lesen, aber nicht
  ändern. Der Helfer spielt nur eine Datei zurück, die er selbst geschrieben hat (Besitzer,
  Rechte und genau ein Dateiname werden geprüft); eine untergeschobene oder veränderte Datei
  lehnt er ab.
- **Zeitbudget der geplanten Wartung: 4 Stunden statt 2,** weil vor DISM und SFC jetzt eine
  Messung, die freigegebenen Maßnahmen (ein Schnellscan bis 20 Minuten, ein Laufwerks-Scan bis
  30 Minuten je Laufwerk) und die Nachmessung laufen. Das gilt für neu eingerichtete
  Zeitpläne; ein bestehender Zeitplan behält 2 Stunden, bis Sie ihn in den Einstellungen neu
  einrichten.
- **Aufgaben mit Sonderzeichen im Namen bekommen keinen Schalter.** Enthält der Pfad einer
  geplanten Aufgabe andere Zeichen als Buchstaben von A bis Z (ohne Umlaute), Ziffern,
  Leerzeichen und `_ . - \ ( ) { }`, gibt das Programm ihn nicht an die Aufgabenplanung
  weiter; eine Aufgabe „Sicherung Büro“ bekommt also keinen Schalter, und die Karte sagt dann,
  dass die Aufgabe nur dort von Hand zu deaktivieren ist. Aufgaben von Windows Update, Windows Defender
  und die eigene Wartungsaufgabe lassen sich hier nie deaktivieren.
- **Die Maßnahme zur Benutzerkontensteuerung setzt zwei Werte:** den Schalter (EnableLUA auf
  1, wirkt nach 1 Neustart) und die Nachfragestufe (ConsentPromptBehaviorAdmin auf 5, wirkt
  sofort), jeweils nur, wenn der Wert 0 ist. „Rückgängig“ stellt beide alten Werte wieder her.
- **Ein Nachweis, der nicht messbar ist, ist kein Erfolg.** Kann das Programm den Wert nach
  der Maßnahme nicht lesen, zeigt die Karte „Nachweis nicht messbar“ mit dem Grund; in der
  geplanten Wartung zählt das als nicht bestanden.
- **„Wiederherstellungspunkt anlegen“ hebt die 24-Stunden-Sperre von Windows dauerhaft auf**
  (SystemRestorePointCreationFrequency 0), sonst entstünde nach einem Punkt vom selben Tag
  kein zweiter.

## [8.1.0] - 2026-09-13

Das Programm startet ohne die Nachfrage von Windows. Administratorrechte holt es sich erst,
wenn Sie etwas ändern lassen, und nur für diesen einen Schritt.

### Neu

- **Kein Dialog mehr beim Start.** Prüfen, Ansehen, Bericht speichern: alles läuft mit Ihren
  normalen Rechten. Erst wenn Sie eine Reparatur, ein Werkzeug oder die Tiefenprüfung
  starten, fragt Windows einmal nach Administratorrechten. Die Erlaubnis gilt dann für diese
  Sitzung; nach 10 Minuten ohne Auftrag fragt Windows erneut.
- **„Nein“ im Dialog ist erlaubt.** Lehnen Sie die Nachfrage ab, sagt das Programm das kurz
  und lässt alles, wie es ist. Sie können es jederzeit erneut versuchen.
- **Was ohne Rechte nicht lesbar ist, wird benannt.** Einige Werte (Festplatten-Zähler,
  Prüfbit der Laufwerke, Ausnahmen des Virenschutzes, Wiederherstellungspunkte, Sicherungsspeicher,
  ein Ereignisprotokoll) brauchen Administratorrechte. Die Prüfung zeigt dann „N Werte brauchen
  einmal Administratorrechte“ mit einer Schaltfläche; nach dem Klick werden sie in etwa
  10 Sekunden ergänzt.
- **Der Helfer prüft jeden Auftrag selbst.** Der Teil mit Administratorrechten kennt nur
  einen festen Katalog von Maßnahmen und nimmt von der Oberfläche keine Befehle an, nur
  Kennungen. Was nicht im Katalog steht, läuft nicht.
- **Das Programm prüft sich beim Start selbst.** Signatur und Oberflächendateien werden
  gegen die eingebaute Liste geprüft. Wurde eine Datei verändert, startet die installierte
  Fassung nicht und bittet um eine Neuinstallation.
- **Verlauf, Zeitplan und Protokolle liegen jetzt an einem Ort** (`C:\ProgramData\WindowsWartung`),
  unabhängig davon, welches Konto das Programm bedient. Vorhandene Daten werden beim ersten
  Start übernommen.

### Behoben

- **Wechseldatenträger von gestern zählen nicht mehr als Festplattenfehler.** Meldungen für
  einen USB-Stick, der inzwischen abgezogen ist, wurden als Laufwerksfehler gewertet.
- **Die geplante Wartung fällt nicht mehr still aus,** wenn sie während einer laufenden
  Prüfung fällig wird. Sind Administratorrechte in dieser Sitzung schon erteilt, startet sie,
  sobald der Lauf beendet ist; sonst läuft sie im Hintergrund weiter, ohne Fenster und ohne
  Nachfrage. Läuft sie im Hintergrund, lehnt das Programm einen zweiten Start ab, statt DISM
  zweimal laufen zu lassen.
- **Ein Werkzeug, das die Zeitgrenze überschreitet, endet nicht mehr als „Fertig“.** Jeder
  Werkzeugschritt läuft in einem eigenen Auftragsobjekt von Windows; ist die Zeitgrenze
  erreicht (in der Regel 45 Minuten) oder klicken Sie „Abbrechen“, werden auch die
  Hilfsprozesse beendet, die das Werkzeug gestartet hat (bei DISM zum Beispiel
  `DismHost.exe`), und der Schritt endet mit „Zeitgrenze erreicht“ oder „nicht gestartet“.

### Gut zu wissen

- **Selbststart aus Version 8.0:** Wer das Programm bei der Anmeldung starten ließ, hat eine
  Aufgabe mit Administratorrechten. Die Einstellungen zeigen das an; „Umstellen“ fragt einmal
  nach Rechten und legt die Aufgabe ohne sie neu an.
- **Update ohne Installer:** Wer das Programm aus dem ZIP nutzt, sieht beim Update den
  Windows-Dialog für den Befehlsprozessor; der Installer-Weg zeigt den Namen des Programms.
  Empfohlen bleibt der Installer.
- **Nach einem ZIP-Update von 8.0:** Die Dateien unter `C:\ProgramData\WindowsWartung` gehören
  dann noch der Administratorengruppe, und eine Antwort auf eine Frage lässt sich nicht
  speichern („Die Antworten gehören noch der vorigen Fassung; nach dem nächsten Schritt mit
  Administratorrechten klappt es“). Einmal „Mit Administratorrechten ergänzen“ klicken oder
  ein Werkzeug starten, danach sind die Dateien wieder beschreibbar. Der Installer setzt die
  Rechte selbst.
- **Ein anderes Konto im Windows-Dialog:** Tippen Sie dort das Kennwort eines anderen Kontos
  ein, lehnt das Programm alles ab, was Ihr Benutzerprofil betrifft (Speicher aufräumen, Apps
  entfernen, Werkzeuge, geplante Wartung und ihr Zeitplan): „Im Dialog ist ein anderes Konto
  angemeldet; diese Maßnahme braucht Ihr eigenes Konto.“ Bei den Registrierungs-Einträgen
  werden die Ihres Profils übersprungen und gezählt. Tiefenprüfung, Reparatur und die
  Ergänzung der Messwerte laufen mit jedem Administratorkonto.
- **Herausgeber-Bindung des Updates:** Ein Update wird nur angenommen, wenn Name **und**
  öffentlicher Schlüssel des Signaturzertifikats zur installierten Fassung passen. Ein
  Zertifikat mit gleichem Namen, aber neuem Schlüsselpaar wird abgelehnt.
- Die Werkzeuge im Werkzeugkasten laufen unverändert (eine Warteschlange legt wie seit 7.0.1
  einen Sicherungspunkt am Anfang an, nicht je Werkzeug); die Umstellung auf Maßnahmen mit
  Vorschau folgt mit 8.2.

## [8.0.0] - 2026-09-12

Eine neue Diagnose im vertrauten Gehäuse. Alles, was Sie bisher hatten (die Werkzeuge, die
beiden Suchläufe, die geplante Wartung, das Update), bleibt. Neu ist, wie das Programm Ihren
PC ansieht.

### Neu

- **Die Prüfung dauert Sekunden, nicht Minuten.** Das Programm liest den PC jetzt direkt über
  die Schnittstellen von Windows aus, ohne ein einziges PowerShell-Fenster zu starten. Auf
  dem Entwicklungsrechner: rund zehn Sekunden für alle acht Bereiche. Die Prüfung verändert
  dabei nichts.
- **Jeder Punkt sagt, was gemessen wurde und woher der Wert kommt.** Hinter jedem Befund
  steht unter „Fachlich“ der Messwert, die Grenze, ab der er zum Problem wird, und die
  Quelle. Ein Techniker sieht damit auf einen Blick, worauf sich die Aussage stützt.
- **Fragen statt reparieren.** Ist an Ihrem PC etwas abgeschaltet, das Absicht sein kann
  (zum Beispiel ein Gerät im Geräte-Manager), fragt das Programm einmal nach: „Haben Sie
  das so eingerichtet?“ Ihre Antwort wird gemerkt und gilt, bis sich der Zustand ändert.
  Nichts wird repariert, was Sie selbst so gewollt haben.
- **Acht Bereiche mit dokumentierten Grenzen:** Sicherheit, Zustand der Festplatten (bei
  NVMe samt Gesundheitsprotokoll und Herstellergrenzen für die Temperatur), freier
  Speicherplatz, Stabilität (Blauschirme mit Fehlercode, Stromausfälle, Programmabstürze,
  Hardwarefehler), Windows-Updates (auch Updates, die sich immer wieder installieren),
  Geräte und Treiber, Arbeitsspeicher und Startprogramme, Netzwerk.
- **Beim Öffnen sehen Sie sofort, wie es dem PC geht.** Der erste Blick läuft von selbst
  und braucht keinen Klick.
- **Die Tiefenprüfung ist ein eigener Schritt.** Windows seine eigenen Dateien durchsehen
  zu lassen dauert 5 bis 10 Minuten; das startet nur, wenn Sie es wollen, und wird
  vorher gesagt. Sie liest nur, sie verändert nichts.
- **Beheben gibt es nur, wenn ein Befund es benennt.** Hat die Tiefenprüfung nichts
  gefunden, gibt es nichts zu reparieren, und das Programm sagt das auch so. Vor jeder
  Reparatur wird ein Sicherungspunkt angelegt, und das Programm prüft nach, ob er wirklich
  entstanden ist.
- **Ein Protokoll für zwei Leser.** Jeder Lauf schreibt mit: eine Zeile in Alltagssprache
  für Sie, das Fachliche daneben für den Techniker. „Bericht speichern“ legt beides als
  Textdatei ab.
- **Was sich nicht feststellen ließ, wird so genannt.** Fehlen Rechte oder Daten, steht
  „ließ sich nicht prüfen“ samt Grund da. Nie wird eine leere Antwort als „in Ordnung“
  ausgegeben.
- **Für Helfer:** `WindowsWartung.exe --aufzeichnen bild.json` nimmt das Systembild eines
  fremden Rechners auf (Rechnername, Benutzername, Seriennummern und Adressen werden
  entfernt), `--pruefen bild.json` schreibt die Befunde daneben.

### Behoben

- **„Abstürze anzeigen“ zeigt jetzt die Abstürze.** Bisher verdrängten die vielen normalen
  Neustarts die echten Vorfälle, und Meldungen anderer Quellen mit derselben Nummer wurden
  als Absturz mitgezählt. Jetzt wird jede Nummer nur bei ihrer echten Quelle gelesen, und
  normale Neustarts stehen getrennt am Ende.
- **Der Windows-Update-Reset vergisst keine ausgeblendeten Updates mehr.** Vor dem Reset
  werden sie gesichert und danach wieder ausgeblendet. Vorher kam ein bewusst ausgeblendetes
  Update nach dem Reset immer wieder.
- **Ein Microsoft-Konto ohne lokales Kennwort ist kein Problem mehr.** Bei einem
  Microsoft-Konto läuft die Anmeldung über das Microsoft-Konto; nur rein lokale Konten ohne
  Kennwort werden gemeldet.

### Gut zu wissen

- Das Programm braucht in dieser Fassung weiterhin Administratorrechte beim Start. Die
  Trennung in einen Teil ohne Rechte und einen Helfer mit Rechten folgt mit 8.1.
- Die Antworten auf Fragen und die Protokolle liegen unter `C:\ProgramData\WindowsWartung`,
  weil sie den PC betreffen und nicht das Konto, das das Programm gerade bedient.

## [7.3.2] - 2026-08-22

### Behoben

- **Der Bildschirm bleibt nicht mehr stehen, wenn die geplante Wartung dazwischenkommt.**
  Lief die Wartung nach Zeitplan, während Sie „Beheben“ oder „PC prüfen“ angeklickt haben,
  passierte Folgendes: Die App schaltete auf den Ablauf-Bildschirm um, verwarf Ihren Klick
  aber stillschweigend, weil sie schon beschäftigt war. Der Fortschrittsbalken wurde noch
  von der Wartung weitergezogen und blieb dann für immer stehen – bei uns bei 70 %, über
  fünf Stunden. In Wahrheit war längst alles fertig. Jetzt sagt Ihnen die App, was gerade
  läuft, und bringt Sie zurück zum Start.
- **Die Fertigmeldung räumt den Bildschirm jetzt in jedem Fall auf.** Bisher tat sie das
  nur bei Werkzeugen, die Sie selbst gestartet hatten. Startete die App einen Lauf von
  sich aus, stand unter „Technische Details“ schon „Fertig“, während oben noch der
  Fortschritt lief.
- **„Abbrechen“ funktioniert wieder in jeder Lage.** Zwei Fälle machten den Knopf
  wirkungslos: wenn im Hintergrund gar nichts mehr lief, und nach jedem fertigen Werkzeug –
  danach blätterte er nur noch weiter, ohne etwas abzubrechen.
- **Kein Schritt kann den Lauf mehr endlos festhalten.** Bleibt ein Windows-Werkzeug
  hängen, wird es nach 45 Minuten beendet und der Lauf geht weiter. Vorher konnte ein
  einziger solcher Schritt alles Weitere blockieren.

### Gut zu wissen

- Die geplante Wartung schreibt jetzt Anfang und Ende ins Protokoll. Vorher lief sie zehn
  Minuten lang über Ihr System, ohne dort eine Spur zu hinterlassen – beim Nachsehen sah
  es aus, als sei nichts geschehen.

## [7.3.1] - 2026-08-05

### Neu

- **Die Wartezeit lässt sich jetzt frei eintragen.** Die vier Vorgaben aus 7.3.0 sind nur
  Abkürzungen für das Übliche; wer 45 Minuten braucht, trägt daneben einfach 45 ein.
  Möglich ist alles von einer Minute bis zu 24 Stunden. Vorgaben und Eingabefeld halten
  sich gegenseitig aktuell: Klicken Sie eine Vorgabe an, steht sie im Feld, und tippen Sie
  einen eigenen Wert, ist keine Vorgabe mehr hervorgehoben.
- Tragen Sie mehr ein als möglich ist, bekommen Sie beim Verlassen des Feldes den
  Höchstwert, tragen Sie weniger ein, den Mindestwert. Im Feld steht danach genau das, was
  auch wirklich gilt.

## [7.3.0] - 2026-08-05

### Neu

- **„Wenn alles fertig ist“ gibt es jetzt auch bei der Prüfung und der Reparatur.** Bisher
  stand die Wahl nur im Werkzeugkasten. Ausgerechnet beim Hauptweg fehlte sie, also genau
  dort, wo ein Lauf zehn Minuten und länger dauert und man derweil weggehen möchte.
- **Sie können jetzt selbst bestimmen, wie lange vorher gewartet wird:** 1, 5, 15 oder 30
  Minuten. Bisher war es immer eine Minute, ohne dass sich daran etwas ändern ließ. Diese
  Zeit ist Ihr Fenster zum Abbrechen: Solange läuft unten das Banner, mit dem Sie das
  Herunterfahren noch stoppen können.
- **Die Wahl lässt sich noch während des Laufs treffen.** Sie müssen sich also nicht vorher
  entscheiden, sondern erst dann, wenn Sie tatsächlich gehen wollen.

### Geändert

- **Abgebrochen heißt abgebrochen.** Brechen Sie einen Lauf ab oder geht dabei etwas schief,
  wird der PC nicht mehr heruntergefahren, auch wenn Sie es vorher so gewählt hatten.
- **Das Banner sagt die Zeit in Minuten.** Vorher stand dort „Der PC wird in 900 Sekunden
  heruntergefahren“.

### Behoben

- **Die automatische Wartung startet nicht mehr mitten in einen laufenden Prüfvorgang
  hinein.** Bisher wurde nur auf Einzelaktionen Rücksicht genommen. Fiel der Termin während
  einer Prüfung oder Reparatur, liefen deren Windows-Werkzeuge doppelt.

## [7.2.0] - 2026-08-05

Die letzten offenen Punkte aus der großen Prüfung von v7.0. Nichts davon fällt im Alltag auf,
solange alles gutgeht. Genau darum ging es: dass es auch dann stimmt, wenn etwas schiefläuft.

### Sicherer

- **Der Verlauf überlebt jetzt einen Absturz.** Bisher wurde die Verlaufsdatei beim
  Schreiben zuerst geleert und dann neu gefüllt. Ging in genau diesem Moment der Strom aus
  oder stürzte etwas ab, blieb eine halbe Datei zurück, die sich nicht mehr lesen ließ, und
  der gesamte Verlauf war stillschweigend weg. Jetzt wird daneben geschrieben und erst am
  Ende getauscht, und von der vorherigen Fassung bleibt eine Sicherungskopie liegen. Ist die
  Hauptdatei beschädigt, wird sie benutzt.
- **Ein misslungenes Update wird rückgängig gemacht.** Vor dem Austauschen der
  Programmdateien legen wir die bisherige Fassung zur Seite. Bricht das Kopieren mittendrin
  ab, wird sie zurückgeholt, statt eine halb ersetzte Installation zu starten. Lässt sich
  die Sicherung nicht anlegen, wird gar nichts angefasst.
- **Ein misslungenes Update sagt es Ihnen auch.** Vorher passierte nach dem Klick auf
  „Jetzt aktualisieren“ scheinbar einfach nichts.
- **Ein Schalter, der nichts bewirkt hat, springt zurück.** Ließ sich ein Startprogramm
  nicht umschalten, zeigte die Liste die Änderung trotzdem an. Man war also überzeugt, das
  Programm starte nicht mehr mit, während es weiter mitstartete.

### Neu

- **Das Programm sagt Ihnen, wenn es unter einem anderen Konto läuft.** Wer die
  Administratorrechte über ein anderes Benutzerkonto holt, sieht ab da dessen Startprogramme,
  dessen Verlauf, dessen Speicherplatz und dessen Registrierungs-Einträge, nicht die eigenen.
  Das ist ungefährlich, aber verwirrend. Jetzt steht es auf dem Startbildschirm, mit beiden
  Kontonamen. Am PC selbst wird trotzdem alles richtig repariert.

## [7.1.1] - 2026-08-05

Zwei Nachbesserungen an den neuen Ansichten aus 7.1.0.

### Geändert

- **Sehr lange Gruppen sind zugeklappt.** In der Ansicht „Einträge, die ins Leere zeigen“
  machten die reinen Merkzettel allein 98 Prozent aller Funde aus: mehrere hundert Zeilen,
  durch die niemand scrollt, und der Knopf zum Entfernen lag ganz am Ende dahinter. Gruppen
  ab zwölf Einträgen sind jetzt zugeklappt und nennen nur ihre Anzahl. Die kleinen, wirklich
  interessanten Gruppen bleiben offen. „Alle auswählen“ liegt bewusst mit im zugeklappten
  Bereich: Wer hunderte Einträge auf einmal auswählt, soll sie vorher aufgeklappt haben.

### Sicherer

- **Nichts wird mehr mit den Rechten des Programms geöffnet.** Windows-Wartung läuft mit
  Administratorrechten. Wurde von hier aus ein Explorer-Fenster, der Browser oder das
  Fehlerprotokoll geöffnet, erbte das geöffnete Programm diese Rechte. Aus einem Explorer
  mit Administratorrechten lässt sich anschließend jede beliebige Datei mit denselben
  Rechten starten, und ein Browser gäbe sie jedem Download mit. Solche Fenster öffnet jetzt
  die Windows-Oberfläche selbst, die ganz normal unter Ihrem Benutzerkonto läuft. Betrifft
  sechs Stellen: die beiden „Anzeigen“-Schaltflächen der neuen Ansichten, den Autostart-Ordner,
  den Weg zur Download-Seite, den Hinweis auf die fehlende Windows-Komponente und das
  Fehlerprotokoll.

## [7.1.0] - 2026-08-04

Zwei neue Ansichten beantworten Fragen, die das Programm bisher offengelassen hat.

### Neu

- **„Wo steckt der Platz?“** Bisher stand im Ergebnis „machen Sie Platz frei“, ohne zu
  sagen, wo. Jetzt zeigt eine eigene Ansicht beides: was sich gefahrlos wegräumen lässt,
  jede Kategorie einzeln mit ihrer Größe, und daneben die größten Ordner und Dateien in
  Ihren eigenen Bereichen. Sie ist über die Bereichszeile „Freier Speicherplatz“, über den
  Ratschlag im Ergebnis und über „Alle Werkzeuge“ erreichbar.
- **Aufräumen mit Vorschau.** Sie sehen zuerst, was wie viel bringt, und wählen dann aus.
  Hinterher steht dort, wie viel wirklich frei geworden ist, nicht wie viel erhofft war.
  Der Papierkorb ist nie vorangehakt: Was darin liegt, ist danach endgültig weg. Der Ordner
  „Downloads“ wird gar nicht erst angeboten, weil dort Dateien liegen, die Sie behalten
  möchten.
- **„Einträge, die ins Leere zeigen“.** Windows führt eine Liste, in der Programme
  hinterlegen, wo ihre Dateien liegen. Wird ein Programm entfernt, bleibt der Eintrag
  manchmal stehen. Gemeldet wird ausschließlich, was sich nachweisen lässt: Der Eintrag
  nennt eine Datei, und die gibt es nicht mehr. Vor dem Entfernen legt das Programm einen
  Sicherungspunkt an und schreibt zusätzlich eine Sicherungsdatei, mit der sich jeder
  einzelne Eintrag per Doppelklick zurückholen lässt. Lässt sich die Sicherung nicht
  schreiben, wird nichts verändert.

### Gut zu wissen

Das Aufräumen der Einträge macht Ihren PC nicht schneller, und die Ansicht behauptet das
auch nicht. Diese Einträge kosten so gut wie keinen Platz und bremsen nichts. Was es
bringt, ist Ordnung. Deshalb ist dort auch nichts vorausgewählt.

### Sicherer

- **Ein Eintrag gilt erst als tot, wenn auch der Weg zum Deinstallieren tot ist.** Auf
  einem echten Rechner nachgemessen: Von neun gemeldeten Programmen waren drei fälschlich
  dabei. Bei einem Chipsatz-Paket zeigte der eingetragene Ordner ins Leere, weil es der
  längst gelöschte Entpack-Ordner war, während die Deinstallation einwandfrei funktioniert.
  Wer solche Einträge entfernt, nimmt sich die Möglichkeit, das Paket je wieder sauber
  loszuwerden. Diese drei Fälle werden jetzt übersprungen.
- **Nur fest eingebaute Laufwerke werden beurteilt.** Bei USB-Sticks, Speicherkarten und
  Netzlaufwerken sagt ein vorhandener Laufwerksbuchstabe nichts: Windows vergibt die
  Buchstaben in der Reihenfolge des Ansteckens, dahinter kann ein anderer Datenträger
  liegen als früher.
- **Die Oberfläche kann keinen Pfad vorgeben.** Sie schickt nur Kennungen. Welcher Ordner
  und welcher Eintrag dahintersteckt, entscheidet allein das Programm.
- **Ohne vollständige Sicherung wird nichts entfernt, und zwar wörtlich.** Lässt sich auch
  nur einer der betroffenen Einträge nicht sichern, bricht der ganze Vorgang ab und es
  bleibt alles, wie es war.
- **Der Papierkorb wird nur dort geleert, wo er vorher auch gezeigt wurde.** Auf diesem
  Rechner waren das 5,1 GB auf allen fest eingebauten Laufwerken statt 1,3 GB auf dem
  Windows-Laufwerk. Vorher wurde nur das Windows-Laufwerk gemessen, geleert aber jedes
  Laufwerk einschließlich angesteckter USB-Datenträger.
- **Beim Aufräumen wird auf jeder Ebene geprüft, nicht nur auf der obersten.** Verweise auf
  andere Ordner werden als Verweis entfernt, niemals ihr Ziel, und Dateien, die nur
  stellvertretend für die Cloud dastehen, bleiben unangetastet.
- **Beim Entfernen von Einträgen bleibt das Fenster stehen.** Vorher ließ es sich mitten im
  Vorgang schließen, obwohl der Bildschirm ausdrücklich sagt, dass sich das nicht mehr
  anhalten lässt.

## [7.0.3] - 2026-08-04

Erste signierte Ausgabe.

### Neu

- **Programm und Installer sind signiert.** Beides trägt jetzt eine Authenticode-Signatur
  mit Zeitstempel. Das macht nachträgliche Veränderungen erkennbar: Windows meldet eine
  beschädigte Datei, statt sie stillschweigend auszuführen.
- **Damit greift die Herkunftsprüfung aus 7.0.2 wirklich.** Eine neue Fassung wird nur noch
  angenommen, wenn sie denselben Herausgeber trägt. Getestet: gleiche Herkunft wird
  angenommen, eine unsignierte und eine fremd signierte Fassung werden abgelehnt.

### Gut zu wissen

Das Zertifikat ist selbst ausgestellt, nicht von einer anerkannten Stelle gekauft. Beim
ersten Start zeigt Windows deshalb weiterhin den Hinweis „Der Computer wurde geschützt“;
über *Weitere Informationen → Trotzdem ausführen* startet das Programm. Was die Signatur
trotzdem bringt: Sie erkennt Manipulationen, und sie schützt die Selbstaktualisierung.

## [7.0.2] - 2026-08-04

Sicherheit rund um die Selbstaktualisierung.

### Sicherer

- **Die neue Fassung muss vom selben Herausgeber stammen.** Bisher belegte nur eine
  Prüfsumme, dass der Download heil ankam. Die liegt aber im selben Release wie die
  Datei und sagt deshalb nichts über die Herkunft. Jetzt wird zusätzlich die Signatur
  geprüft und an den Herausgeber der installierten Fassung gebunden. Ist die installierte
  Fassung nicht signiert, entfällt die Bindung, damit eine Aktualisierung überhaupt
  möglich bleibt.
- **Wer über den Installer installiert hat, wird über den Installer aktualisiert.**
  Vorher wurden nur die Dateien getauscht. Der Eintrag unter „Apps und Features“ blieb
  dabei auf der alten Version stehen, und eine spätere Deinstallation hätte Reste
  übersehen. Erkannt wird das am Installationsort in der Registrierung.
- **Kein Passwort mehr im Quelltext.** Die Skripte zum Signieren und zum Erzeugen des
  Zertifikats hatten ein fest eingetragenes Standardpasswort. Es kommt jetzt aus dem
  Aufruf, aus der Umgebungsvariablen `WW_CERT_PASSWORD` oder aus einer Abfrage.

### Behoben

- **Die Prüfsumme wird eindeutig zugeordnet.** Seit dem Release auch eine Prüfsumme für
  den Installer beiliegt, gab es zwei Kandidaten, und welcher genommen wurde, entschied
  allein die Reihenfolge der Antwort von GitHub. Das ging bisher zufällig gut. Kippt die
  Reihenfolge, hätte die App das Paket gegen die falsche Prüfsumme geprüft und die
  Aktualisierung als beschädigt abgelehnt. Jetzt zählt der Dateiname.

### Unter der Haube

- Die Signatur-Prüfung ist selbst getestet: ein kleines Prüfprogramm übersetzt sie und
  lässt sie gegen echte signierte Dateien laufen. Dabei kam heraus, dass eine Bindung an
  den Fingerabdruck des Zertifikats ein Fehler gewesen wäre: er ändert sich, sobald ein
  Zertifikat erneuert wird, und hätte ab diesem Tag jede weitere Aktualisierung abgelehnt.
  Gebunden wird deshalb am Herausgebernamen.
- Zwei weitere Prüfungen: kein fest eingetragenes Passwort in den Skripten, und die
  Prüfsumme muss über den Dateinamen zugeordnet werden.
- Die Bausteine der Abläufe in GitHub sind auf den aktuellen Stand gehoben.

## [7.0.1] - 2026-08-04

Nachtrag zu 7.0: Beim großen Umbau der Oberfläche waren acht Funktionen unter den Tisch
gefallen. Sie sind wieder da, an passenderer Stelle als vorher.

### Wieder da

- **Mehrere Aktionen vormerken.** Das Pluszeichen auf jeder Karte legt eine Aktion auf eine
  Liste; die läuft dann in einem Rutsch der Reihe nach durch. Ein Sicherungspunkt wird
  einmal für die ganze Liste angelegt statt für jede Aktion einzeln.
- **Wenn alles fertig ist.** Der PC kann sich nach dem letzten Lauf herunterfahren oder neu
  starten. Die Wahl steht im Werkzeugkasten und gilt für einzelne Aktionen genauso wie für
  die Liste; vor dem Start wird sie noch einmal genannt.
- **Verbindung zu einer Seite testen.** Prüft, ob und wie gut Ihr PC eine bestimmte Adresse
  erreicht, und zeigt den Weg dorthin.
- **Gerätetreiber sichern.** Kopiert alle nachinstallierten Treiber in einen Ordner Ihrer
  Wahl, praktisch vor einer Neuinstallation.
- **Windows-Wartung mit dem PC starten.** Der Schalter in den Startprogrammen ist zurück.
- **Autostart-Ordner öffnen**, getrennt für Ihr Konto und für alle am PC, mit Erklärung.
- **Verlauf leeren.**
- **Im Browser öffnen**, wenn eine Aktualisierung nicht durchläuft.

### Behoben

- **Abbrechen wirkt wieder.** Bei einer einzelnen Aktion aus dem Werkzeugkasten tat der Knopf
  nichts: er stoppte nur den Prüfablauf, nicht den tatsächlich laufenden Befehl. Jetzt gibt
  es einen Abbruch, der beides beendet.
- Das Pluszeichen zum Vormerken fehlte im Symbolsatz und blieb als leerer Kreis stehen.

### Unter der Haube

Vier neue Prüfungen, die genau diese Fehlerklassen künftig abfangen, bevor etwas
veröffentlicht wird:

- Jeder Befehl, den das Programm intern versteht, muss aus der Oberfläche auch auslösbar
  sein. Das hätte die acht verlorenen Funktionen sofort gemeldet.
- Abbrechen muss beide Laufarten stoppen.
- Das JavaScript der Oberfläche wird auf Lesbarkeit geprüft. Ein Tippfehler darin legt sonst
  die gesamte Oberfläche lahm, ohne dass der Bau etwas merkt.
- Jede angesprochene Kennung und jedes verwendete Symbol muss es wirklich geben.

## [7.0] - 2026-08-04

Die größte Überarbeitung seit Bestehen des Programms. Es richtet sich jetzt ausdrücklich an
Menschen ohne PC-Kenntnisse: ein klarer Hauptweg, verständliche Sprache, ein Ergebnis, das
die Frage „Und was mache ich jetzt?“ beantwortet.

### Neu

- **Ein Knopf statt eines Werkzeugkastens.** Beim Öffnen sehen Sie, wie es Ihrem PC geht,
  und darunter eine große Schaltfläche: **PC jetzt prüfen**. Kein Rätselraten mehr, welches
  von 28 Werkzeugen das richtige ist. Alle Werkzeuge sind weiterhin vollständig da, sie
  liegen jetzt hinter **Alle Werkzeuge**.
- **Sieben Prüfungen, die vorher fehlten.** Freier Speicherplatz, Virenschutz und Firewall,
  Zustand der Festplatten (samt Verschleiß), Ordnung auf der Festplatte, Windows-Updates,
  Stabilität der letzten zwei Wochen und der Akku-Zustand bei Laptops. Alle Prüfungen
  **lesen nur**, sie verändern nichts.
- **Ein Ergebnis-Bildschirm.** Nach einem Lauf steht dort, was gefunden wurde, was behoben
  wurde und vor allem: **was Sie jetzt tun sollten**, mit höchstens einer Schaltfläche.
  Bisher gab es nach einem Lauf nur eine Zeile Text, die nach vier Sekunden verschwand.
- **Fortschritt, den man versteht.** Statt „läuft … 45 %“ heißt es jetzt „Schritt 2 von 3:
  Die Dateien von Windows werden auf Beschädigungen geprüft“. Die rohe Ausgabe der Werkzeuge
  steht weiterhin zur Verfügung, aber zusammengeklappt unter **Technische Details**.
- **Heller und dunkler Modus.** Das Programm folgt Ihrer Windows-Einstellung; in den
  Einstellungen lässt sich das übersteuern. Bisher war es immer dunkel.
- **Vollständig mit der Tastatur bedienbar.** Alle Kacheln, Schalter und Dialoge lassen sich
  ansteuern, der Fokus ist überall sichtbar, Dialoge lassen sich mit Escape schließen und
  halten den Fokus fest. Vorlesehilfen bekommen jetzt Namen, Rollen und Statusmeldungen.
- **Fehlerprotokoll.** Läuft etwas schief, steht in einer Datei nachvollziehbar, was passiert
  ist. In den Einstellungen gibt es dafür eine Schaltfläche.

### Verständlicher

- **Jede Kachel heißt jetzt nach ihrer Aufgabe, nicht nach ihrem Werkzeug.** Aus
  „SFC scannow“ wurde „Windows-Dateien prüfen und reparieren“, aus „DISM RestoreHealth“
  wurde „Windows über das Internet reparieren“, aus „WinSxS aufräumen“ wurde „Alte
  Update-Reste löschen“. Der Fachname steht klein darunter, für alle, die ihn suchen.
- Sämtliche Erklärungen wurden neu geschrieben, ohne unerklärte Fachbegriffe.
- Durchgehende Anrede mit „Sie“. Vorher war sie gemischt.
- Der Punktestand „70/100“ ist weggefallen. Eine Zahl ohne Maßstab sagt niemandem etwas.
  Stattdessen steht der Zustand in Worten da, für jeden Bereich einzeln.
- Was sich nicht prüfen ließ, wird jetzt auch so genannt, statt als „in Ordnung“ zu gelten.

### Behoben

- **Der Sicherungspunkt wird jetzt auch bei riskanten Aktionen angelegt.** Das Häkchen
  „Sicherungspunkt vor jeder Reparatur“ wirkte bisher ausgerechnet bei den fünf Aktionen
  nicht, bei denen am meisten schiefgehen kann: Festplattenprüfung, Zurücksetzen der
  Interneteinstellungen, Neuaufbau der Windows-Suche, Speicherprüfung und Vorschaubilder.
- **Kein Herunterfahren mehr mitten in einer Reparatur.** Stand ein automatischer
  Wartungstermin an, während „danach herunterfahren“ gewählt war, startete beides
  gleichzeitig. Der PC konnte mitten im Reparieren ausgehen.
- **Updates werden nur noch installiert, wenn ihre Echtheit belegt ist.** Fehlte die
  Prüfsumme oder ließ sie sich nicht laden, wurde bisher trotzdem installiert.
- **Update mit Umlaut im Benutzernamen.** Das Programm startete nach der Aktualisierung
  nicht mehr, wenn im Pfad ein Umlaut vorkam.
- Der Fortschrittsbalken beim Aktualisieren zeigt jetzt den echten Download. Bisher lief er
  nach festem Zeitplan und stand bei langsamer Leitung minutenlang auf 100 %.
- **Zweiter Programmstart** öffnet nicht mehr ein totes schwarzes Fenster, sondern holt das
  bereits offene nach vorn.
- **Fehlt die WebView2-Komponente**, erklärt das Programm das jetzt und bietet die
  Download-Seite an, statt mit einem leeren Fenster stehen zu bleiben.
- **Auf hochauflösenden Bildschirmen** startet das Fenster wieder in sinnvoller Größe.
- **Die automatische Wartung läuft jetzt auch im Akkubetrieb** und holt einen verpassten
  Termin nach. Bisher fiel er ersatzlos aus, sobald der PC zur Terminzeit aus war.
- **„Windows-Update von vorn starten“ meldet nicht mehr blind Erfolg.** Ließ sich nichts
  zurücksetzen, stand trotzdem „Erfolgreich“ da.
- Ein hängender Hintergrundlauf blockiert nicht mehr alle künftigen Wartungstermine.
- Umlaute in Wiederherstellungspunkten und App-Namen werden richtig angezeigt.
- Die Ausgabe lässt sich jetzt markieren und kopieren.

### Unter der Haube

- Übersetzt wird ab sofort mit dem Roslyn-Compiler aus dem .NET SDK. Das Programm läuft
  unverändert ohne jede Installation auf jedem Windows 11; nur zum Bauen wird das SDK
  gebraucht. Die Datei bleibt mit rund 190 KB genauso klein und eigenständig wie vorher.
- Titel und Erklärungen der Aktionen stehen nur noch an einer einzigen Stelle. Vorher gab
  es sie doppelt, und 18 von 28 Beschreibungen waren bereits auseinandergelaufen.
- Neues Prüfskript, das bei jedem Bau läuft: Es wacht über verständliche Sprache, korrekte
  deutsche Anführungszeichen, Umlaute und darüber, dass riskante Aktionen ihren
  Sicherungspunkt behalten.

## [6.6] - 2026-07-13

### Neu
- **Fehlermeldungen, die weiterhelfen.** Schlägt ein Schritt fehl, erklärt die Ausgabe jetzt bekannte Fehler in einfacher Sprache und nennt einen konkreten Lösungsweg – z. B. Zugriff verweigert (Virenschutz/Datei in Benutzung), deaktivierter Windows-Dienst (mit Weg über services.msc) oder die DISM-Quellfehler 0x800F081F/0x800F0906/0x800F0954 (Internet prüfen → Windows-Update reparieren → erneut). Gedeutet werden **ausschließlich offiziell dokumentierte** Windows-Fehlercodes – unbekannte Codes bleiben bewusst unkommentiert, statt etwas zu erfinden.
- **SFC/DISM-Ergebnisse verständlich zusammengefasst.** Nach dem Lauf erscheint eine Klartext-Zeile („Verständlich gesagt: …“) auf Basis der offiziellen Meldungstexte: alles in Ordnung / repariert (mit Neustart-Empfehlung) / teilweise nicht reparierbar – inklusive des offiziell empfohlenen nächsten Schritts (erst DISM RestoreHealth, dann SFC erneut).
- **Erklärboxen in den Werkzeug-Kategorien.** Reparieren, Netzwerk, Aufräumen und Diagnose starten jetzt mit einer kurzen Laien-Einordnung (was passiert hier, was ist sicher, sinnvolle Reihenfolge).

### Behoben
- **Exit-Code 3010 wird korrekt als Erfolg gewertet.** 3010 bedeutet offiziell „erfolgreich, Neustart erforderlich“ – bisher wurde das fälschlich als Fehler angezeigt.
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
- **Update-Hinweis ohne Download-Paket** – direkt nach Erscheinen einer neuen Version konnte der Update-Dialog ins Leere laufen („Kein Download-Paket im Release gefunden“), weil das Release schon sichtbar war, während die Dateien noch gebaut wurden. Ein Release wird jetzt erst veröffentlicht, wenn alle Dateien vollständig angehängt sind; zusätzlich überspringt die App ein unvollständiges Release still und prüft beim nächsten Start erneut.

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
- **Geplante Wartung: Aufgaben frei wählbar** – in „Geplant“ lässt sich jetzt einstellen, **was** der automatische Lauf erledigt: 8 stille, ungefährliche Aufgaben zur Auswahl (DISM, SFC, Temp, Papierkorb, WinSxS, Update-Cache, DNS-Cache, Defender-Schnellscan). Solange nichts geändert wird, gilt weiter der bewährte Standard-Satz; „Auf Standard zurücksetzen“ ist ein Klick. Der Status zeigt die gewählten Aufgaben an.
- **Flexiblere Zeitpläne** – neben täglich/wöchentlich jetzt auch **monatlich** (Tag 1–31, mit Hinweis bei 29–31) und bei „wöchentlich“ **mehrere Wochentage** auf einmal (z. B. Mo + Mi + Fr) über anklickbare Tages-Chips.

### Sicherheit
- Alle neuen Eingaben (Wochentage, Monatstag, Aufgaben) werden ausschließlich über **feste Whitelists** übernommen – in die Aufgabenplanung (`schtasks`) gelangen nie Roh-Eingaben.

## [6.2] - 2026-06-21

### Neu
- **Lebendiges Dashboard** – beim Öffnen der Übersicht zählen die Werte für Prozessor, Arbeitsspeicher, Festplatte und der Gesundheits-Score nun weich von 0 auf ihren Stand hoch (synchron zur Ring-Füllung), und die Kacheln blenden dezent gestaffelt ein. Reine `transform`/`opacity`-Animation, **ohne zusätzliche Bibliothek** – leichtgewichtig auch auf älteren PCs.

### Barrierefreiheit
- **„Bewegung reduzieren“ wird respektiert** – ist die gleichnamige Windows-Einstellung aktiv, verzichtet die App jetzt durchgängig auf Animationen und zeigt Inhalte sofort im Endzustand. Das gilt auch für alle bisherigen Effekte (Kacheln, Hinweise, Dialoge, Fortschritt).

## [6.1] - 2026-06-09

### Neu
- **Bloatware-Entferner** – neue Sidebar-Ansicht „Bloatware“: listet vorinstallierte Apps (`Get-AppxPackage`), die viele nicht brauchen, gruppiert nach Kategorie. Mehrere auf einmal auswählen und – nach **doppelter** Sicherheitsabfrage – entfernen (`Remove-AppxPackage`). Auf Wunsch wird vorher ein **Wiederherstellungspunkt** angelegt; jeder Lauf landet im Verlauf.

### Sicherheit
- **Strikte Whitelist statt Holzhammer**: Es werden ausschließlich als unbedenklich bekannte Apps (Solitaire, Bing-News/Wetter, Xbox-Apps, Clipchamp, Teams-Consumer, 3D-Viewer, Cortana, OEM-Spiele u. a.) zur Auswahl angeboten. Eine zusätzliche **Blockliste** schützt System-, Shell-, Store-, Runtime- und Defender-Pakete hart – auch gegen versehentliche Katalog-Einträge (Defense-in-Depth).
- **Keine Befehls-Injektion**: Der `PackageFullName` wird vor jeder Verwendung streng auf erlaubte Zeichen `[A-Za-z0-9._-]` geprüft und nur in einfachen Anführungszeichen an PowerShell übergeben; entfernt wird ausschließlich, was die Whitelist erlaubt.

## [6.0] - 2026-06-09

### Neu
- **Reparatur-Verlauf** – jede Ausführung (Zeit, Aktion, Ergebnis, Dauer) wird protokolliert; neue Sidebar-Ansicht „Verlauf“ zeigt die letzten Läufe (mit „Leeren“). Gespeichert unter `%LOCALAPPDATA%\WindowsWartung\history.json`.
- **Wiederherstellungspunkt-Verwaltung** – eigene Ansicht: Punkt anlegen (mit Beschreibung), vorhandene auflisten und – nach **doppelter** Sicherheitsabfrage – auf einen Punkt zurücksetzen.
- **Geplante Wartung** – richtet über die Windows-Aufgabenplanung (schtasks, mit höchsten Rechten) einen wiederkehrenden Wartungslauf ein (täglich/wöchentlich + Uhrzeit). Der neue **`--auto`-Modus** führt einen gründlichen, ungefährlichen Satz (DISM RestoreHealth + SFC + Temp + Papierkorb) still im Hintergrund aus und meldet sich per Windows-Benachrichtigung; ist die App gerade geöffnet, wird der Termin ausgelassen.
- **Energieplan-Umschalter** – neue Ansicht „Energie“: zeigt die vorhandenen Energiesparpläne, markiert den aktiven und wechselt per Klick (powercfg).
- **Netzwerk-Diagnose** – Ping und Route (tracert) zu einem frei eingebbaren Ziel; die Eingabe wird streng geprüft und ohne Shell ausgeführt (keine Befehls-Injektion).
- **Treiber-Backup** – exportiert alle installierten Treiber in einen über den System-Dialog wählbaren Ordner (pnputil `/export-driver`).
- **Live-Fortschritt für DISM/SFC** – der Prozentwert wird laufend aus der (mit `\r` aktualisierten) Ausgabe gelesen und als Fortschrittsbalken angezeigt, statt dass die App „eingefroren“ wirkt.

### Geändert
- Der Screenshot-/Vorschaumodus nutzt einen eigenen WebView2-Datenordner und stört so eine bereits laufende Instanz nicht mehr.

## [5.10] - 2026-06-09

### Sicherheit
- **Update-Download wird per SHA-256 geprüft.** Das Release liefert eine Prüfsumme (`WindowsWartung.zip.sha256`); die App vergleicht den heruntergeladenen Inhalt damit und bricht bei Abweichung ab – bevor mit Adminrechten getauscht wird.

### Geändert
- **Erfolg/Fehler ehrlicher**: Best-effort-Schritte (Dienste stoppen/starten, catroot2 umbenennen …) werten einen erwartbaren Fehlercode nicht mehr als Gesamtfehler – die Windows-Update-Reparatur läuft dadurch robuster durch.
- **Aufräumen meldet Konkretes**: Temp & Update-Cache zeigen die freigegebenen MB, der Papierkorb erkennt „bereits leer“.
- **Defender-Schnellscan** mit Fallback: klare Meldung, wenn Defender nicht verfügbar ist (z. B. anderes Antivirus aktiv).

## [5.9] - 2026-06-09

### Behoben
- **„CHKDSK planen“ → „Zugriff verweigert“ behoben.** Der Befehl ist interaktiv (Rückfrage beim System-Laufwerk) und lief bisher headless ohne Konsole, sodass die Bestätigung nie ankam. Läuft jetzt in einem eigenen, sichtbaren (elevated) Fenster, in dem die Rückfrage mit J/Y bestätigt wird – auch locale-unabhängig.

### Geändert
- **Admin-Status wird echt geprüft.** Die „Als Administrator“-Anzeige war fest verdrahtet; sie zeigt jetzt den tatsächlichen Rechtestatus und warnt rot, falls ohne Adminrechte gestartet.

## [5.8] - 2026-06-09

### Behoben
- **Setup**: weißer Rahmen um das Banner auf Willkommens-/Abschlussseite entfernt – das Bild ist jetzt flach im App-Dunkel gehalten und der Bildflächen-Hintergrund (`TBitmapImage.BackColor`) dunkel; dazu ein dezenter Logo-Schein.

## [5.7] - 2026-06-09

### Behoben
- **Setup**: das Zusammenfassungsfeld auf der Seite „Bereit zur Installation“ war noch weiß – ist jetzt ebenfalls dunkel (`TRichEditViewer` mit eingefärbt).

## [5.6] - 2026-06-09

### Geändert
- **Setup im App-Look** – der Installer ist jetzt dunkel gestaltet (Akzentfarbe, helle Schrift, gebrandetes Logo-Banner) statt Standard-Windows-Wizard. (Inno-Setup-Kompilierfehler aus 5.5 behoben.)

## [5.4] - 2026-06-09

### Behoben
- **Update-Erinnerung** – „Später“ bzw. das Wegklicken des Hinweises blendet ihn jetzt nur noch für die aktuelle Sitzung aus; beim nächsten Start erscheint er wieder. Vorher hat das „×“ die Version dauerhaft übersprungen.

## [5.3] - 2026-06-09

### Geändert
- **Update-Download** fühlt sich jetzt wie ein echtes Update an: der Fortschrittsbalken läuft bewusst über einige Sekunden (parallel zum tatsächlichen Download), danach „Wird entpackt …“ und Neustart – statt sofort durchzuspringen.

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
- **Einstellungen** – wählbare Akzentfarbe, Konsole beim Start offen/zu, „immer vor dem Ausführen fragen“ und Benachrichtigungen an/aus

## [4.9] - 2026-06-09

### Behoben
- **Scrollen** – zu lange Inhalte lassen sich jetzt scrollen (vorher abgeschnitten) und verschwinden nicht mehr hinter der Ausgabe-Konsole
- **Fenstergröße** – das rahmenlose Fenster lässt sich an allen Kanten und Ecken größer/kleiner ziehen; kleinere Mindestgröße

## [4.8] - 2026-06-09

### Behoben (Installer)
- **Fehler 740** beim „Windows-Wartung ausführen“ am Setup-Ende behoben (Start jetzt per `shellexec`, löst die UAC-Abfrage korrekt aus)
- **„Neuen Ordner anlegen“**-Button im Zielordner-Auswahldialog ergänzt

## [4.7] - 2026-06-09

### Geändert
- **Übersicht** zeigt jetzt den freien Speicher **aller fest verbauten Laufwerke** (z. B. C: und D:) statt nur des System-Laufwerks

## [4.6] - 2026-06-09

### Neu
- **Dashboard / Übersicht** – neue Startseite mit Live-Anzeigen für Prozessor, Arbeitsspeicher und Festplatte (Ring-Gauges), System-Steckbrief (Windows-Version, Gerät, Speicher, Laufzeit) sowie einem **Gesundheits-Score (0–100)** mit anklickbaren Empfehlungen
- **Erklärungen in einfacher Sprache** – „?“-Button auf jeder Kachel öffnet eine laienverständliche Erklärung (mit Warnhinweis bei riskanten Aktionen)
- **Installer** (Inno Setup) – Setup mit Startmenü-Eintrag und sauberer Deinstallation; das Release liefert jetzt zusätzlich `WindowsWartung-Setup.exe`

## [4.5] - 2026-06-03

### Behoben
- **Fenster im Vordergrund** – die App holt sich beim Start zuverlässig den Fokus (auch als elevierte/UAC-Anwendung), statt im Hintergrund zu öffnen

## [4.4] - 2026-06-03

### Behoben
- **Update-Erkennung** – die Prüfung auf neue Versionen startet jetzt erst, wenn die Oberfläche bereit ist („ready“-Handshake). Vorher konnte die „Update verfügbar“-Meldung bei kaltem Start verloren gehen, weil sie zu früh an das noch nicht geladene UI geschickt wurde.

## [4.3] - 2026-06-03

### Neu
- **In-App-Update** – „Update verfügbar“-Dialog mit *Jetzt herunterladen / Später*, Download-Fortschrittsbalken, automatischer Datei-Tausch über einen Helfer, Neustart und Erfolgsmeldung „Erfolgreich auf vX.X aktualisiert“

### Geändert
- Der Update-Hinweis lädt jetzt direkt **in der App** statt nur den Browser zu öffnen (Browser bleibt als Fallback)

## [4.2] - 2026-06-03

### Neu
- **Auto-Update** – beim Start prüft die App per GitHub-API auf ein neueres Release und blendet bei Bedarf oben einen Hinweis mit „Herunterladen“ ein (inkl. „diese Version überspringen“). Ohne veröffentlichtes Release oder ohne Internet passiert nichts.
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
