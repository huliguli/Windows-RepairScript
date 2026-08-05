using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
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

        // ---------------------------------------------------------------- Im Nutzerkontext oeffnen

        // Die Fensterliste der Windows-Oberflaeche. Sie wird von der laufenden explorer.exe
        // bereitgestellt - also von einem Prozess, der NICHT erhoeht laeuft.
        [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
        class ShellWindowsKlasse { }

        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"),
         InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        interface IShellWindows
        {
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object FindWindowSW(ref object ort, ref object wurzel, int klasse,
                                out int hwnd, int optionen);
        }

        const int SWC_DESKTOP = 8;
        const int SWFO_NEEDDISPATCH = 1;

        /// <summary>
        /// Oeffnet eine Datei, einen Ordner oder eine Adresse so, wie es der angemeldete
        /// Nutzer selbst tun wuerde - ausdruecklich OHNE die Administratorrechte dieser App.
        ///
        /// Warum der Umweg: Diese App laeuft im Auslieferungszustand erhoeht. Ein einfaches
        /// Process.Start vererbt diese Rechte an das gestartete Programm. Ein Explorer oder
        /// ein Browser mit Administratorrechten ist ein weit offenes Tor: Aus dem Explorer
        /// heraus laesst sich jede beliebige Datei erhoeht starten, und ein Browser mit
        /// Adminrechten gibt jeder heruntergeladenen Datei dieselben Rechte mit.
        ///
        /// Der Weg hier laesst die laufende explorer.exe den Start ausfuehren. Sie gehoert
        /// dem angemeldeten Nutzer und laeuft nicht erhoeht - das gestartete Programm erbt
        /// also deren Rechte, nicht unsere.
        ///
        /// Klappt das nicht (keine Oberflaeche vorhanden, COM abgelehnt), wird auf den
        /// direkten Start zurueckgefallen: Lieber erhoeht oeffnen als gar nicht, denn ohne
        /// den Weg zum Protokoll oder zum Sicherungsordner steht der Nutzer im Regen.
        /// </summary>
        public static void OeffneImNutzerkontext(string ziel, string argumente = null)
        {
            if (string.IsNullOrWhiteSpace(ziel)) return;

            try
            {
                var fenster = (IShellWindows)new ShellWindowsKlasse();
                object ort = null, wurzel = null;
                int hwnd;
                object schreibtisch = fenster.FindWindowSW(ref ort, ref wurzel,
                                                           SWC_DESKTOP, out hwnd, SWFO_NEEDDISPATCH);
                if (schreibtisch != null)
                {
                    object doc = schreibtisch.GetType().InvokeMember(
                        "Document", BindingFlags.GetProperty, null, schreibtisch, null);
                    object anwendung = doc.GetType().InvokeMember(
                        "Application", BindingFlags.GetProperty, null, doc, null);
                    anwendung.GetType().InvokeMember(
                        "ShellExecute", BindingFlags.InvokeMethod, null, anwendung,
                        new object[] { ziel, argumente ?? "", "", "open", 1 });
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Start über die Oberfläche nicht möglich (" + ex.Message
                            + "), es wird direkt gestartet.");
            }

            try
            {
                var psi = new ProcessStartInfo(ziel) { UseShellExecute = true };
                if (!string.IsNullOrEmpty(argumente)) psi.Arguments = argumente;
                Process.Start(psi);
            }
            catch (Exception ex) { AppLog.Warn("Konnte '" + ziel + "' nicht oeffnen: " + ex.Message); }
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
