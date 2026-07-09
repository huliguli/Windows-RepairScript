using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;

namespace WartungsToolbox
{
    // Geplante Wartung ueber die Windows-Aufgabenplanung (schtasks).
    // Die Anzeige-Parameter werden zusaetzlich lokal gespeichert (sprachunabhaengig,
    // da die schtasks-Textausgabe lokalisiert und damit unzuverlaessig zu parsen waere).
    static class Scheduler
    {
        public const string TaskName = "WindowsWartung-AutoWartung";

        public static bool Exists()
        {
            return RunCode("schtasks.exe", "/Query /TN \"" + TaskName + "\"") == 0;
        }

        // mode ("daily"/"weekly"/"monthly") und dSpec sind vom Aufrufer validiert:
        // weekly -> Tages-Liste "MON,WED,FRI" (Whitelist-Tokens), monthly -> Monatstag "1".."31".
        public static bool Create(string mode, string dSpec, string hh, string mm, string exePath)
        {
            string sc = mode == "weekly" ? "WEEKLY" : (mode == "monthly" ? "MONTHLY" : "DAILY");
            string args =
                "/Create /TN \"" + TaskName + "\" " +
                "/TR \"\\\"" + exePath + "\\\" --auto\" " +
                "/SC " + sc + " /ST " + hh + ":" + mm + " /RL HIGHEST /F";
            if (mode == "weekly" || mode == "monthly") args += " /D " + dSpec;
            return RunCode("schtasks.exe", args) == 0;
        }

        public static void Delete()
        {
            RunCode("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F");
        }

        static int RunCode(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = file;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.OutputDataReceived += delegate { };
                    p.ErrorDataReceived += delegate { };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return -1; }
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        // ---- gespeicherte Anzeige-Parameter ----
        static string CfgPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsWartung", "schedule.json");
        }

        // days = Wochentage (weekly), dom = Monatstag (monthly), actions = gewaehlte
        // Aufgaben-Schluessel oder null (= Standard-Satz; bewusst NICHT gespeichert,
        // damit ein spaeter geaenderter Standard automatisch gilt).
        public static void Write(string mode, string[] days, int dom, string time, string[] actions)
        {
            try
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["mode"] = mode;
                if (mode == "weekly" && days != null) d["days"] = days;
                if (mode == "monthly") d["dom"] = dom;
                d["time"] = time;
                if (actions != null && actions.Length > 0) d["actions"] = actions;
                Directory.CreateDirectory(Path.GetDirectoryName(CfgPath()));
                File.WriteAllText(CfgPath(), new JavaScriptSerializer().Serialize(d));
            }
            catch { }
        }

        // Gewaehlte Aufgaben-Schluessel fuer den --auto-Lauf (null = Standard-Satz).
        public static string[] ReadActions()
        {
            try
            {
                Dictionary<string, object> d = Read() as Dictionary<string, object>;
                if (d == null || !d.ContainsKey("actions")) return null;
                object[] arr = d["actions"] as object[];
                if (arr == null || arr.Length == 0) return null;
                List<string> keys = new List<string>();
                foreach (object o in arr) { string s = o as string; if (!string.IsNullOrEmpty(s)) keys.Add(s); }
                return keys.Count > 0 ? keys.ToArray() : null;
            }
            catch { return null; }
        }

        public static object Read()
        {
            try
            {
                if (!File.Exists(CfgPath())) return null;
                return new JavaScriptSerializer().DeserializeObject(File.ReadAllText(CfgPath()));
            }
            catch { return null; }
        }

        public static void Clear()
        {
            try { if (File.Exists(CfgPath())) File.Delete(CfgPath()); }
            catch { }
        }
    }
}
