using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Web.Script.Serialization;

namespace WartungsToolbox
{
    /// <summary>
    /// Ein Ort fuer das Starten von Hilfsprozessen. Vorher lag derselbe Aufruf viermal
    /// im Projekt, zweimal davon zeichengleich, und zwei Fassungen dekodierten die Ausgabe
    /// als UTF-8, obwohl powershell.exe in der Konsolen-Codepage schreibt - Umlaute kamen
    /// dort als Fragezeichen an.
    ///
    /// Ausserdem wurde bei mehreren Fassungen stderr umgeleitet, aber nie gelesen. Fuellt
    /// sich dessen Puffer, blockiert der Kindprozess bis zum Zeitlimit und die Funktion
    /// liefert eine leere Liste ohne jeden Hinweis. Hier werden beide Stroeme gelesen.
    /// </summary>
    static class Shell
    {
        static readonly Encoding Oem = GetOem();
        static Encoding GetOem()
        {
            try { return Encoding.GetEncoding((int)Native.GetOEMCP()); }
            catch { return Encoding.Default; }
        }

        public class Result
        {
            public int ExitCode = -1;
            public string Output = "";
            public string Error = "";
            public bool TimedOut;
            public bool Started;

            public bool Ok { get { return Started && !TimedOut && ExitCode == 0; } }
        }

        /// <summary>
        /// Fuehrt ein Programm aus und wartet hoechstens timeoutMs. Nur aus einem
        /// Hintergrund-Thread aufrufen.
        /// </summary>
        public static Result Run(string file, string args, int timeoutMs)
        {
            Result r = new Result();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Oem,
                    StandardErrorEncoding = Oem,
                };

                using (Process p = Process.Start(psi))
                {
                    r.Started = true;
                    StringBuilder outBuf = new StringBuilder();
                    StringBuilder errBuf = new StringBuilder();

                    // Beide Stroeme asynchron leeren, sonst blockiert der Kindprozess,
                    // sobald ein Puffer voll ist.
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) outBuf.AppendLine(e.Data); };
                    p.ErrorDataReceived  += (s, e) => { if (e.Data != null) errBuf.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(timeoutMs))
                    {
                        r.TimedOut = true;
                        KillTree(p.Id);
                        AppLog.Warn("Zeitlimit erreicht: " + file + " " + args);
                    }
                    else
                    {
                        p.WaitForExit();   // wartet, bis die Leser fertig sind
                        r.ExitCode = p.ExitCode;
                    }

                    r.Output = outBuf.ToString();
                    r.Error = errBuf.ToString();
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                AppLog.Warn("Prozessstart fehlgeschlagen (" + file + "): " + ex.Message);
            }
            return r;
        }

        /// <summary>PowerShell-Einzeiler ausfuehren und die Standardausgabe liefern.</summary>
        public static string Ps(string command, int timeoutMs)
        {
            // Die Ausgabe kommt in der Konsolen-Codepage zurueck; ConvertTo-Json liefert
            // dabei zuverlaessig ASCII-vertraegliches JSON mit \uXXXX-Escapes.
            Result r = Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + command + "\"", timeoutMs);
            if (!string.IsNullOrEmpty(r.Error) && string.IsNullOrEmpty(r.Output))
                AppLog.Warn("PowerShell meldete: " + r.Error.Trim());
            return r.Output ?? "";
        }

        /// <summary>
        /// PowerShell-Einzeiler, dessen Ausgabe JSON ist. Liefert immer eine Liste:
        /// ConvertTo-Json gibt bei genau einem Treffer ein Einzelobjekt zurueck.
        /// </summary>
        public static List<Dictionary<string, object>> PsJson(string command, int timeoutMs)
        {
            var list = new List<Dictionary<string, object>>();
            string json = Ps(command, timeoutMs);
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                object parsed = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }
                                    .DeserializeObject(json.Trim());
                if (parsed is object[] arr)
                {
                    foreach (object o in arr)
                        if (o is Dictionary<string, object> d) list.Add(d);
                }
                else if (parsed is Dictionary<string, object> single)
                {
                    list.Add(single);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("JSON aus PowerShell nicht lesbar: " + ex.Message);
            }
            return list;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return (d != null && d.TryGetValue(key, out v) && v != null) ? v.ToString() : null;
        }

        public static double Num(Dictionary<string, object> d, string key, double fallback)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); }
                catch { }
            }
            return fallback;
        }

        public static bool? Flag(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToBoolean(v); } catch { }
            }
            return null;
        }

        public static void KillTree(int pid)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("taskkill.exe", "/PID " + pid + " /T /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
            }
            catch { }
        }
    }
}
