using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WartungsToolbox
{
    public partial class ShellForm : Form
    {
        WebView2 _web;
        CommandRunner _runner;
        readonly List<MaintenanceAction> _actions = Catalog.All();
        readonly StringBuilder _log = new StringBuilder();
        readonly JavaScriptSerializer _js = new JavaScriptSerializer();

        readonly string _shotPath;
        readonly string _view;
        readonly int _shotWaitMs;

        string _pendingPost = "none";
        int _pendingDelay = 60;

        const string Repo = "huliguli/Windows-RepairScript";
        string _updateUrl;
        string _updateTag;
        string _updateAsset;
        string _updateHashUrl;
        string _updateSetup;       // Installer des Releases (falls per Installer installiert)
        string _updateSetupHashUrl;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        NotifyIcon _tray;
        bool _notifyEnabled = true;

        public ShellForm(string shotPath, string view, int shotWaitMs = 950)
        {
            _shotPath = shotPath;
            _view = view ?? "";
            _shotWaitMs = shotWaitMs < 200 ? 200 : shotWaitMs;

            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(21, 24, 29);

            // Das Manifest fordert PerMonitorV2 - Groessen in WinForms sind damit echte
            // Geraetepixel. Ungeskaliert startete die App auf einem 200-%-Bildschirm mit
            // nur 590x380 logischen Pixeln, das Fenster war unbenutzbar klein.
            float scale = 1f;
            try { using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f; }
            catch { }
            if (scale < 1f) scale = 1f;
            if (scale > 3f) scale = 3f;

            ClientSize = new Size((int)(1180 * scale), (int)(760 * scale));
            MinimumSize = new Size((int)(760 * scale), (int)(560 * scale));

            // Auf sehr kleinen Bildschirmen nicht ueber den Arbeitsbereich hinauswachsen.
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            if (ClientSize.Width > work.Width || ClientSize.Height > work.Height)
                ClientSize = new Size(Math.Min(ClientSize.Width, work.Width - 40),
                                      Math.Min(ClientSize.Height, work.Height - 40));

            StartPosition = FormStartPosition.CenterScreen;
            Text = "Windows-Wartung";
            DoubleBuffered = true;

            _web = new WebView2();
            _web.Dock = DockStyle.Fill;
            _web.DefaultBackgroundColor = Color.FromArgb(21, 24, 29);
            Controls.Add(_web);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            Load += OnLoad;
            Shown += delegate { ForceForeground(); };
            FormClosing += OnClosingWhileBusy;
            FormClosed += delegate { try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { } };
        }

        /// <summary>
        /// Waehrend etwas ENTFERNT wird, bleibt das Fenster stehen.
        ///
        /// Der Arbeits-Thread laeuft im Hintergrund und stirbt beim Prozessende sofort.
        /// Ginge das Fenster mitten im Entfernen zu, bliebe die Loeschschleife auf halbem
        /// Weg stehen, und der Nutzer erfuehre nie, wo seine Sicherungsdatei liegt. Die
        /// Oberflaeche verspricht an dieser Stelle ausdruecklich, dass sich der Vorgang
        /// nicht mehr anhalten laesst - das X in der Titelleiste darf dieses Versprechen
        /// nicht unterlaufen. Es dauert nur Sekunden.
        /// </summary>
        void OnClosingWhileBusy(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason != CloseReason.UserClosing) return;
            if (!ScanEntferntGerade) return;

            e.Cancel = true;
            try
            {
                MessageBox.Show(this,
                    "Es wird gerade aufgeräumt. Bitte warten Sie einen Moment, bis der Vorgang " +
                    "abgeschlossen ist. Danach lässt sich das Fenster wie gewohnt schließen.",
                    "Windows-Wartung", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { }
        }

        // Windows-Benachrichtigung (nur wenn das Fenster im Hintergrund/minimiert ist)
        void Notify(string title, string message, LogKind kind)
        {
            if (!_notifyEnabled || _tray == null) return;
            try
            {
                bool fg = (WindowState != FormWindowState.Minimized) && (GetForegroundWindow() == Handle);
                if (fg) return;
                ToolTipIcon ic = kind == LogKind.Good ? ToolTipIcon.Info
                               : (kind == LogKind.Bad ? ToolTipIcon.Error : ToolTipIcon.Warning);
                _tray.ShowBalloonTip(5000, "Windows-Wartung", title + " – " + message, ic);
            }
            catch { }
        }

        // Bringt das Fenster beim Start zuverlässig in den Vordergrund (auch elevated/UAC)
        void ForceForeground()
        {
            if (_shotPath != null) return;
            try
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;

                IntPtr fore = GetForegroundWindow();
                uint forePid;
                uint foreThread = GetWindowThreadProcessId(fore, out forePid);
                uint thisThread = GetCurrentThreadId();

                bool attached = false;
                if (foreThread != 0 && foreThread != thisThread)
                    attached = AttachThreadInput(thisThread, foreThread, true);

                TopMost = true;
                TopMost = false;
                BringWindowToTop(Handle);
                SetForegroundWindow(Handle);
                Activate();

                if (attached)
                    AttachThreadInput(thisThread, foreThread, false);
            }
            catch { }
        }

        async void OnLoad(object sender, EventArgs e)
        {
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }

            try
            {
                _tray = new NotifyIcon();
                _tray.Icon = Icon ?? System.Drawing.SystemIcons.Application;
                _tray.Text = "Windows-Wartung";
                _tray.Visible = true;
                _tray.DoubleClick += delegate { WindowState = FormWindowState.Normal; ForceForeground(); };
            }
            catch { }

            try
            {
                // Screenshot-Laeufe bekommen einen eigenen Datenordner, damit sie eine
                // bereits laufende Instanz (die den normalen Ordner sperrt) nicht stoeren.
                string udf = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindowsWartung", _shotPath != null ? "WebView2_shot" : "WebView2");
                Directory.CreateDirectory(udf);

                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, udf, null);
                await _web.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                if (_shotPath != null) { Close(); return; }
                MessageBox.Show(this, "WebView2 konnte nicht geladen werden:\n\n" + ex.Message,
                    "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            CoreWebView2 core = _web.CoreWebView2;

            string uiDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
            core.SetVirtualHostNameToFolderMapping("app", uiDir, CoreWebView2HostResourceAccessKind.Allow);

            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;

            core.WebMessageReceived += OnWebMessage;
            _runner = new CommandRunner(_web, Log, SetState, Done, OnProgress);

            // Gespeicherte UI-Groesse schon vor dem Anzeigen anwenden (kein Flackern)
            try { _web.ZoomFactor = (_shotPath != null) ? 1.0 : ReadZoom(); } catch { }

            if (_shotPath != null)
                core.NavigationCompleted += OnNavForShot;

            // Der --view-Wert wird unveraendert als Adresszusatz weitergereicht.
            // Die Oberflaeche wertet ihn nur fuer Belegaufnahmen aus (Farbschema, Ansicht).
            string suffix = string.IsNullOrEmpty(_view) ? "" : "#" + Uri.EscapeDataString(_view);

            _web.Source = new Uri("https://app/index.html" + suffix);
            // Update-Prüfung startet erst, wenn das UI 'ready' meldet (siehe OnReady)
        }

        void OnReady()
        {
            if (_shotPath != null)
            {
                SendCatalog();
                SendAdmin();
                if (_shotWaitMs > 1500) StartQuickGlance();
                return;
            }
            SendCatalog();
            try { Post(new { type = "zoom", factor = _web.ZoomFactor }); } catch { }
            SendAdmin();
            CheckUpdatedMarker();   // nach einem Update: Erfolgsmeldung zeigen
            StartUpdateCheck();     // auf neue Version prüfen
            StartQuickGlance();     // sofort einen echten, lesenden Erstbefund zeigen
        }

        /// <summary>
        /// Schickt den Aktionskatalog an die Oberflaeche. Er ist damit nur noch an EINER
        /// Stelle gepflegt; frueher stand eine zweite Fassung in ui/app.js, und 18 von 28
        /// Beschreibungen waren bereits auseinandergelaufen.
        /// </summary>
        void SendCatalog()
        {
            try
            {
                Post(new
                {
                    type = "catalog",
                    version = typeof(ShellForm).Assembly.GetName().Version.ToString(3),
                    categories = Catalog.Categories,
                    notes = Catalog.CategoryNotes,
                    actions = _actions.Select(a => new
                    {
                        id = a.Id,
                        title = a.Title,
                        tech = a.TechTitle,
                        desc = a.Desc,
                        info = a.Info,
                        icon = a.Icon,
                        cat = a.Category,
                        danger = a.Danger,
                        restore = a.WantsRestorePoint,
                        special = a.Special,
                    }).ToArray(),
                    autoTasks = Catalog.AutoCatalog().Select(t => new
                    {
                        key = t.Key, title = t.Title, desc = t.Desc, std = t.Std,
                    }).ToArray(),
                });
            }
            catch (Exception ex) { AppLog.Error("Katalog konnte nicht gesendet werden", ex); }
        }

        /// <summary>
        /// Meldet Rechtelage UND Benutzerkonto an die Oberflaeche. Beides gehoert zusammen:
        /// Wer die Rechte ueber ein fremdes Konto geholt hat, sieht ueberall das Profil
        /// dieses Kontos statt sein eigenes.
        /// </summary>
        void SendAdmin()
        {
            string laeuftAls = "", angemeldet = "";
            bool fremd = false;
            try { fremd = Nutzerkontext.AnderesKontoAlsAngemeldet(out laeuftAls, out angemeldet); }
            catch (Exception ex) { AppLog.Warn("Kontopruefung fehlgeschlagen: " + ex.Message); }

            if (fremd)
                AppLog.Info("Laeuft als '" + laeuftAls + "', angemeldet ist '" + angemeldet + "'.");

            try
            {
                Post(new
                {
                    type = "admin",
                    on = IsElevated(),
                    fremdesKonto = fremd,
                    laeuftAls = laeuftAls,
                    angemeldet = angemeldet,
                });
            }
            catch { }
        }

        static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>Konsolen-Codepage des Systems (cmd.exe und PowerShell schreiben darin).</summary>
        static Encoding OemEncoding()
        {
            try { return Encoding.GetEncoding((int)Native.GetOEMCP()); }
            catch { return Encoding.Default; }
        }

        static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        // ---------- Update-Prüfung (GitHub-Releases) ----------
        void StartUpdateCheck()
        {
            Thread t = new Thread(delegate ()
            {
                try
                {
                    SetupTls();

                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(
                        "https://api.github.com/repos/" + Repo + "/releases/latest");
                    req.UserAgent = "WindowsWartung-Updater";
                    req.Accept = "application/vnd.github+json";
                    req.Timeout = 8000;

                    string json;
                    using (WebResponse resp = req.GetResponse())
                    using (Stream s = resp.GetResponseStream())
                    using (StreamReader sr = new StreamReader(s))
                        json = sr.ReadToEnd();

                    Dictionary<string, object> data = _js.DeserializeObject(json) as Dictionary<string, object>;
                    if (data == null) return;

                    string tag = data.ContainsKey("tag_name") ? Convert.ToString(data["tag_name"]) : null;
                    string url = data.ContainsKey("html_url") ? Convert.ToString(data["html_url"]) : null;
                    string name = data.ContainsKey("name") ? Convert.ToString(data["name"]) : "";
                    if (string.IsNullOrEmpty(tag)) return;

                    // Dateien des Releases nach Namen zuordnen.
                    //
                    // Frueher galt "die letzte Datei auf .sha256" als Pruefsumme. Seit das
                    // Release AUCH eine Pruefsumme fuer den Installer mitbringt, sind das
                    // zwei Kandidaten, und welcher gewinnt, haengt allein an der Reihenfolge
                    // der API. Heute passt es zufaellig (alphabetisch steht die ZIP-Summe
                    // hinten); dreht sich das, wuerde das ZIP gegen die Installer-Summe
                    // geprueft und das Update als "beschaedigt" abgelehnt.
                    // Deshalb: die Pruefsumme heisst IMMER "<Datei>.sha256".
                    var dateien = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    object assetsObj;
                    if (data.TryGetValue("assets", out assetsObj) && assetsObj is object[])
                    {
                        foreach (object ao in (object[])assetsObj)
                        {
                            Dictionary<string, object> ad = ao as Dictionary<string, object>;
                            if (ad == null) continue;
                            string dateiName = ad.ContainsKey("name") ? Convert.ToString(ad["name"]) : "";
                            string dateiUrl = ad.ContainsKey("browser_download_url") ? Convert.ToString(ad["browser_download_url"]) : "";
                            if (dateiName.Length > 0 && dateiUrl.Length > 0) dateien[dateiName] = dateiUrl;
                        }
                    }

                    string zipName = dateien.Keys.FirstOrDefault(
                        n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                    if (zipName != null)
                    {
                        _updateAsset = dateien[zipName];
                        string h;
                        _updateHashUrl = dateien.TryGetValue(zipName + ".sha256", out h) ? h : null;
                    }

                    string setupName = dateien.Keys.FirstOrDefault(
                        n => n.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase));
                    if (setupName != null)
                    {
                        _updateSetup = dateien[setupName];
                        string h;
                        _updateSetupHashUrl = dateien.TryGetValue(setupName + ".sha256", out h) ? h : null;
                    }

                    Version latest = ParseVer(tag);
                    Version cur = typeof(ShellForm).Assembly.GetName().Version;
                    if (latest == null || latest <= cur) return;

                    // Neueres Release ohne ZIP-Paket = Release wird gerade erst
                    // bestueckt (der CI-Build haengt die Dateien einige Minuten
                    // nach dem Veroeffentlichen an). Still ueberspringen statt
                    // ein totes Update anzubieten - der naechste Start findet
                    // das vollstaendige Release.
                    if (string.IsNullOrEmpty(_updateAsset)) return;

                    _updateTag = tag;
                    _updateUrl = string.IsNullOrEmpty(url) ? "https://github.com/" + Repo + "/releases/latest" : url;

                    if (_web != null && _web.IsHandleCreated)
                    {
                        string ftag = tag, fname = name;
                        try
                        {
                            _web.BeginInvoke((Action)delegate
                            {
                                Post(new { type = "update", version = ftag, notes = fname });
                            });
                        }
                        catch { }
                    }
                }
                catch { } // kein Release / offline / Fehler -> einfach still
            });
            t.IsBackground = true;
            t.Start();
        }

        void OpenUpdate()
        {
            if (string.IsNullOrEmpty(_updateUrl) || !_updateUrl.StartsWith("http")) return;
            // Ein Browser mit Administratorrechten gibt jeder heruntergeladenen Datei
            // dieselben Rechte mit. Deshalb ueber die Oberflaeche des Nutzers starten.
            Shell.OeffneImNutzerkontext(_updateUrl);
        }

        // ---------- In-App-Update: herunterladen, entpacken, tauschen, neu starten ----------
        void BeginUpdate()
        {
            if (string.IsNullOrEmpty(_updateAsset))
            {
                Post(new { type = "updateError", message = "Kein Download-Paket im Release gefunden." });
                return;
            }
            Thread t = new Thread(delegate ()
            {
                string tmp = UpdateWorkDir();
                try
                {
                    try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                    CreateAdminOnlyDirectory(tmp);

                    // Wurde die App ueber den Installer eingerichtet, wird auch ueber den
                    // Installer aktualisiert. Ein blosser Dateitausch laesst sonst den
                    // Eintrag unter "Apps und Features" auf der alten Version stehen, und
                    // eine spaetere Deinstallation raeumt nicht mehr alles weg.
                    string installOrt;
                    if (UpdateTrust.IstPerInstallerInstalliert(out installOrt)
                        && !string.IsNullOrEmpty(_updateSetup))
                    {
                        AppLog.Info("Aktualisierung ueber den Installer (" + installOrt + ").");
                        UpdateViaInstaller(tmp);
                        return;
                    }

                    string zip = Path.Combine(tmp, "update.zip");

                    SetupTls();
                    UiPost(new { type = "updateStatus", phase = "download" });

                    // Echter Fortschritt aus dem echten Download. Frueher lief hier ein
                    // fingierter Balken (70 Schritte a 50 ms) und der Download wurde erst
                    // danach abgewartet: auf langsamer Leitung stand die Anzeige minutenlang
                    // bei 100 %, ohne dass etwas passierte.
                    string dlError = DownloadWithProgress(_updateAsset, zip);
                    if (dlError != null)
                    {
                        AppLog.Warn("Update-Download fehlgeschlagen: " + dlError);
                        UiPost(new { type = "updateError", message = dlError });
                        return;
                    }

                    // Pruefsumme gegen den Download (Unversehrtheit). Frueher war das
                    // fail-open: fehlte die .sha256 oder scheiterte ihr Abruf, wurde ohne
                    // jede Pruefung installiert. Jetzt bricht jeder dieser Faelle ab.
                    string pruef = PruefeZip(zip);
                    if (pruef != null) { UiPost(new { type = "updateError", message = pruef }); return; }

                    UiPost(new { type = "updateStatus", phase = "extract" });

                    if (_web != null && _web.IsHandleCreated)
                        _web.BeginInvoke((Action)delegate { FinishUpdate(tmp, zip); });
                }
                catch (Exception ex)
                {
                    AppLog.Error("Update fehlgeschlagen", ex);
                    UiPost(new { type = "updateError", message = ex.Message });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        const string UserAgent = "WindowsWartung-Updater";
        const long MaxUpdateBytes = 200L * 1024 * 1024;   // Reissleine gegen endlose Antworten

        /// <summary>
        /// Aktualisierung ueber den Installer des Releases. Laeuft still durch und ersetzt
        /// die Installation sauber, inklusive Eintrag unter "Apps und Features".
        /// </summary>
        void UpdateViaInstaller(string tmp)
        {
            SetupTls();
            UiPost(new { type = "updateStatus", phase = "download" });

            string setup = Path.Combine(tmp, "setup.exe");
            string fehler = DownloadWithProgress(_updateSetup, setup);
            if (fehler != null)
            {
                AppLog.Warn("Installer-Download fehlgeschlagen: " + fehler);
                UiPost(new { type = "updateError", message = fehler });
                return;
            }

            string pruef = PruefeDatei(setup, _updateSetupHashUrl);
            if (pruef != null) { UiPost(new { type = "updateError", message = pruef }); return; }

            UiPost(new { type = "updateStatus", phase = "restart" });
            WriteMarker(_updateTag);
            AppLog.Info("Installer wird still ausgefuehrt.");

            try
            {
                // /VERYSILENT: keine Oberflaeche. /NORESTART: der Installer startet den PC nicht neu.
                // Der Installer wartet, bis diese Instanz beendet ist - deshalb erst starten,
                // dann beenden.
                ProcessStartInfo psi = new ProcessStartInfo(setup,
                    "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
                BeginInvoke((Action)delegate { Application.Exit(); });
            }
            catch (Exception ex)
            {
                AppLog.Error("Installer liess sich nicht starten", ex);
                UiPost(new { type = "updateError", message = ex.Message });
            }
        }

        /// <summary>
        /// Prueft eine heruntergeladene Datei: erst die Pruefsumme (Unversehrtheit), dann
        /// den Herausgeber (Herkunft). Gibt null zurueck, wenn beides in Ordnung ist.
        /// Fail-closed: fehlt die Pruefsumme oder laesst sie sich nicht laden, wird abgebrochen.
        /// </summary>
        string PruefeDatei(string datei, string hashUrl)
        {
            if (string.IsNullOrEmpty(hashUrl))
            {
                AppLog.Warn("Update abgebrochen: keine Pruefsumme im Release hinterlegt.");
                return "Zu diesem Update fehlt die Prüfsumme. Aus Sicherheitsgründen wurde es nicht installiert.";
            }

            string erwartet = null;
            try
            {
                using (WebClient hw = new WebClient())
                {
                    hw.Headers.Add("User-Agent", UserAgent);
                    erwartet = (hw.DownloadString(hashUrl) ?? "").Trim();
                }
            }
            catch (Exception ex) { AppLog.Warn("Pruefsumme nicht abrufbar: " + ex.Message); }

            if (string.IsNullOrEmpty(erwartet))
                return "Die Prüfsumme konnte nicht geladen werden. Aus Sicherheitsgründen wurde das Update nicht installiert.";

            if (!string.Equals(erwartet, Sha256File(datei), StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Error("Update abgebrochen: Pruefsumme weicht ab.");
                return "Die heruntergeladene Datei ist beschädigt. Aus Sicherheitsgründen wurde die " +
                       "Installation abgebrochen. Bitte versuchen Sie es später erneut.";
            }

            // Die Pruefsumme liegt im selben Release wie die Datei - sie belegt nur, dass der
            // Download heil ankam. Die Herkunft belegt erst die Signatur, gebunden an den
            // bereits installierten Herausgeber.
            return UpdateTrust.PruefeHerausgeber(datei);
        }

        /// <summary>
        /// Nur die Pruefsumme des ZIP. Die Signatur sitzt an der EXE IM Paket und wird
        /// deshalb erst nach dem Entpacken geprueft (siehe FinishUpdate).
        /// </summary>
        string PruefeZip(string zip)
        {
            if (string.IsNullOrEmpty(_updateHashUrl))
            {
                AppLog.Warn("Update abgebrochen: keine Pruefsumme im Release hinterlegt.");
                return "Zu diesem Update fehlt die Prüfsumme. Aus Sicherheitsgründen wurde es nicht installiert.";
            }

            string erwartet = null;
            try
            {
                using (WebClient hw = new WebClient())
                {
                    hw.Headers.Add("User-Agent", UserAgent);
                    erwartet = (hw.DownloadString(_updateHashUrl) ?? "").Trim();
                }
            }
            catch (Exception ex) { AppLog.Warn("Pruefsumme nicht abrufbar: " + ex.Message); }

            if (string.IsNullOrEmpty(erwartet))
                return "Die Prüfsumme konnte nicht geladen werden. Aus Sicherheitsgründen wurde das Update nicht installiert.";

            if (!string.Equals(erwartet, Sha256File(zip), StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Error("Update abgebrochen: Pruefsumme weicht ab.");
                return "Die heruntergeladene Datei ist beschädigt. Aus Sicherheitsgründen wurde die " +
                       "Installation abgebrochen. Bitte versuchen Sie es später erneut.";
            }
            return null;
        }

        static void SetupTls()
        {
            // Nur moderne Protokolle. Frueher wurde TLS 1.2 nur ODER-verknuepft ergaenzt,
            // veraltete Protokolle blieben also aktiv.
            try
            {
                SecurityProtocolType want = SecurityProtocolType.Tls12;
                try { want |= (SecurityProtocolType)12288; } catch { }   // TLS 1.3, falls bekannt
                ServicePointManager.SecurityProtocol = want;
            }
            catch
            {
                try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { }
            }
        }

        /// <summary>
        /// Arbeitsordner fuer das Update. Bewusst NICHT %TEMP%: die App laeuft als Administrator,
        /// der Nutzer-Temp ist aber auch ohne Adminrechte beschreibbar. Ein nicht erhoehter
        /// Prozess konnte das entpackte Paket oder die Batchdatei zwischen Schreiben und
        /// Ausfuehren austauschen und damit Code als Administrator ausfuehren.
        /// </summary>
        static string UpdateWorkDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "WindowsWartung", "update");
        }

        /// <summary>
        /// Legt einen Ordner an, in den nur Administratoren und SYSTEM schreiben duerfen.
        /// Vererbte Rechte werden ausdruecklich abgeworfen.
        /// </summary>
        static void CreateAdminOnlyDirectory(string path)
        {
            Directory.CreateDirectory(path);
            try
            {
                DirectoryInfo di = new DirectoryInfo(path);
                DirectorySecurity sec = new DirectorySecurity();
                sec.SetAccessRuleProtection(true, false);   // Vererbung aus, nichts uebernehmen

                SecurityIdentifier admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                SecurityIdentifier system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                InheritanceFlags inh = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

                sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
                sec.SetOwner(admins);

                di.SetAccessControl(sec);
            }
            catch (Exception ex)
            {
                // Ohne gesetzte Rechte waere der Pfad angreifbar - dann lieber gar nicht updaten.
                AppLog.Error("Update-Ordner konnte nicht abgesichert werden", ex);
                throw new InvalidOperationException(
                    "Der Ordner für das Update konnte nicht abgesichert werden. Das Update wurde abgebrochen.");
            }
        }

        /// <summary>
        /// Laedt die Datei und meldet echten Fortschritt. Gibt null bei Erfolg zurueck,
        /// sonst eine laienverstaendliche Fehlermeldung.
        /// </summary>
        string DownloadWithProgress(string url, string target)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = UserAgent;
                req.Timeout = 20000;             // Verbindungsaufbau
                req.ReadWriteTimeout = 60000;    // Stillstand mitten im Strom

                using (WebResponse resp = req.GetResponse())
                using (Stream src = resp.GetResponseStream())
                using (FileStream dst = new FileStream(target, FileMode.Create, FileAccess.Write))
                {
                    long total = resp.ContentLength;      // -1, wenn der Server nichts sagt
                    if (total > MaxUpdateBytes) return "Das Update ist unerwartet groß. Der Download wurde abgebrochen.";

                    byte[] buf = new byte[81920];
                    long got = 0;
                    int lastPct = -1;
                    int read;
                    while ((read = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, read);
                        got += read;
                        if (got > MaxUpdateBytes) return "Das Update ist unerwartet groß. Der Download wurde abgebrochen.";

                        int pct = total > 0 ? (int)(got * 100 / total) : -1;
                        if (pct != lastPct)
                        {
                            lastPct = pct;
                            UiPost(new { type = "updateProgress", percent = pct, bytes = got, total = total });
                        }
                    }
                    if (total > 0 && got != total) return "Der Download wurde unterbrochen. Bitte versuchen Sie es später erneut.";
                }
                return null;
            }
            catch (WebException ex)
            {
                AppLog.Warn("Update-Download: " + ex.Message);
                return "Das Update konnte nicht geladen werden. Häufigste Ursache: keine oder gestörte Internetverbindung.";
            }
            catch (Exception ex)
            {
                AppLog.Error("Update-Download", ex);
                return ex.Message;
            }
        }

        void FinishUpdate(string tmp, string zip)
        {
            try
            {
                string newDir = Path.Combine(tmp, "new");
                if (Directory.Exists(newDir)) Directory.Delete(newDir, true);
                ZipFile.ExtractToDirectory(zip, newDir);

                string neueExe = Path.Combine(newDir, "WindowsWartung.exe");
                if (!File.Exists(neueExe))
                {
                    Post(new { type = "updateError", message = "Paket enthält keine WindowsWartung.exe." });
                    return;
                }

                // Herkunft pruefen, BEVOR getauscht wird: die neue Fassung muss vom selben
                // Herausgeber stammen wie die laufende. Die Pruefsumme allein sagt darueber
                // nichts, weil sie im selben Release liegt wie die Datei.
                string herkunft = UpdateTrust.PruefeHerausgeber(neueExe);
                if (herkunft != null)
                {
                    Post(new { type = "updateError", message = herkunft });
                    return;
                }

                string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                string appExe = Path.Combine(appDir, "WindowsWartung.exe");
                int pid = Process.GetCurrentProcess().Id;

                WriteMarker(_updateTag);

                // Die Batchdatei liegt im abgesicherten Update-Ordner, NICHT in %TEMP%:
                // sie wird gleich mit Adminrechten ausgefuehrt.
                string bat = Path.Combine(tmp, "ww_update.cmd");
                string sicherung = Path.Combine(tmp, "vorher");
                string content =
                    "@echo off\r\n" +
                    ":w\r\n" +
                    "tasklist /FI \"PID eq " + pid + "\" 2>nul | find \"" + pid + "\" >nul\r\n" +
                    "if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto w )\r\n" +
                    // Erst die bisherige Fassung zur Seite legen. Ohne diesen Schritt gab es
                    // keinen Rueckweg: Bricht das Kopieren mittendrin ab, blieb eine halb
                    // ueberschriebene Installation stehen - neue Oberflaeche, alte Programmdatei
                    // oder umgekehrt. Scheitert schon die Sicherung, wird gar nichts angefasst.
                    "rmdir /s /q \"" + sicherung + "\" >nul 2>&1\r\n" +
                    "robocopy \"" + appDir + "\" \"" + sicherung + "\" /E /NFL /NDL /NJH /NJS /R:1 /W:1 >nul\r\n" +
                    "if errorlevel 8 (\r\n" +
                    "  echo Die bisherige Fassung liess sich nicht sichern. Es wurde nichts veraendert.> \"" + Path.Combine(tmp, "fehler.txt") + "\"\r\n" +
                    "  start \"\" \"" + appExe + "\"\r\n" +
                    "  goto ende\r\n" +
                    ")\r\n" +
                    "robocopy \"" + newDir + "\" \"" + appDir + "\" /E /NFL /NDL /NJH /NJS /R:3 /W:2 >nul\r\n" +
                    // Robocopy meldet alles unter 8 als Erfolg. Ist mehr passiert, wurde nicht
                    // sauber kopiert - dann die gesicherte Fassung zurueckholen, statt eine
                    // halb ueberschriebene zu starten. /PURGE raeumt dabei weg, was der
                    // abgebrochene Lauf bereits hineinkopiert hatte.
                    "if errorlevel 8 (\r\n" +
                    "  robocopy \"" + sicherung + "\" \"" + appDir + "\" /E /PURGE /NFL /NDL /NJH /NJS /R:2 /W:2 >nul\r\n" +
                    "  echo Update fehlgeschlagen, die bisherige Fassung wurde wiederhergestellt.> \"" + Path.Combine(tmp, "fehler.txt") + "\"\r\n" +
                    ")\r\n" +
                    "start \"\" \"" + appExe + "\"\r\n" +
                    ":ende\r\n" +
                    "rmdir /s /q \"" + sicherung + "\" >nul 2>&1\r\n" +
                    "rmdir /s /q \"" + Path.Combine(tmp, "new") + "\" >nul 2>&1\r\n" +
                    "del \"" + Path.Combine(tmp, "update.zip") + "\" >nul 2>&1\r\n" +
                    "del \"%~f0\" >nul 2>&1\r\n";

                // cmd.exe liest Batchdateien in der OEM-Codepage. Mit Encoding.Default (ANSI)
                // wurden Umlaute im Pfad verstuemmelt - bei einem Benutzernamen wie "Müller"
                // schlug das Kopieren fehl und die App startete nach dem Update nicht mehr.
                File.WriteAllText(bat, content, OemEncoding());

                Post(new { type = "updateStatus", phase = "restart" });
                AppLog.Info("Update auf " + _updateTag + " wird angewendet.");

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);

                BeginInvoke((Action)delegate { Application.Exit(); });
            }
            catch (Exception ex)
            {
                AppLog.Error("Update konnte nicht angewendet werden", ex);
                Post(new { type = "updateError", message = ex.Message });
            }
        }

        string ZoomPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsWartung", "zoom.txt");
        }
        double ReadZoom()
        {
            try
            {
                string p = ZoomPath();
                if (File.Exists(p))
                {
                    double z;
                    if (double.TryParse(File.ReadAllText(p).Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out z))
                    {
                        if (z < 0.7) z = 0.7; if (z > 2.5) z = 2.5;
                        return z;
                    }
                }
            }
            catch { }
            return 1.0;
        }
        void WriteZoom(double z)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ZoomPath()));
                File.WriteAllText(ZoomPath(), z.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch { }
        }

        string MarkerPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsWartung", "pending_update.txt");
        }
        void WriteMarker(string tag)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath())); File.WriteAllText(MarkerPath(), tag ?? ""); }
            catch { }
        }
        void CheckUpdatedMarker()
        {
            try
            {
                string p = MarkerPath();
                if (!File.Exists(p)) return;
                string tag = File.ReadAllText(p).Trim();
                try { File.Delete(p); } catch { }
                Version target = ParseVer(tag);
                Version cur = typeof(ShellForm).Assembly.GetName().Version;
                if (target == null || _web == null || !_web.IsHandleCreated) return;

                if (cur >= target)
                {
                    string ftag = tag;
                    try { _web.BeginInvoke((Action)delegate { Post(new { type = "updated", version = ftag }); }); }
                    catch { }
                }
                else
                {
                    // Der Merker nennt eine neuere Fassung, als hier laeuft: der Tausch ist
                    // schiefgegangen und die bisherige Fassung wurde wiederhergestellt.
                    // Frueher blieb das voellig stumm - der Nutzer klickte auf
                    // "Jetzt aktualisieren" und danach war scheinbar nichts passiert.
                    AppLog.Warn("Update auf " + tag + " wurde nicht wirksam, es laeuft weiter " + cur + ".");
                    string ftag = tag;
                    try
                    {
                        _web.BeginInvoke((Action)delegate
                        {
                            Post(new { type = "updateFailedSilently", version = ftag });
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        static Version ParseVer(string tag)
        {
            if (tag == null) return null;
            string s = tag.TrimStart('v', 'V', ' ');
            StringBuilder sb = new StringBuilder();
            foreach (char c in s) { if (char.IsDigit(c) || c == '.') sb.Append(c); else break; }
            string v = sb.ToString().Trim('.');
            if (v.Length == 0) return null;
            if (v.IndexOf('.') < 0) v += ".0";
            Version res;
            return Version.TryParse(v, out res) ? res : null;
        }

        async void OnNavForShot(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            await Task.Delay(_shotWaitMs);
            try
            {
                using (FileStream fs = new FileStream(_shotPath, FileMode.Create))
                    await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
            }
            catch { }
            Close();
        }

        // ---------- Nachrichten aus dem UI ----------
        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw;
            try { raw = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (raw == null) return;

            Dictionary<string, object> m;
            try { m = _js.DeserializeObject(raw) as Dictionary<string, object>; }
            catch { return; }
            if (m == null) return;

            string type = Str(m, "type");
            if (type == "run")
            {
                int id = ToInt(m, "id");
                if (id < 0 || id >= _actions.Count) return;
                bool restore = ToBool(m, "restore");
                ReadPost(m);
                MaintenanceAction a = _actions[id];
                if (a.Special != null) return;   // braucht eine Eingabe, laeuft ueber den eigenen Befehl
                List<Job> jobs = new List<Job>();
                jobs.Add(new Job { Title = a.Title, Steps = BuildSteps(a, restore) });
                _runner.RunJobs(a.Title, jobs);
            }
            else if (type == "runQueue")
            {
                bool restore = ToBool(m, "restore");
                ReadPost(m);
                List<Job> jobs = new List<Job>();
                object idsObj;
                if (m.TryGetValue("ids", out idsObj) && idsObj is object[])
                {
                    // Ein Sicherungspunkt fuer die GANZE Warteschlange, ganz vorn. Vorher legte
                    // jede einzelne Aktion einen an; Windows drosselt auf einen je 24 Stunden,
                    // also wurden die uebrigen mit einer Warnung uebersprungen - das las sich wie
                    // ein Fehler, obwohl alles in Ordnung war.
                    bool restoreDone = false;
                    foreach (object o in (object[])idsObj)
                    {
                        int id;
                        try { id = Convert.ToInt32(o); } catch { continue; }
                        if (id < 0 || id >= _actions.Count) continue;
                        MaintenanceAction a = _actions[id];
                        if (a.Special != null) continue;   // Sonderaktion, nicht sammelbar

                        List<Step> steps = new List<Step>();
                        if (restore && a.WantsRestorePoint && !restoreDone)
                        {
                            steps.Add(RestoreStep());
                            restoreDone = true;
                        }
                        steps.AddRange(a.Steps);
                        jobs.Add(new Job { Title = a.Title, Steps = steps });
                    }
                }
                if (jobs.Count == 0) return;
                _runner.RunJobs("Warteschlange (" + jobs.Count + ")", jobs);
            }
            // --- Hauptweg -------------------------------------------------------
            else if (type == "startCheck") StartCheck();
            else if (type == "startFix") StartFix();
            // Ein Editor mit Administratorrechten koennte jede Datei des Systems
            // ueberschreiben - deshalb ueber die Oberflaeche des Nutzers oeffnen.
            else if (type == "openLog") Shell.OeffneImNutzerkontext(AppLog.PfadZumOeffnen());
            // Der Wunsch "danach herunterfahren" laesst sich auch NACH dem Start noch
            // aendern - genau dann faellt die Entscheidung ja, wenn der Lauf schon
            // begonnen hat und man weggehen will.
            else if (type == "setPost") ReadPost(m);
            else if (type == "restartNow") { _pendingPost = "restart"; _pendingDelay = 60; ScheduleShutdown(); _pendingPost = "none"; }
            // --- Speicher- und Registrierungs-Suche ----------------------------
            else if (type == "storageScan") StartStorageScan();
            else if (type == "storageClean") StorageClean(m);
            else if (type == "openBrocken") OpenBrocken(ToInt(m, "index"));
            else if (type == "registryScan") StartRegistryScan();
            else if (type == "registryClean") RegistryClean(m);
            else if (type == "openRegBackup") OpenRegBackup();
            // --------------------------------------------------------------------
            // Stoppt alles drei: den Pruef-/Reparaturablauf, eine Einzelaktion aus dem
            // Werkzeugkasten und einen Suchlauf. Die laufen in verschiedenen Threads,
            // ein Aufruf allein liesse die jeweils anderen weiterlaufen.
            else if (type == "cancel") { bool lief = EtwasLaeuft; if (_runner != null) _runner.Cancel(); CancelFlow(); CancelScan(); if (!lief) FlowIdle(); }
            else if (type == "cancelShutdown") CancelShutdown();
            else if (type == "ready") OnReady();
            else if (type == "openUpdate") OpenUpdate();
            else if (type == "startUpdate") BeginUpdate();
            else if (type == "save") SaveLog();
            else if (type == "win") Win(Str(m, "action"));
            else if (type == "resize") BeginResize(Str(m, "dir"));
            else if (type == "setNotify") _notifyEnabled = ToBool(m, "on");
            else if (type == "setZoom")
            {
                object v; double z = 1.0;
                if (m.TryGetValue("factor", out v) && v != null) { try { z = Convert.ToDouble(v); } catch { } }
                if (z < 0.7) z = 0.7; if (z > 2.5) z = 2.5;
                try { _web.ZoomFactor = z; } catch { }
                WriteZoom(z);
            }
            else if (type == "autostartList") Post(new { type = "autostart", items = Autostart.List() });
            // Klappt das Umschalten nicht, bekommt die Oberflaeche die WAHRE Liste zurueck.
            // Sonst zeigt der Schalter eine Aenderung, die es gar nicht gab.
            else if (type == "autostartSet")
            {
                bool ok = Autostart.SetEnabled(Str(m, "loc"), Str(m, "key"), ToBool(m, "enable"));
                if (!ok) Post(new { type = "autostart", items = Autostart.List(), fehlgeschlagen = true });
            }
            else if (type == "historyList") Post(new { type = "history", items = History.List() });
            else if (type == "historyClear") { History.Clear(); Post(new { type = "history", items = History.List() }); }
            else if (type == "restoreList") StartRestoreList();
            else if (type == "restoreCreate") RestoreCreate(Str(m, "desc"));
            else if (type == "restoreRevert") RestoreRevert(ToInt(m, "seq"));
            else if (type == "powerList") StartPowerList();
            else if (type == "powerSet") PowerSet(Str(m, "guid"));
            else if (type == "bloatList") StartBloatList();
            else if (type == "bloatRemove") BloatRemove(m);
            else if (type == "netDiag") NetDiag(Str(m, "target"));
            else if (type == "driverBackup") DriverBackup();
            else if (type == "scheduleStatus") SendScheduleStatus();
            else if (type == "scheduleCreate") ScheduleCreate(m);
            else if (type == "scheduleDelete") ScheduleDelete();
            else if (type == "selfStartGet") SelfStartGet();
            else if (type == "selfStartSet") SelfStartSet(ToBool(m, "on"));
            else if (type == "openStartupFolder") OpenStartupFolder(Str(m, "scope"));
        }

        // ---------- App-Selbststart (Autostart-Ansicht) ----------
        void SelfStartGet()
        {
            Thread t = new Thread(delegate ()
            {
                UiPost(new { type = "selfstart", on = Scheduler.StartTaskExists() });
            });
            t.IsBackground = true;
            t.Start();
        }

        void SelfStartSet(bool on)
        {
            string exe = Application.ExecutablePath;
            Thread t = new Thread(delegate ()
            {
                bool ok = Scheduler.StartTaskSet(on, exe);
                UiPost(new { type = "selfstart", on = Scheduler.StartTaskExists(), changed = ok });
            });
            t.IsBackground = true;
            t.Start();
        }

        void OpenStartupFolder(string scope)
        {
            // Explorer oeffnet den Autostart-Ordner (shell:-Moniker, kein Pfad-Gefrickel).
            // Ueber die Oberflaeche des Nutzers, damit der Explorer nicht erhoeht laeuft.
            string arg = scope == "common" ? "shell:common startup" : "shell:startup";
            Shell.OeffneImNutzerkontext("explorer.exe", arg);
        }

        void Win(string a)
        {
            if (a == "min") WindowState = FormWindowState.Minimized;
            else if (a == "max")
            {
                MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            }
            else if (a == "close") Close();
            else if (a == "drag")
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero); // WM_NCLBUTTONDOWN / HTCAPTION
            }
        }

        // Fenstergröße über die Greifzonen am Rand ändern
        void BeginResize(string dir)
        {
            if (WindowState != FormWindowState.Normal) return;
            int ht;
            switch (dir)
            {
                case "t": ht = 12; break;   // HTTOP
                case "b": ht = 15; break;   // HTBOTTOM
                case "l": ht = 10; break;   // HTLEFT
                case "r": ht = 11; break;   // HTRIGHT
                case "tl": ht = 13; break;  // HTTOPLEFT
                case "tr": ht = 14; break;  // HTTOPRIGHT
                case "bl": ht = 16; break;  // HTBOTTOMLEFT
                case "br": ht = 17; break;  // HTBOTTOMRIGHT
                default: return;
            }
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)ht, IntPtr.Zero); // WM_NCLBUTTONDOWN
        }

        // ---------- Runner-Callbacks -> ans UI ----------
        void Log(string text, LogKind k)
        {
            _log.AppendLine(text);
            Post(new { type = "log", text = text, kind = KindStr(k) });
        }
        void SetState(bool running) { Post(new { type = "state", running = running }); }
        void OnProgress(int pct) { Post(new { type = "progress", percent = pct }); }
        void Done(string title, LogKind k, string message, double seconds)
        {
            AppLog.Info("Lauf beendet: " + title + " - " + message + ".");
            History.Add(title, KindStr(k), message, seconds);
            Post(new { type = "done", title = title, kind = KindStr(k), message = message });

            // Steht noch ein nachgeholter Wartungslauf an, darf JETZT kein Abschalt-Countdown
            // starten: sonst faehrt der PC mitten in DISM oder SFC herunter und beschaedigt genau
            // das, was das Werkzeug reparieren soll. Der Wunsch bleibt gemerkt und greift nach
            // dem letzten Lauf.
            if (_autoRunPending)
            {
                Notify(title, message, k);
                _autoRunPending = false;
                if (_pendingPost != "none")
                    Log("●  Der Abschalt-Countdown startet erst nach der geplanten Wartung.", LogKind.Warn);
                RunScheduledJobsNow();
                return;
            }

            if (k != LogKind.Bad && _pendingPost != "none") ScheduleShutdown();
            _pendingPost = "none";
            Notify(title, message, k);
        }

        // ---------- Geplante Wartung, an die offene App uebergeben (--auto -> WM_WW_RUNAUTO) ----------
        bool _autoRunPending;

        // Rueckgabe true = Lauf uebernommen (gestartet oder fuer direkt danach vorgemerkt).
        // false (z. B. UI noch nicht bereit) laesst den --auto-Prozess still selbst laufen.
        bool OnScheduledRunRequested()
        {
            if (_runner == null) return false;
            // FlowRunning gehoert mit in die Pruefung: Waehrend des Hauptwegs laufen DISM und
            // SFC bereits: startet die geplante Wartung jetzt zusaetzlich, laufen zwei
            // DISM-Instanzen gleichzeitig auf dasselbe Windows-Abbild.
            if (_runner.Running || FlowRunning)
            {
                // Laufende Aktion nicht stoeren - die Wartung startet direkt danach.
                if (!_autoRunPending)
                {
                    _autoRunPending = true;
                    Log("●  Geplante Wartung wartet, bis die laufende Aktion abgeschlossen ist.", LogKind.Warn);
                }
                return true;
            }
            RunScheduledJobsNow();
            return true;
        }

        void RunScheduledJobsNow()
        {
            List<Job> jobs = new List<Job>();
            jobs.Add(new Job { Title = "Geplante Wartung", Steps = Catalog.AutoSet(Scheduler.ReadActions()) });
            // Ins Protokoll, nicht nur ins Fenster: Bei der Fehlersuche am 22.08.2026 war
            // genau diese Luecke die teuerste. Die geplante Wartung war zehn Minuten lang
            // mit DISM und SFC ueber das System gelaufen, ohne in app.log eine einzige
            // Zeile zu hinterlassen - im Protokoll sah es aus, als sei nichts geschehen.
            AppLog.Info("Geplante Wartung gestartet (Zeitplan, in der offenen App).");
            Log("●  Geplante Wartung wird jetzt automatisch ausgeführt (Zeitplan).", LogKind.Warn);
            _runner.RunJobs("Geplante Wartung", jobs);
        }

        List<Step> BuildSteps(MaintenanceAction a, bool restore)
        {
            List<Step> steps = new List<Step>();
            if (restore && a.WantsRestorePoint) steps.Add(RestoreStep());
            steps.AddRange(a.Steps);
            return steps;
        }

        // Grenzen der Wartezeit: 5 Sekunden bis 24 Stunden. Die Oberflaeche bietet 1 Minute
        // bis 24 Stunden an; die 5 Sekunden hier sind der aeltere, weitere Rahmen.
        const int MinDelay = 5;
        const int MaxDelay = 86400;

        void ReadPost(Dictionary<string, object> m)
        {
            _pendingPost = Str(m, "post");
            if (_pendingPost != "shutdown" && _pendingPost != "restart") _pendingPost = "none";

            int d = ToInt(m, "delay");
            if (d >= MinDelay && d <= MaxDelay)
            {
                _pendingDelay = d;
            }
            else
            {
                // Nicht stillschweigend korrigieren: Wer eine Wartezeit waehlt und eine
                // andere bekommt, soll das wenigstens im Protokoll wiederfinden.
                if (_pendingPost != "none")
                    AppLog.Warn("Wartezeit " + d + "s liegt ausserhalb von " + MinDelay + "-" + MaxDelay
                                + "s, es gilt " + _pendingDelay + "s.");
            }
        }

        void ScheduleShutdown()
        {
            string args = (_pendingPost == "restart" ? "-r" : "-s") + " -t " + _pendingDelay;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("shutdown.exe", args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
            string word = _pendingPost == "restart" ? "neu gestartet" : "heruntergefahren";
            Log("●  Der PC wird in " + _pendingDelay + "s " + word + " – Abbrechen über das Banner.", LogKind.Warn);
            Post(new { type = "shutdownScheduled", mode = _pendingPost, delay = _pendingDelay });
        }

        void CancelShutdown()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("shutdown.exe", "-a");
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
            Log("●  Herunterfahren abgebrochen.", LogKind.Good);
            Post(new { type = "shutdownCancelled" });
        }

        void Post(object o)
        {
            try { _web.CoreWebView2.PostWebMessageAsString(_js.Serialize(o)); }
            catch { }
        }
        void UiPost(object o)
        {
            if (_web != null && _web.IsHandleCreated)
            {
                try { _web.BeginInvoke((Action)delegate { Post(o); }); }
                catch { }
            }
        }

        void SaveLog()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "Textdatei (*.txt)|*.txt";
                d.FileName = "wartung-log.txt";
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    try { File.WriteAllText(d.FileName, _log.ToString()); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fehler"); }
                }
            }
        }

        Step RestoreStep()
        {
            return new Step
            {
                File = "powershell.exe",
                Args = "-NoProfile -ExecutionPolicy Bypass -Command \"try { Checkpoint-Computer -Description 'Wartungstool' -RestorePointType MODIFY_SETTINGS -EA Stop; 'Wiederherstellungspunkt erstellt.' } catch { 'Wiederherstellungspunkt uebersprungen: ' + $_.Exception.Message + ' (Hinweis: Windows legt standardmaessig hoechstens einen Punkt pro 24 Stunden an - der vorhandene ist dann noch aktuell.)' }\""
            };
        }

        // ---------- Wiederherstellungspunkte ----------
        void StartRestoreList()
        {
            Thread t = new Thread(delegate ()
            {
                List<object> items = RestorePoints.List();
                UiPost(new { type = "restorePoints", items = items });
            });
            t.IsBackground = true;
            t.Start();
        }

        void RestoreCreate(string desc)
        {
            string safe = SanitizeDesc(desc);
            // Frequenz-Drossel kurz aufheben, damit ein bewusst angelegter Punkt nicht still verworfen wird.
            string cmd =
                "try { " +
                "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' -Name 'SystemRestorePointCreationFrequency' -Value 0 -EA SilentlyContinue; " +
                "Checkpoint-Computer -Description '" + safe + "' -RestorePointType MODIFY_SETTINGS -EA Stop; " +
                "'Wiederherstellungspunkt wurde angelegt.' " +
                "} catch { 'Konnte nicht angelegt werden (Systemschutz aktiv?): ' + $_.Exception.Message }";
            Step s = new Step { File = "powershell.exe", Args = "-NoProfile -ExecutionPolicy Bypass -Command \"" + cmd + "\"" };
            List<Job> jobs = new List<Job>();
            jobs.Add(new Job { Title = "Wiederherstellungspunkt anlegen", Steps = new List<Step> { s } });
            _runner.RunJobs("Wiederherstellungspunkt anlegen", jobs);
        }

        void RestoreRevert(int seq)
        {
            if (seq <= 0) return; // Sequenznummer ist eine reine Zahl -> keine Injektion moeglich
            string cmd =
                "try { Restore-Computer -RestorePoint " + seq + " -Confirm:$false -EA Stop; 'Wiederherstellung gestartet - der PC startet neu.' } " +
                "catch { 'Wiederherstellung fehlgeschlagen: ' + $_.Exception.Message }";
            Step s = new Step { File = "powershell.exe", Args = "-NoProfile -ExecutionPolicy Bypass -Command \"" + cmd + "\"" };
            List<Job> jobs = new List<Job>();
            jobs.Add(new Job { Title = "Windows auf einen früheren Stand zurücksetzen", Steps = new List<Step> { s } });
            _runner.RunJobs("Wiederherstellung", jobs);
        }

        // Beschreibung fuer Checkpoint-Computer absichern: nur unkritische Zeichen, begrenzte Laenge.
        static string SanitizeDesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Manueller Punkt (Windows-Wartung)";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.' || c == ':' || c == '(' || c == ')')
                    sb.Append(c);
                if (sb.Length >= 60) break;
            }
            string r = sb.ToString().Trim();
            return r.Length == 0 ? "Manueller Punkt (Windows-Wartung)" : r;
        }

        // ---------- Energieplaene ----------
        void StartPowerList()
        {
            Thread t = new Thread(delegate ()
            {
                List<object> items = PowerPlans.List();
                UiPost(new { type = "powerPlans", items = items });
            });
            t.IsBackground = true;
            t.Start();
        }

        void PowerSet(string guid)
        {
            Guid x;
            if (string.IsNullOrEmpty(guid) || !Guid.TryParse(guid, out x)) return;
            Thread t = new Thread(delegate ()
            {
                PowerPlans.SetActive(guid);
                List<object> items = PowerPlans.List();
                UiPost(new { type = "powerPlans", items = items });
            });
            t.IsBackground = true;
            t.Start();
        }

        // ---------- Bloatware-Entferner ----------
        void StartBloatList()
        {
            Thread t = new Thread(delegate ()
            {
                List<object> items = AppxCleaner.List();
                UiPost(new { type = "bloatPackages", items = items });
            });
            t.IsBackground = true;
            t.Start();
        }

        void BloatRemove(Dictionary<string, object> m)
        {
            bool restore = ToBool(m, "restore");

            // Jede angefragte PackageFullName streng pruefen: nur gueltige Zeichen, im Katalog,
            // nicht kritisch (AppxCleaner.IsRemovable). Ungeprueftes geht NIE in den Befehl.
            List<string> fulls = new List<string>();
            object arr;
            if (m.TryGetValue("fulls", out arr) && arr is object[])
            {
                foreach (object o in (object[])arr)
                {
                    string full = o == null ? "" : o.ToString();
                    if (!AppxCleaner.IsRemovable(full)) continue;
                    if (fulls.Contains(full)) continue;
                    fulls.Add(full);
                }
            }

            if (fulls.Count == 0)
            {
                Post(new { type = "log", text = "Keine gueltigen Pakete zum Entfernen.", kind = "bad" });
                Post(new { type = "done", title = "Bloatware entfernen", kind = "bad", message = "Nichts entfernt" });
                return;
            }

            List<Step> steps = new List<Step>();
            if (restore) steps.Add(BloatRestoreStep());
            foreach (string full in fulls)
                steps.Add(BloatRemoveStep(full, AppxCleaner.LabelFor(full)));

            string title = "Bloatware entfernen (" + fulls.Count + ")";
            List<Job> jobs = new List<Job>();
            jobs.Add(new Job { Title = title, Steps = steps });
            _runner.RunJobs(title, jobs);
        }

        // Zuverlaessiger Wiederherstellungspunkt vor dem Entfernen (Frequenz-Drossel kurz aufheben).
        // Ein uebersprungener Punkt (Systemschutz aus) darf den Lauf nicht als Fehler werten.
        Step BloatRestoreStep()
        {
            string cmd =
                "try { " +
                "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' -Name 'SystemRestorePointCreationFrequency' -Value 0 -EA SilentlyContinue; " +
                "Checkpoint-Computer -Description 'Vor Bloatware-Entfernung' -RestorePointType MODIFY_SETTINGS -EA Stop; " +
                "'Wiederherstellungspunkt angelegt.' " +
                "} catch { 'Es wurde kein Sicherungspunkt angelegt: ' + $_.Exception.Message }";
            return new Step
            {
                File = "powershell.exe",
                Args = "-NoProfile -ExecutionPolicy Bypass -Command \"" + cmd + "\"",
                IgnoreExit = true
            };
        }

        // full ist bereits per AppxCleaner.IsRemovable geprueft (nur [A-Za-z0-9._-]) -> sicher in '' .
        Step BloatRemoveStep(string full, string label)
        {
            string safe = AppxCleaner.SafeLabel(label);
            string cmd =
                "$ErrorActionPreference='Stop'; " +
                "try { Remove-AppxPackage -Package '" + full + "' -EA Stop; 'Entfernt: " + safe + "' } " +
                "catch { 'Fehler bei " + safe + ": ' + $_.Exception.Message; exit 1 }";
            return new Step
            {
                File = "powershell.exe",
                Args = "-NoProfile -ExecutionPolicy Bypass -Command \"" + cmd + "\""
            };
        }

        // ---------- Netzwerk-Diagnose ----------
        void NetDiag(string target)
        {
            string t = SanitizeHost(target);
            if (t == null)
            {
                Post(new { type = "log", text = "Ungueltiges Ziel - erlaubt sind nur Buchstaben, Zahlen, Punkt, Doppelpunkt und Bindestrich.", kind = "bad" });
                Post(new { type = "done", title = "Netzwerk-Diagnose", kind = "bad", message = "Ungueltiges Ziel" });
                return;
            }
            // ping.exe/tracert.exe werden direkt (ohne Shell) aufgerufen -> der Parameter wird nie interpretiert.
            List<Step> steps = new List<Step>();
            steps.Add(new Step { File = "ping.exe", Args = "-n 4 " + t });
            steps.Add(new Step { File = "tracert.exe", Args = "-d -h 20 " + t });
            List<Job> jobs = new List<Job>();
            jobs.Add(new Job { Title = "Netzwerk-Diagnose: " + t, Steps = steps });
            _runner.RunJobs("Netzwerk-Diagnose: " + t, jobs);
        }

        // Hostname/IP streng auf unkritische Zeichen begrenzen.
        static string SanitizeHost(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Trim();
            if (s.Length == 0 || s.Length > 253) return null;
            foreach (char c in s)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                          || c == '.' || c == '-' || c == ':';
                if (!ok) return null;
            }
            return s;
        }

        // ---------- Treiber-Backup ----------
        void DriverBackup()
        {
            string folder;
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Ordner fuer die Treiber-Sicherung aussuchen";
                d.ShowNewFolderButton = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                folder = d.SelectedPath;
            }
            if (string.IsNullOrEmpty(folder)) return;
            // Pfad stammt aus dem System-Ordnerdialog; pnputil wird direkt (ohne Shell) gestartet.
            List<Step> steps = new List<Step>();
            steps.Add(new Step { File = "pnputil.exe", Args = "/export-driver * \"" + folder + "\"" });
            List<Job> jobs = new List<Job>();
            jobs.Add(new Job { Title = "Treiber-Backup nach " + folder, Steps = steps });
            _runner.RunJobs("Treiber-Backup", jobs);
        }

        // ---------- Geplante Wartung ----------
        void SendScheduleStatus()
        {
            Thread t = new Thread(delegate ()
            {
                bool exists = Scheduler.Exists();
                object cfg = Scheduler.Read();
                UiPost(new { type = "schedule", exists = exists, config = cfg });
            });
            t.IsBackground = true;
            t.Start();
        }

        void ScheduleCreate(Dictionary<string, object> m)
        {
            string mode = Str(m, "mode");
            int hh = ToInt(m, "hh");
            int mi = ToInt(m, "mm");

            if (mode != "daily" && mode != "weekly" && mode != "monthly") return;
            if (hh < 0 || hh > 23 || mi < 0 || mi > 59) return;

            // Wochentage: nur Whitelist-Tokens, dedupliziert, in Wochenreihenfolge (fuer /D MON,WED,...)
            string[] valid = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
            string dSpec = "";
            string[] daysArr = null;
            if (mode == "weekly")
            {
                List<string> chosen = new List<string>();
                object dv;
                if (m.TryGetValue("days", out dv) && dv is object[])
                {
                    foreach (string tok in valid)
                        foreach (object o in (object[])dv)
                            if (tok.Equals(o as string) && !chosen.Contains(tok)) chosen.Add(tok);
                }
                if (chosen.Count == 0) return;
                daysArr = chosen.ToArray();
                dSpec = string.Join(",", daysArr);
            }
            int dom = 1;
            if (mode == "monthly")
            {
                dom = ToInt(m, "dom");
                if (dom < 1 || dom > 31) return;
                dSpec = dom.ToString();
            }

            // Aufgaben-Satz: nur bekannte Katalog-Schluessel, Katalogreihenfolge; null = Standard.
            string[] actionsArr = null;
            object av;
            if (m.TryGetValue("actions", out av) && av is object[])
            {
                List<string> keys = new List<string>();
                foreach (AutoItem it in Catalog.AutoCatalog())
                    foreach (object o in (object[])av)
                        if (it.Key.Equals(o as string) && !keys.Contains(it.Key)) keys.Add(it.Key);
                if (keys.Count == 0) return; // explizit leer gewaehlt -> ungueltig (UI verhindert das)
                actionsArr = keys.ToArray();
            }

            string hhs = hh.ToString("00");
            string mms = mi.ToString("00");
            string exe = Application.ExecutablePath;
            string fMode = mode, fSpec = dSpec;
            string[] fDays = daysArr, fActions = actionsArr;
            int fDom = dom;

            Thread t = new Thread(delegate ()
            {
                bool ok = Scheduler.Create(fMode, fSpec, hhs, mms, exe);
                if (ok) Scheduler.Write(fMode, fDays, fDom, hhs + ":" + mms, fActions);
                UiPost(new { type = "schedule", exists = Scheduler.Exists(), config = Scheduler.Read(), justCreated = ok });
            });
            t.IsBackground = true;
            t.Start();
        }

        void ScheduleDelete()
        {
            Thread t = new Thread(delegate ()
            {
                Scheduler.Delete();
                Scheduler.Clear();
                UiPost(new { type = "schedule", exists = Scheduler.Exists(), config = (object)null });
            });
            t.IsBackground = true;
            t.Start();
        }

        // ---------- Rahmenloses Fenster: Größe ändern ----------
        protected override void WndProc(ref Message m)
        {
            // Geplanter Wartungslauf, vom --auto-Prozess an diese offene Instanz uebergeben.
            // Result 1 = uebernommen (Handshake); sonst faellt --auto auf den stillen Lauf zurueck.
            if (Native.WM_WW_RUNAUTO != 0 && m.Msg == (int)Native.WM_WW_RUNAUTO)
            {
                m.Result = OnScheduledRunRequested() ? (IntPtr)1 : IntPtr.Zero;
                return;
            }
            const int WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                Point p = PointToClient(new Point(m.LParam.ToInt32()));
                int g = 7, w = ClientSize.Width, h = ClientSize.Height;
                bool l = p.X <= g, r = p.X >= w - g, t = p.Y <= g, b = p.Y >= h - g;
                if (l && t) m.Result = (IntPtr)13;
                else if (r && t) m.Result = (IntPtr)14;
                else if (l && b) m.Result = (IntPtr)16;
                else if (r && b) m.Result = (IntPtr)17;
                else if (l) m.Result = (IntPtr)10;
                else if (r) m.Result = (IntPtr)11;
                else if (t) m.Result = (IntPtr)12;
                else if (b) m.Result = (IntPtr)15;
                return;
            }
            base.WndProc(ref m);
        }

        // ---------- Helfer ----------
        static string Str(Dictionary<string, object> m, string k)
        {
            object v;
            return (m.TryGetValue(k, out v) && v != null) ? v.ToString() : "";
        }
        static int ToInt(Dictionary<string, object> m, string k)
        {
            object v;
            if (m.TryGetValue(k, out v) && v != null) { try { return Convert.ToInt32(v); } catch { } }
            return -1;
        }
        static bool ToBool(Dictionary<string, object> m, string k)
        {
            object v;
            return (m.TryGetValue(k, out v) && v is bool) && (bool)v;
        }
        static string KindStr(LogKind k)
        {
            switch (k)
            {
                case LogKind.Header: return "head";
                case LogKind.Good: return "good";
                case LogKind.Bad: return "bad";
                case LogKind.Warn: return "warn";
                case LogKind.Dim: return "dim";
                default: return "norm";
            }
        }
    }
}
