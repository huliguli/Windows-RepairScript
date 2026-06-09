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

        static string FilePath()
        {
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
            List<object> list = new List<object>();
            try
            {
                string p = FilePath();
                if (!File.Exists(p)) return list;
                string json = File.ReadAllText(p);
                if (string.IsNullOrEmpty(json)) return list;
                JavaScriptSerializer js = new JavaScriptSerializer();
                object[] arr = js.DeserializeObject(json) as object[];
                if (arr != null)
                    foreach (object o in arr) list.Add(o);
            }
            catch { }
            return list;
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

                    string dir = Path.GetDirectoryName(FilePath());
                    Directory.CreateDirectory(dir);
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    File.WriteAllText(FilePath(), js.Serialize(list));
                }
                catch { }
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                try { string p = FilePath(); if (File.Exists(p)) File.Delete(p); }
                catch { }
            }
        }
    }
}
