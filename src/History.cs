using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using WartungsToolbox.Kern;

namespace WartungsToolbox
{
    // Reparatur-Verlauf: jede Ausfuehrung (Zeit, Aktion, Ergebnis, Dauer, seit 8.2 optional die
    // Sicherungsdatei einer Massnahme) wird nach %ProgramData%\WindowsWartung\verlauf\history.json
    // geschrieben (neueste zuerst, begrenzt).
    //
    // Maschinenweit seit 8.1 (Rechte-Modell B): die Oberflaeche laeuft ohne Rechte, der
    // Helfer und die geplante Wartung (--auto) laufen erhoeht - alle drei schreiben denselben
    // Verlauf. Im Nutzerprofil (%LOCALAPPDATA%) fand der erhoehte Lauf ueber ein fremdes
    // Konto die Datei nicht wieder. Den Ort bestimmt Kern.Ablage (weicht ohne Schreibrecht
    // selbst ins Profil aus); eine vorhandene 8.0-Datei wird beim ersten Zugriff uebernommen
    // und im Profil auf history.json.uebernommen umbenannt.
    //
    // Geschrieben wird nicht, wenn der Verlaufsordner hinter einer Abzweigung liegt
    // (Ablage.IstAbzweigung): BUILTIN\Users darf im Laufzeitordner aendern, also kann ein
    // anderes lokales Konto verlauf\ leeren und als Junction auf einen fremden Ort neu anlegen;
    // der erhoehte Helfer oder --auto wuerde dort dann ersetzen (File.Replace) und loeschen.
    // Der Eintrag geht in dem Fall verloren, mit Warnung im app.log; das ist besser als ein
    // erhoehter Schreibzugriff an einen Ort, den ein anderes Konto bestimmt hat.
    static class History
    {
        static readonly object _lock = new object();
        const int Max = 200;

        /// <summary>
        /// Nur fuer die Probe in tests/: verlegt den Verlauf in einen Wegwerf-Ordner.
        /// Ohne das muesste ein Test den echten Verlauf des Nutzers anfassen, und ein Test,
        /// der die Daten kaputtmachen kann, die er schuetzen soll, ist keiner.
        /// Im laufenden Programm bleibt das Feld immer null. Es geht der Ablage vor, damit
        /// die Probe nie ProgramData beruehrt.
        /// </summary>
        internal static string PfadFuerProbe;

        // Einmal je Prozess ermittelt; die Uebernahme der alten Datei haengt daran. null nach
        // der Ermittlung heisst: kein Ort (der Verlaufsordner liegt hinter einer Abzweigung),
        // dann wird nichts gelesen und nichts geschrieben, mit Warnung je Versuch.
        static string _pfad;
        static bool _pfadErmittelt;

        static string FilePath()
        {
            if (!string.IsNullOrEmpty(PfadFuerProbe)) return PfadFuerProbe;
            lock (_lock)
            {
                if (!_pfadErmittelt) { _pfad = PfadErmitteln(); _pfadErmittelt = true; }
                return _pfad;
            }
        }

        /// <summary>
        /// Neuer Ort ueber Kern.Ablage plus einmalige Uebernahme aus dem Nutzerprofil (8.0 → 8.1).
        /// Kopiert werden history.json und die Sicherung .alt, aber nur, solange am neuen Ort
        /// noch keine history.json liegt: sonst wuerde eine uralte .alt neben eine frische
        /// Hauptdatei geraten. Jede kopierte Datei wird im Profil sofort auf *.uebernommen
        /// umbenannt (AppLog.UebernahmeAbschliessen): Ablage.Uebernehmen kopiert, sobald am
        /// neuen Ort nichts liegt, und ohne das Umbenennen waere ein geleerter Verlauf beim
        /// naechsten Start wieder da; bei UAC ueber die Schulter zoege der Helfer zudem den
        /// Verlauf aus dem Adminprofil nach ProgramData.
        ///
        /// Liegt am neuen Ort schon eine history.json (zweites Konto, das 8.0 selbst genutzt
        /// hat), werden die alten Dateien dieses Profils trotzdem abgehakt, nur ohne Kopie:
        /// sonst blieben sie liegen, und nach "Verlauf leeren" (Clear loescht nur ProgramData)
        /// kaemen sie beim naechsten Prozess dieses Kontos als Verlauf zurueck, weil der Ort je
        /// Prozess neu ermittelt wird.
        /// </summary>
        static string PfadErmitteln()
        {
            string neu;
            try
            {
                // Ablage.Verlauf liefert null, wenn verlauf\ oder der Ordner darueber hinter einer
                // Abzweigung liegt: dann gibt es keinen Ort, an den erhoeht geschrieben werden darf.
                string ordner = Ablage.Verlauf();
                if (ordner == null)
                {
                    AppLog.Warn("Verlaufsordner liegt hinter einer Abzweigung (Junction oder symbolischer Link); der Verlauf wird in dieser Sitzung weder gelesen noch geschrieben.");
                    return null;
                }
                neu = Path.Combine(ordner, "history.json");
            }
            catch (Exception ex)
            {
                // Ablage wirft praktisch nie; falls doch, bleibt der Verlauf im Profil statt gar nicht.
                AppLog.Warn("Verlaufsordner nicht ermittelbar, Verlauf bleibt im Nutzerprofil: " + ex.Message);
                return AlterPfad();
            }

            try
            {
                string alt = AlterPfad();
                bool neuFehlt = !File.Exists(neu);
                if (Ablage.Uebernehmen(alt, neu))
                {
                    bool abgehakt = AppLog.UebernahmeAbschliessen(alt);
                    AppLog.Info("Verlauf aus dem Nutzerprofil übernommen: " + alt + " nach " + neu
                                + (abgehakt ? "" : " (alte Datei ließ sich nicht umbenennen)"));
                }
                else if (!neuFehlt && File.Exists(alt))
                {
                    bool abgehakt = AppLog.UebernahmeAbschliessen(alt);
                    AppLog.Info("Verlauf aus dem Nutzerprofil nicht übernommen, am neuen Ort liegt schon einer: " + alt
                                + (abgehakt ? " auf .uebernommen umbenannt" : " ließ sich nicht umbenennen"));
                }
                if (neuFehlt && Ablage.Uebernehmen(alt + ".alt", neu + ".alt"))
                    AppLog.UebernahmeAbschliessen(alt + ".alt");
                else if (!neuFehlt && File.Exists(alt + ".alt"))
                    AppLog.UebernahmeAbschliessen(alt + ".alt");
            }
            catch (Exception ex) { AppLog.Warn("Verlauf konnte nicht übernommen werden: " + ex.Message); }
            return neu;
        }

