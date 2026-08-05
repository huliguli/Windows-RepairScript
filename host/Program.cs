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

            string shot = null, view = "";
            bool auto = false;
            int shotWait = 950;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--shot" && i + 1 < args.Length) shot = args[++i];
                else if (args[i] == "--view" && i + 1 < args.Length) view = args[++i];
                else if (args[i] == "--shotwait" && i + 1 < args.Length) int.TryParse(args[++i], out shotWait);
                else if (args[i] == "--auto") auto = true;
            }

            // Stiller, geplanter Wartungslauf ohne Oberflaeche.
            if (auto) { AutoRunner.Run(); return; }

            // Screenshot-Laeufe duerfen parallel laufen (eigener Datenordner).
            if (shot == null && !ClaimSingleInstance()) return;

            if (!WebView2Available()) return;

            AppLog.Info("Start (Version " + typeof(Program).Assembly.GetName().Version + ")");
            Application.Run(new ShellForm(shot, view, shotWait));
            AppLog.Info("Beendet.");
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
