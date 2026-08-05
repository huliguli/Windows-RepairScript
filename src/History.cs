using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace WartungsToolbox
{
    // Reparatur-Verlauf: jede Ausfuehrung (Zeit, Aktion, Ergebnis, Dauer) wird nach
    // %LOCALAPPDATA%\WindowsWartung\history.json geschrieben (neueste zuerst, begrenzt).
    static class History
    {
        static readonly object _lock = new object();
        const int Max = 200;

        /// <summary>
        /// Nur fuer die Probe in tests/: verlegt den Verlauf in einen Wegwerf-Ordner.
        /// Ohne das muesste ein Test den echten Verlauf des Nutzers anfassen, und ein Test,
        /// der die Daten kaputtmachen kann, die er schuetzen soll, ist keiner.
        /// Im laufenden Programm bleibt das Feld immer null.
        /// </summary>
        internal static string PfadFuerProbe;

        static string FilePath()
        {
            if (!string.IsNullOrEmpty(PfadFuerProbe)) return PfadFuerProbe;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsWartung", "history.json");
        }

        // Liste der Eintraege (neueste zuerst) – direkt JSON-tauglich fuer das UI.
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
            List<object> list = Lies(FilePath());
            if (list == null) list = Lies(FilePath() + ".alt");
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
            lock (_lock)
            {
                try
                {
                    List<object> list = Load();

                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    entry["action"] = action == null ? "" : action;
                    entry["kind"] = string.IsNullOrEmpty(kind) ? "norm" : kind;
                    entry["message"] = message == null ? "" : message;
                    entry["seconds"] = Math.Round(seconds, 1);

                    list.Insert(0, entry);
                    while (list.Count > Max) list.RemoveAt(list.Count - 1);

                    JavaScriptSerializer js = new JavaScriptSerializer();
                    SchreibeSicher(js.Serialize(list));
                }
                catch (Exception ex) { AppLog.Warn("Verlauf konnte nicht geschrieben werden: " + ex.Message); }
            }
        }

        /// <summary>
        /// Schreibt den Verlauf so, dass es keinen Zwischenzustand gibt.
        ///
        /// File.WriteAllText kuerzt die Zieldatei zuerst auf null und schreibt dann. Wer in
        /// genau diesem Moment den Strom verliert, hat hinterher eine halbe Datei - und die
        /// ist als JSON unlesbar, also war der gesamte Verlauf verloren. Stattdessen wird
        /// daneben geschrieben und erst dann getauscht: File.Replace ist auf NTFS unteilbar
        /// und legt die vorherige Fassung als .alt daneben.
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
                // beim naechsten unsauberen Schreibvorgang wieder auf.
                foreach (string p in new[] { FilePath(), FilePath() + ".alt", FilePath() + ".neu" })
                {
                    try { if (File.Exists(p)) File.Delete(p); }
                    catch (Exception ex) { AppLog.Warn("Verlauf '" + Path.GetFileName(p) + "' liess sich nicht loeschen: " + ex.Message); }
                }
            }
        }
    }
}