        /// <summary>Der Ort bis 8.0: %LOCALAPPDATA%\WindowsWartung\history.json.</summary>
        static string AlterPfad()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Ablage.Ordnername, "history.json");
        }

        // Liste der Eintraege (neueste zuerst), direkt JSON-tauglich fuer das UI.
        public static List<object> List()
        {
            lock (_lock) { return Load(); }
        }

        static List<object> Load()
        {
            // Erst die richtige Datei, dann die Sicherungskopie. Ist die Hauptdatei kaputt
            // (Absturz oder Stromausfall mitten im Schreiben), war der Verlauf frueher
            // ersatzlos weg: der leere catch lieferte einfach eine leere Liste, und der
            // naechste Eintrag hat den Rest ueberschrieben.
            string pfad = FilePath();
            if (pfad == null) return new List<object>();   // kein Ort (Abzweigung), siehe FilePath
            List<object> list = Lies(pfad);
            if (list == null) list = Lies(pfad + ".alt");
            return list ?? new List<object>();
        }

        /// <summary>Liest eine Verlaufsdatei. null heisst "nicht lesbar", eine leere Liste "leer".</summary>
        static List<object> Lies(string pfad)
        {
            try
            {
                if (!File.Exists(pfad)) return null;
                string json = File.ReadAllText(pfad);
                if (string.IsNullOrEmpty(json)) return null;
                JavaScriptSerializer js = new JavaScriptSerializer();
                object[] arr = js.DeserializeObject(json) as object[];
                if (arr == null) return null;

                List<object> list = new List<object>();
                foreach (object o in arr) list.Add(o);
                return list;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Verlauf '" + Path.GetFileName(pfad) + "' ist nicht lesbar: " + ex.Message);
                return null;
            }
        }

        // Einen Lauf protokollieren. kind ist der UI-Kind-String ("good"/"bad"/"warn"/"norm").
        public static void Add(string action, string kind, string message, double seconds)
        {
            Add(action, kind, message, seconds, null);
        }

        /// <summary>
        /// Wie oben, mit der Sicherungsdatei einer Massnahme (Meilenstein 3, docs/M3-ENTWURF.md
        /// Abschnitt 9.1): sicherung ist NUR der Dateiname unter sicherungen\massnahmen, nie ein
        /// Pfad; null oder leer heisst "kein Rueckweg", dann fehlt das Feld im Eintrag wie bei
        /// allen Eintraegen bis 8.1. Ob die Datei noch liegt, prueft der Host beim Lesen
        /// (ShellForm, Nachricht history), nicht der Verlauf: der wird auch vom erhoehten Helfer
        /// und der geplanten Wartung geschrieben und soll keinen Ordner anfassen. Ein Pfadanteil
        /// wird hier abgeschnitten, damit nie ein fremder Ort in der Datei landet.
        /// </summary>
        public static void Add(string action, string kind, string message, double seconds, string sicherung)
        {
            lock (_lock)
            {
                try
                {
                    if (OrdnerIstAbzweigung())
                    {
                        string pfad = FilePath();
                        AppLog.Warn("Verlaufseintrag „" + (action ?? "") + "“ nicht geschrieben: der Verlaufsordner "
                                    + (pfad == null ? "" : Path.GetDirectoryName(pfad) + " ") + "liegt hinter einer Abzweigung (Junction oder symbolischer Link).");
                        return;
                    }
                    List<object> list = Load();

                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    entry["action"] = action == null ? "" : action;
                    entry["kind"] = string.IsNullOrEmpty(kind) ? "norm" : kind;
                    entry["message"] = message == null ? "" : message;
                    entry["seconds"] = Math.Round(seconds, 1);
                    // Optional (8.2): nur der Dateiname, alte Eintraege ohne das Feld bleiben lesbar,
                    // weil jeder Eintrag ein freies Woerterbuch ist und die Oberflaeche fehlende
                    // Felder als "kein Rueckweg" liest.
                    string sicherungName = SicherungName(sicherung);
                    if (sicherungName != null) entry["sicherung"] = sicherungName;

                    list.Insert(0, entry);
                    while (list.Count > Max) list.RemoveAt(list.Count - 1);

                    JavaScriptSerializer js = new JavaScriptSerializer();
                    SchreibeSicher(js.Serialize(list));
                }
                catch (Exception ex) { AppLog.Warn("Verlauf konnte nicht geschrieben werden: " + ex.Message); }
            }
        }

        /// <summary>
        /// Dateiname der Sicherung, wie ihn die Kennung rueckweg annimmt (Buchstaben, Ziffern,
        /// Punkt, Unterstrich, Bindestrich, Endung .json, hoechstens 200 Zeichen); null bei
        /// leer oder ungueltig. Ein Pfad wird vorher auf seinen Dateinamen gekuerzt.
        /// </summary>
        static string SicherungName(string sicherung)
        {
            if (string.IsNullOrEmpty(sicherung)) return null;
            string name;
            try { name = Path.GetFileName(sicherung); }
            catch (Exception) { return null; }
            if (string.IsNullOrEmpty(name) || name.Length > 200) return null;
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (char c in name)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                          || c == '.' || c == '_' || c == '-';
                if (!ok) return null;
            }
            return name;
        }

        /// <summary>
        /// Liegt der Verlaufsordner hinter einer Abzweigung (Ablage.IstAbzweigung: der Pfad oder
        /// eine Komponente unterhalb des maschinenweiten Ordners)? Die Probe (PfadFuerProbe) liegt
        /// ausserhalb der Ablage und wird nicht geprueft. Wirft die Pruefung, gilt "ja": was sich
        /// nicht pruefen laesst, bekommt keinen Schreibzugriff.
        /// </summary>
        static bool OrdnerIstAbzweigung()
        {
            if (!string.IsNullOrEmpty(PfadFuerProbe)) return false;
            try
            {
                string pfad = FilePath();
                if (pfad == null) return true;   // Ablage.Verlauf hat den Ort schon verweigert
                string ordner = Path.GetDirectoryName(pfad);
                return string.IsNullOrEmpty(ordner) || Ablage.IstAbzweigung(ordner);
            }
            catch (Exception) { return true; }
        }

        /// <summary>
        /// Schreibt den Verlauf so, dass es keinen Zwischenzustand gibt.
        ///
        /// File.WriteAllText kuerzt die Zieldatei zuerst auf null und schreibt dann. Wer in
        /// genau diesem Moment den Strom verliert, hat hinterher eine halbe Datei - und die
        /// ist als JSON unlesbar, also war der gesamte Verlauf verloren. Stattdessen wird
        /// daneben geschrieben und erst dann getauscht: File.Replace ist auf NTFS unteilbar
        /// und legt die vorherige Fassung als .alt daneben.
        ///
        /// File.Replace braucht Loeschrecht auf der Zieldatei. Hat sie der erhoehte Helfer
        /// angelegt, reicht das dem nicht erhoehten Host nur, weil der Ordner BUILTIN\Users
        /// Aenderungsrechte vererbt (Ablage.RechteSichern, Installer [Dirs] users-modify).
        /// </summary>
        static void SchreibeSicher(string json)
        {
            string ziel = FilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(ziel));
            string neu = ziel + ".neu";
            string alt = ziel + ".alt";

            File.WriteAllText(neu, json);
            if (File.Exists(ziel))
            {
                File.Replace(neu, ziel, alt, true);
            }
            else
            {
                // Erster Lauf: es gibt noch nichts zu ersetzen.
                File.Move(neu, ziel);
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                // Auch die Sicherungskopie muss weg: sonst taucht der geleerte Verlauf
                // beim naechsten unsauberen Schreibvorgang wieder auf. Das Nutzerprofil wird
                // hier nicht angefasst: die 8.0-Dateien sind seit der Uebernahme *.uebernommen
                // (PfadErmitteln), also kommt nichts mehr zurueck.
                string pfad = FilePath();
                if (pfad == null)
                {
                    AppLog.Warn("Verlauf nicht geleert: der Verlaufsordner liegt hinter einer Abzweigung (Junction oder symbolischer Link).");
                    return;
                }
                var dateien = new List<string> { pfad, pfad + ".alt", pfad + ".neu" };
                foreach (string p in dateien)
                {
                    try { if (File.Exists(p)) File.Delete(p); }
                    catch (Exception ex) { AppLog.Warn("Verlauf '" + Path.GetFileName(p) + "' ließ sich nicht löschen: " + ex.Message); }
                }
            }
        }
    }
}
