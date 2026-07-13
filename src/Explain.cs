using System;

namespace WartungsToolbox
{
    // Laienverstaendliche Deutung bekannter Fehler-Codes und Tool-Ausgaben.
    // WICHTIG: Hier stehen NUR offiziell dokumentierte Windows-Fehlercodes (winerror.h)
    // und die offiziellen SFC-/DISM-Meldungstexte (deutsch + englisch). Unbekannte
    // Codes werden bewusst NICHT gedeutet (null) - lieber keine Erklaerung als eine
    // erfundene.
    static class Explain
    {
        // Bekannte Exit-Codes -> Bedeutung + konkreter Loesungsweg (sonst null).
        public static string ForExit(int code)
        {
            switch (code)
            {
                case 2:                                   // ERROR_FILE_NOT_FOUND
                case unchecked((int)0x80070002):
                    return "Eine benötigte Datei wurde nicht gefunden. → Die Aktion erneut ausführen; besteht der Fehler, den PC neu starten und noch einmal versuchen.";
                case 5:                                   // ERROR_ACCESS_DENIED
                case unchecked((int)0x80070005):
                    return "Zugriff verweigert. Häufig blockiert ein Virenschutz, oder eine Datei ist gerade in Benutzung. → Andere Programme schließen und erneut versuchen.";
                case 87:                                  // ERROR_INVALID_PARAMETER
                    return "Ungültiger Parameter. Diese Windows-Version kennt den Befehl möglicherweise nicht vollständig. → Windows über die Einstellungen aktualisieren und erneut versuchen.";
                case 740:                                 // ERROR_ELEVATION_REQUIRED
                    return "Administratorrechte erforderlich. → Die App per Rechtsklick als Administrator starten.";
                case 1053:                                // ERROR_SERVICE_REQUEST_TIMEOUT
                    return "Ein Windows-Dienst hat nicht rechtzeitig reagiert. → Den PC neu starten und die Aktion wiederholen.";
                case 1058:                                // ERROR_SERVICE_DISABLED
                    return "Ein benötigter Windows-Dienst ist deaktiviert. → Windows-Taste + R, dann services.msc: den betroffenen Dienst auf Starttyp Manuell oder Automatisch stellen.";
                case 1060:                                // ERROR_SERVICE_DOES_NOT_EXIST
                    return "Ein benötigter Windows-Dienst ist auf diesem PC nicht vorhanden – die Funktion ist hier vermutlich nicht verfügbar.";
                case 1223:                                // ERROR_CANCELLED
                    return "Die Aktion wurde abgebrochen – vermutlich wurde eine Windows-Nachfrage verneint. → Erneut ausführen und die Nachfrage bestätigen.";
                case unchecked((int)0x800F081F):          // CBS_E_SOURCE_MISSING
                    return "Windows konnte die Reparatur-Quelldateien nicht beschaffen (0x800F081F). → Internetverbindung prüfen, dann die Aktion Windows-Update reparieren ausführen und die Reparatur wiederholen.";
                case unchecked((int)0x800F0906):          // Quelle nicht herunterladbar
                    return "Die Reparatur-Quelldateien konnten nicht heruntergeladen werden (0x800F0906). → Internetverbindung prüfen und erneut versuchen.";
                case unchecked((int)0x800F0954):          // WSUS-Quelle
                    return "Dieser PC bezieht Updates von einem Firmen- oder Schul-Server (WSUS), der die Reparatur-Quelle nicht bereitstellt (0x800F0954). → Den zuständigen Administrator kontaktieren.";
                default:
                    return null;
            }
        }

        // Deutet die (gepufferte) Ausgabe von SFC bzw. DISM anhand der offiziellen
        // Meldungstexte. good=true => beruhigende Zusammenfassung, sonst Hinweis mit
        // Loesungsweg. Liefert null, wenn kein bekannter Text vorkommt.
        public static string ForOutput(string file, string tail, out bool good)
        {
            good = false;
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(tail)) return null;
            string f = file.ToLowerInvariant();
            string t = tail.ToLowerInvariant();

            if (f.EndsWith("sfc.exe"))
            {
                // Offizielle Ausgaenge des Windows-Ressourcenschutzes (DE/EN)
                if (t.Contains("nicht reparieren") || t.Contains("unable to fix"))
                    return "Verständlich gesagt: Es wurden beschädigte Systemdateien gefunden, einige konnten nicht repariert werden. → Erst die Aktion DISM RestoreHealth ausführen, danach SFC scannow wiederholen. Details stehen in C:\\Windows\\Logs\\CBS\\CBS.log.";
                if (t.Contains("erfolgreich repariert") || t.Contains("successfully repaired"))
                {
                    good = true;
                    return "Verständlich gesagt: Beschädigte Systemdateien wurden gefunden und repariert. → Zum Abschluss den PC neu starten.";
                }
                if (t.Contains("keine integrit") || t.Contains("did not find any integrity"))
                {
                    good = true;
                    return "Verständlich gesagt: Alle geschützten Systemdateien sind in Ordnung – nichts zu reparieren.";
                }
                if (t.Contains("reparaturdienst nicht starten") || t.Contains("could not start the repair service"))
                    return "Verständlich gesagt: Der Windows-Reparaturdienst ließ sich nicht starten. → Den PC neu starten und die Aktion wiederholen.";
                return null;
            }

            if (f.EndsWith("dism.exe"))
            {
                // Unklare Negativ-Formulierungen bewusst nicht deuten (keine Falschaussage riskieren)
                if (t.Contains("nicht reparierbar") || t.Contains("not repairable")) return null;

                if ((t.Contains("quelldateien") && t.Contains("nicht gefunden")) || t.Contains("source files could not be found"))
                    return "Verständlich gesagt: Windows fand die Reparatur-Quelldateien nicht. → Internetverbindung prüfen, dann Windows-Update reparieren ausführen und die Reparatur wiederholen.";
                if (t.Contains("keine komponentenspeicherbeschädigung") || t.Contains("no component store corruption"))
                {
                    good = true;
                    return "Verständlich gesagt: Der Windows-Komponentenspeicher ist gesund – keine Beschädigung gefunden.";
                }
                if (t.Contains("ist reparierbar") || t.Contains("store is repairable"))
                    return "Verständlich gesagt: Es wurden reparierbare Beschädigungen gefunden. → Als Nächstes die Aktion DISM RestoreHealth ausführen.";
                if (t.Contains("wiederherstellung wurde abgeschlossen") || t.Contains("restore operation completed"))
                {
                    good = true;
                    return "Verständlich gesagt: Die Reparatur des Komponentenspeichers wurde abgeschlossen.";
                }
                return null;
            }

            return null;
        }
    }
}
