# Windows-Wartung

Eine native Windows-Wartungs- und Reparatur-Toolbox mit grafischer Oberfläche. Bündelt die Befehle, die man sonst einzeln in der Eingabeaufforderung eintippt – DISM, SFC, Netzwerk-Reset, Aufräumen, Diagnose – in einem aufgeräumten Tool mit Kategorien, Live-Ausgabe und optionalem Wiederherstellungspunkt.

Kompiliert als echte `.exe` mit dem in Windows enthaltenen C#-Compiler – **kein .NET SDK nötig**.

## Funktionen

**Reparieren** — Komplett-Reparatur (DISM + SFC), DISM RestoreHealth, SFC scannow & nur prüfen, WinSxS aufräumen, Komponentenspeicher analysieren, Windows-Update reparieren, CHKDSK planen

**Netzwerk** — Netzwerk-Reset (DNS / Winsock / IP), DNS-Cache leeren, IP-Adresse erneuern

**Aufräumen** — Temp-Dateien, Windows-Update-Cache, Papierkorb, Datenträgerbereinigung

**Diagnose** — System-Übersicht, Festplatten-Gesundheit (SMART), Akkubericht, Defender-Schnellscan, RAM-Diagnose

Dazu:

- Dunkle Oberfläche mit Kategorie-Sidebar und Live-Ausgabe (inkl. Log-Export)
- Optionaler **Wiederherstellungspunkt** vor jeder Reparatur (ein Klick im Kopfbereich)
- Startet automatisch mit Administratorrechten (UAC)

## Bauen

Voraussetzung: Windows 10 oder 11. Der nötige Compiler (`csc.exe`) ist Teil des .NET Framework und damit bereits installiert – es muss nichts nachgeladen werden.

```powershell
.\build.ps1
```

Ergebnis: `bin\WindowsWartung.exe`. Das App-Icon wird beim ersten Build automatisch erzeugt.

## Nutzen

`bin\WindowsWartung.exe` doppelklicken → UAC bestätigen → links eine Kategorie wählen → Kachel anklicken. Die Ausgabe läuft unten live mit; **Log speichern** schreibt sie in eine Textdatei.

## Projektstruktur

```
src/            C#-Quellcode + App-Manifest
assets/         App-Icon
tools/          Icon-Generator, Smoke-Test
build.ps1       Build über das eingebaute csc.exe (ohne SDK)
sfcscript.bat   schlanke Batch-Version (Vorgänger)
```

## Hinweise

- DISM und SFC brauchen je nach System ein paar Minuten – das Fenster arbeitet, auch wenn die Ausgabe kurz still steht.
- Netzwerk-Reset und RAM-Diagnose erfordern anschließend einen Neustart.
- Ausführliche SFC-Ergebnisse stehen wie immer in `C:\Windows\Logs\CBS\CBS.log`.

## Lizenz

MIT – siehe [LICENSE](LICENSE).
