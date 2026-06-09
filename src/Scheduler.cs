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

        // mode ("daily"/"weekly"), day (MON..SUN), hh/mm ("00".."59") sind vom Aufrufer validiert.
        public static bool Create(string mode, string day, string hh, string mm, string exePath)
        {
            string sc = (mode == "weekly") ? "WEEKLY" : "DAILY";
            string args =
                "/Create /TN \"" + TaskName + "\" " +
                "/TR \"\\\"" + exePath + "\\\" --auto\" " +
                "/SC " + sc + " /ST " + hh + ":" + mm + " /RL HIGHEST /F";
            if (mode == "weekly") args += " /D " + day;
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

        public static void Write(string mode, string day, string time)
        {
            try
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["mode"] = mode;
                d["day"] = day;
                d["time"] = time;
                Directory.CreateDirectory(Path.GetDirectoryName(CfgPath()));
                File.WriteAllText(CfgPath(), new JavaScriptSerializer().Serialize(d));
            }
            catch { }
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
