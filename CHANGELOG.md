# Changelog

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
