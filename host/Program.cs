using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace WartungsToolbox
{
    static class Program
    {
        // Ein Name je Sitzung. Verhindert, dass ein zweiter Start in den gesperrten
        // WebView2-Datenordner laeuft und mit einem leeren schwarzen Fenster endet.
        const string SingleInstanceMutex = "WindowsWartung_UI_SingleInstance";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppLog.InstallGlobalHandlers();

            string shot = null, view = "", aufzeichnen = null, pruefen = null;
            bool auto = false, roh = false;
            int shotWait = 950;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--shot" && i + 1 < args.Length) shot = args[++i];
                else if (args[i] == "--view" && i + 1 < args.Length) view = args[++i];
                else if (args[i] == "--shotwait" && i + 1 < args.Length) int.TryParse(args[++i], out shotWait);
                else if (args[i] == "--auto") auto = true;
                else if (args[i] == "--aufzeichnen" && i + 1 < args.Length) aufzeichnen = args[++i];
                else if (args[i] == "--pruefen" && i + 1 < args.Length) pruefen = args[++i];
                else if (args[i] == "--roh") roh = true;
            }

            // Stiller, geplanter Wartungslauf ohne Oberflaeche.
            if (auto) { AutoRunner.Run(); return; }

            // Kommandozeile ohne Oberflaeche (Grundsatz 8: testbar ohne Fenster):
            //   --aufzeichnen <datei.json>  Systembild aufnehmen, redigiert (--roh: unredigiert)
            //   --pruefen <datei.json>      Regeln ueber eine Aufzeichnung laufen lassen,
            //                               Ergebnis nach <datei>.befunde.txt
            // Die EXE hat keine Konsole; Ergebnis und Fehler landen in Dateien und im Protokoll.
            if (aufzeichnen != null || pruefen != null)
            {
                Environment.ExitCode = Kommandozeile(aufzeichnen, pruefen, roh);
                return;
            }

            // Screenshot-Laeufe duerfen parallel laufen (eigener Datenordner).
            if (shot == null && !ClaimSingleInstance()) return;

            if (!WebView2Available()) return;

            AppLog.Info("Start (Version " + typeof(Program).Assembly.GetName().Version + ")");
            Application.Run(new ShellForm(shot, view, shotWait));
            AppLog.Info("Beendet.");
        }

        static int Kommandozeile(string aufzeichnen, string pruefen, bool roh)
        {
            try
            {
                if (aufzeichnen != null)
                {
                    var s = new Sammler.Sammler().Erfassen();
                    Sammler.Aufzeichnung.Schreiben(s, aufzeichnen, !roh);
                    AppLog.Info("Aufzeichnung geschrieben: " + aufzeichnen + (roh ? " (roh)" : " (redigiert)"));
                    if (!roh)
                    {
                        var v = Sammler.Aufzeichnung.Verstoesse(aufzeichnen);
                        if (v.Count > 0) { AppLog.Error("Redaktion unvollständig: " + string.Join(", ", v)); return 2; }
                    }
                    return 0;
                }
                var bild = Sammler.Aufzeichnung.Lesen(pruefen);
                var erg = Kern.Regeln.Alle.Pruefen(bild, Kern.Entscheidungen.Laden());
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Gesamt: " + Kern.Regeln.Alle.Gesamt(erg) + ", Probleme: " + Kern.Regeln.Alle.Probleme(erg));
                foreach (var b in erg)
                {
                    sb.AppendLine();
                    sb.AppendLine("[" + b.Zustand + "] " + Kern.Bereich.Titel(b.Bereich) + (b.DatenVorhanden ? "" : "  (keine Daten: " + string.Join("; ", b.Fehlend) + ")"));
                    foreach (var f in b.Befunde)
                    {
                        sb.AppendLine("   " + f.Zustand.PadRight(7) + " " + f.Titel + "  {" + f.Schluessel + "}");
                        sb.AppendLine("           " + f.Satz);
                        if (f.Rat != null) sb.AppendLine("           Rat: " + f.Rat);
                        if (f.Frage != null) sb.AppendLine("           FRAGE [" + f.Frage.Id + "]: " + f.Frage.Text);
                        foreach (string d in f.Detail) sb.AppendLine("           . " + d);
                    }
                }
                string ziel = pruefen + ".befunde.txt";
                System.IO.File.WriteAllText(ziel, sb.ToString(), new System.Text.UTF8Encoding(true));
                AppLog.Info("Befunde geschrieben: " + ziel);
                return 0;
            }
            catch (Exception ex)
            {
                AppLog.Error("Kommandozeile", ex);
                return 3;
            }
        }

        static Mutex _instance;

        /// <summary>
        /// true, wenn diese Instanz die einzige ist. Sonst wird das bereits offene Fenster
        /// nach vorn geholt und diese Instanz beendet sich still.
        /// </summary>
        static bool ClaimSingleInstance()
        {
            try
            {
                bool created;
                _instance = new Mutex(true, SingleInstanceMutex, out created);
                if (created) return true;

                AppLog.Info("Zweitstart: bestehendes Fenster wird nach vorn geholt.");
                FocusExistingWindow();
                return false;
            }
            catch
            {
                return true;   // im Zweifel starten statt den Nutzer aussperren
            }
        }

        static void FocusExistingWindow()
        {
            try
            {
                int me = Process.GetCurrentProcess().Id;
                foreach (Process p in Process.GetProcessesByName("WindowsWartung"))
                {
                    if (p.Id == me || p.MainWindowHandle == IntPtr.Zero) continue;
                    Native.ShowWindow(p.MainWindowHandle, Native.SW_RESTORE);
                    Native.SetForegroundWindow(p.MainWindowHandle);
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// Prueft, ob die WebView2-Laufzeit vorhanden ist. Fehlte sie, blieb bisher ein totes
        /// schwarzes Fenster stehen - ohne Hinweis, was zu tun ist.
        /// </summary>
        static bool WebView2Available()
        {
            try
            {
                string ver = Microsoft.Web.WebView2.Core.CoreWebView2Environment
                                 .GetAvailableBrowserVersionString(null);
                if (!string.IsNullOrEmpty(ver)) return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("WebView2-Laufzeit nicht gefunden: " + ex.Message);
            }

            AppLog.Error("Start abgebrochen: WebView2-Laufzeit fehlt.");
            DialogResult r = MessageBox.Show(
                "Windows-Wartung braucht eine Windows-Komponente, die auf diesem PC fehlt: " +
                "die WebView2-Laufzeit. Sie ist kostenlos und kommt direkt von Microsoft." +
                Environment.NewLine + Environment.NewLine +
                "Soll die Download-Seite jetzt geöffnet werden?" +
                Environment.NewLine + Environment.NewLine +
                "Nach der Installation starten Sie Windows-Wartung einfach erneut.",
                "Windows-Wartung", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (r == DialogResult.Yes)
            {
                // Auch hier nicht erhoeht: ein Browser mit Administratorrechten gibt jeder
                // heruntergeladenen Datei dieselben Rechte mit.
                Shell.OeffneImNutzerkontext("https://developer.microsoft.com/microsoft-edge/webview2/");
            }
            return false;
        }
    }
}
