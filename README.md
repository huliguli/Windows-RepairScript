# Windows Reparaturassistent

Ein kleines Batch-Tool, das die wichtigsten Windows-Reparaturbefehle (DISM und SFC) in der richtigen Reihenfolge ausführt – mit Menü, Protokoll und optionalem Neustart bzw. Herunterfahren.

Entstanden, weil ich diese Befehle ständig einzeln eingetippt habe.

## Funktionen

- **Automatische Adminrechte** – fragt per UAC nach, kein manuelles „Als Administrator ausführen" nötig
- **Auswählbarer Umfang**
  - Komplett (DISM + SFC)
  - Nur DISM (Komponentenspeicher)
  - Nur SFC (Systemdateien)
  - Komplett + CHKDSK (Datenträgerprüfung beim nächsten Neustart)
- **Richtige Reihenfolge**: DISM `ScanHealth` → `RestoreHealth` → `sfc /scannow`
- **Aktion danach frei wählbar**: nichts / Herunterfahren / Neustart – mit eigener Verzögerung
- **Protokoll** jeder Ausführung im Ordner `logs\`

## Voraussetzungen

- Windows 10 oder 11
- Adminrechte (fordert das Skript selbst an)
- Internetverbindung für `DISM /RestoreHealth` (lädt fehlende Dateien über Windows Update nach)

## Nutzung

1. `sfcscript.bat` herunterladen
2. Doppelklick – die Admin-Abfrage kommt automatisch
3. Im Menü Umfang und Aktion danach wählen

> [!NOTE]
> DISM und SFC können je nach System mehrere Minuten dauern. Das Fenster „hängt" nicht – es arbeitet.

## Menü

Im Terminal ist das Ganze farbig (Akzentfarben, Badges, Zahlen-Chips). Als Textvorschau:

```
   WINDOWS REPARATURASSISTENT
   by Jonas | v2.0

   Was soll gemacht werden?

    1   Komplett           DISM + SFC          empfohlen
    2   Nur DISM           Komponentenspeicher
    3   Nur SFC            Systemdateien
    4   Komplett + CHKDSK  Datenträger prüfen

    0   Beenden
```

## Protokolle

- `logs\reparatur_<Zeitstempel>.log` – Kurzprotokoll des Durchlaufs (Auswahl + Exit-Codes)
- `logs\dism_<Zeitstempel>.log` – ausführliches DISM-Log
- Ausführliche SFC-Ergebnisse stehen wie immer in `C:\Windows\Logs\CBS\CBS.log`

## Warum Neustart statt „einfach Herunterfahren"?

Wegen **Schnellstart** (Fast Startup) ist ein normales Herunterfahren unter Windows 10/11 kein vollständiger Neustart – der Kernel wird nicht komplett neu geladen. Reparaturen, die einen echten Reboot brauchen, werden dabei nicht sauber abgeschlossen. Wer neu starten lässt, bekommt deshalb `shutdown -r` (echter Neustart), nicht `-s`.

## Lizenz

MIT – siehe [LICENSE](LICENSE).
