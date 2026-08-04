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
            // Ist die App gerade offen, wird der Lauf an sie uebergeben und dort sichtbar
            // ausgefuehrt (verhindert zwei parallele DISM/SFC-Instanzen). Handshake per
            // SendMessageTimeout: Nur wenn die App mit 1 antwortet, hat sie den Lauf
            // WIRKLICH uebernommen - sonst (alte Version, blockierte Nachricht,
            // haengendes Fenster) laeuft die Wartung hier still im Hintergrund weiter.
            IntPtr win = FindInteractiveWindow();
            if (win != IntPtr.Zero)
            {
                IntPtr res;
                IntPtr ok = Native.SendMessageTimeout(win, Native.WM_WW_RUNAUTO, IntPtr.Zero, IntPtr.Zero,
                                                      Native.SMTO_NORMAL | Native.SMTO_ABORTIFHUNG, 5000, out res);
                if (ok != IntPtr.Zero && res == (IntPtr)1) return; // App fuehrt den Lauf sichtbar aus
            }

            // Gegen doppelten Trigger in derselben Sitzung absichern.
            bool created;
            using (Mutex mx = new Mutex(true, "WindowsWartung_AutoRun", out created))
            {
                if (!created)
                {
                    // Ein vorheriger Lauf haengt noch. Das gehoert in den Verlauf, sonst
                    // merkt der Nutzer nie, dass seine Wartung seit Wochen ausfaellt.
                    History.Add("Geplante Wartung", "warn", "Übersprungen, ein vorheriger Lauf war noch aktiv", 0);
                    AppLog.Warn("Geplante Wartung nicht gestartet: vorheriger Lauf ist noch aktiv.");
                    return;
                }

                Stopwatch sw = Stopwatch.StartNew();
                bool problem = false;
                // Vom Nutzer gewaehlter Aufgaben-Satz (schedule.json); null => Standard-Satz.
                foreach (Step s in Catalog.AutoSet(Scheduler.ReadActions()))
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

        static IntPtr FindInteractiveWindow()
        {
            try
            {
                int me = Process.GetCurrentProcess().Id;
                foreach (Process pr in Process.GetProcessesByName("WindowsWartung"))
                {
                    try { if (pr.Id != me && pr.MainWindowHandle != IntPtr.Zero) return pr.MainWindowHandle; }
                    catch { }
                }
            }
            catch { }
            return IntPtr.Zero;
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
                    // Ohne Zeitlimit haelt ein haengendes DISM den Mutex und damit ALLE
                    // kuenftigen geplanten Laeufe auf - lautlos, ohne Protokolleintrag.
                    if (!p.WaitForExit(45 * 60 * 1000))
                    {
                        AppLog.Warn("Geplante Wartung: Zeitlimit bei " + s.File + " " + s.Args);
                        Shell.KillTree(p.Id);
                        return -1;
                    }
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
