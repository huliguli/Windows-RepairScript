using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace WartungsToolbox
{
    // Stiller, geplanter Wartungslauf (--auto): fuehrt einen gruendlichen, ungefaehrlichen
    // Satz aus, protokolliert ihn im Verlauf und meldet sich per Windows-Benachrichtigung.
    // Keine Oberflaeche.
    static class AutoRunner
    {
        public static void Run()
        {
            // Nicht laufen, wenn der Nutzer die App gerade offen hat (sichtbares Fenster) -> Termin auslassen.
            if (InteractiveInstanceRunning())
            {
                Notify("Wartung verschoben",
                    "Die App ist gerade geoeffnet - die geplante Wartung wird beim naechsten Termin nachgeholt.",
                    ToolTipIcon.Info);
                return;
            }

            // Gegen doppelten Trigger in derselben Sitzung absichern.
            bool created;
            using (Mutex mx = new Mutex(true, "WindowsWartung_AutoRun", out created))
            {
                if (!created) return;

                Stopwatch sw = Stopwatch.StartNew();
                bool problem = false;
                foreach (Step s in Catalog.AutoSet())
                {
                    int code = RunSilent(s);
                    if (code != 0 && !s.IgnoreExit) problem = true;
                }
                sw.Stop();

                string msg = problem ? "Mit Hinweisen abgeschlossen" : "Erfolgreich abgeschlossen";
                History.Add("Geplante Wartung", problem ? "warn" : "good", msg, sw.Elapsed.TotalSeconds);

                Notify("Wartung abgeschlossen",
                    msg + " (ca. " + Math.Round(sw.Elapsed.TotalMinutes, 1) + " min).",
                    problem ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
        }

        static bool InteractiveInstanceRunning()
        {
            try
            {
                int me = Process.GetCurrentProcess().Id;
                foreach (Process pr in Process.GetProcessesByName("WindowsWartung"))
                {
                    try { if (pr.Id != me && pr.MainWindowHandle != IntPtr.Zero) return true; }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        static int RunSilent(Step s)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = s.File;
                psi.Arguments = s.Args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    // Ausgabe verwerfen, aber Puffer leeren (sonst Deadlock bei viel Output).
                    p.OutputDataReceived += delegate { };
                    p.ErrorDataReceived += delegate { };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        static void Notify(string title, string text, ToolTipIcon icon)
        {
            try
            {
                NotifyIcon ni = new NotifyIcon();
                try { ni.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch { ni.Icon = SystemIcons.Information; }
                ni.Visible = true;
                ni.BalloonTipTitle = "Windows-Wartung – " + title;
                ni.BalloonTipText = text;
                ni.BalloonTipIcon = icon;
                ni.ShowBalloonTip(8000);

                // Kurze Message-Pump, damit der Ballon erscheint, dann beenden.
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 6000;
                t.Tick += delegate
                {
                    try { ni.Visible = false; ni.Dispose(); } catch { }
                    Application.ExitThread();
                };
                t.Start();
                Application.Run();
            }
            catch { }
        }
    }
}
