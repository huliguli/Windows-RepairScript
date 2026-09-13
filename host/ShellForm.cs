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
using WartungsToolbox.Kern;

namespace WartungsToolbox
{
    public partial class ShellForm : Form
    {
        WebView2 _web;
        CommandRunner _runner;
        readonly List<MaintenanceAction> _actions = Catalog.All();
        readonly StringBuilder _log = new StringBuilder();
        readonly JavaScriptSerializer _js = new JavaScriptSerializer();

        // Einmaliger Nachlauf nach dem naechsten Done des Runners (Zeitplan anlegen/loeschen
        // melden ihren Stand erst, wenn der Plan durch ist). Wird vor dem Aufruf geleert.
        Action<LogKind> _nachLauf;

        // Die Selbststart-Aufgabe stammt aus 8.0 (/RL HIGHEST) und liess sich aus diesem
        // Prozess nicht erneuern: die Oberflaeche zeigt dann den Hinweis (Nachricht selfstart,
        // Feld veraltet). Gesetzt einmal beim Start, nachgezogen bei jedem Umschalten.
        volatile bool _selfStartVeraltet;

        // Stand von HelferClient.Verbunden bei der letzten Nachricht admin. Nach dem ersten
        // UAC-Dialog eines Werkzeugs ist ein Helfer da, die Startzeile wuesste sonst nichts
        // davon: AdminNachmelden vergleicht und schickt admin erneut, wenn es sich geaendert hat.
        bool _helferGemeldet;

        readonly string _shotPath;
        readonly string _view;
        readonly int _shotWaitMs;

        string _pendingPost = "none";
        int _pendingDelay = 60;

        // Ein Abschalt-Countdown von shutdown.exe laeuft (Entwurf, Abschnitt 14, B18): gesetzt,
        // sobald shutdown.exe den Befehl angenommen hat (Exit 0), geloescht in CancelShutdown
        // und bei Exit 1190 (Windows: es lief schon einer, der hier ist nicht unserer).
        // StartAbgelehnt (CheckFlow) liest es und bricht den Countdown ab, bevor ein neuer
        // Lauf beginnt: sonst faehrt der PC bei Sekunde 60 mitten in DISM herunter.
        // Geschrieben im Hintergrund-Thread von ShutdownBefehl, gelesen im UI-Thread.
        volatile bool _countdownAktiv;

        // 0/1: das In-App-Update laeuft (Download, Pruefung, Austausch; Entwurf, Abschnitt 14,
        // B11/B28). Gesetzt in BeginUpdate per Interlocked.CompareExchange, geloescht im finally
        // des Update-Threads, NICHT aber nach dem Start von Batch oder Installer (die App
        // beendet sich dann). Ein zweiter Klick auf "Jetzt aktualisieren" raeumte vorher den
        // laufenden Download ab (gleicher Arbeitsordner, update.zip mit FileMode.Create).
        int _updateLaeuft;

        /// <summary>true, solange das In-App-Update laeuft. Darf von jedem Thread aus gelesen werden.</summary>
        bool UpdateLaeuft { get { return Volatile.Read(ref _updateLaeuft) != 0; } }

        // Das Fenster wurde waehrend eines Runner-Laufs geschlossen (OnClosingWhileBusy, B19):
        // der Abbruch ist durch, Done ist unterwegs; Done schliesst das Fenster dann selbst.
        bool _schliessenNachDone;

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
            // Belegaufnahmen schreiben nie in den Absichtsspeicher des Rechners: eigene Datei
            // neben dem Shot-Datenordner, damit ein Screenshot-Lauf keine echte Antwort hinterlaesst.
            if (shotPath != null)
                Kern.Entscheidungen.PfadFuerProbe = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindowsWartung", "WebView2_shot", "entscheidungen.json");
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
        ///
        /// Laeuft der Runner oder der Hauptweg (seit 8.1 im Helfer), fragt das Fenster nach:
        /// das Ende der Pipe bricht den laufenden Plan ab (Baum-Kill im Helfer), und ein
        /// halbes "sfc /scannow" soll niemand aus Versehen ausloesen. Geht das Fenster zu,
        /// bekommt der Helfer "ende" (HelferClient.Beenden); bleibt es offen, bleibt auch er.
        ///
        /// Vor dem "ende" wartet das Fenster bis 3 s auf das Ende des abgebrochenen Laufs
        /// (Entwurf, Abschnitt 14, B19): der Runner schreibt "Lauf beendet" im Hintergrund-
        /// Thread und stellt Done zu (Verlaufseintrag), der Hauptweg schreibt Protokoll-Ende
        /// und Verlauf selbst; ohne das Warten endete der Prozess mitten darin, und der Lauf
        /// hatte einen Anfang, aber kein Ende. Beim Runner wird das Schliessen dazu vertagt,
        /// bis Done zugestellt ist (_schliessenNachDone).
        /// </summary>
        void OnClosingWhileBusy(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (ScanEntferntGerade)
                {
                    e.Cancel = true;
                    try
                    {
                        MessageBox.Show(this,
                            "Es wird gerade aufgeräumt. Bitte warten Sie einen Moment, bis der Vorgang " +
                            "abgeschlossen ist. Danach lässt sich das Fenster wie gewohnt schließen.",
                            "Windows-Wartung", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch { }
                    return;
                }

                bool runnerLaeuft = _runner != null && _runner.Running;
                if (runnerLaeuft || FlowRunning)
                {
                    string was = runnerLaeuft && !string.IsNullOrEmpty(_runner.Title)
                        ? "„" + _runner.Title + "“"
                        : "eine Prüfung oder Reparatur";
                    DialogResult r = DialogResult.Yes;
                    try
                    {
                        r = MessageBox.Show(this,
                            "Gerade läuft " + was + ". Wird das Fenster jetzt geschlossen, bricht dieser " +
                            "Lauf ab; Ihrem PC passiert dabei nichts, der Lauf ist nur nicht zu Ende.\n\n" +
                            "Trotzdem schließen?",
                            "Windows-Wartung", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2);
                    }
                    catch { }
                    if (r != DialogResult.Yes)
                    {
                        e.Cancel = true;
                        AppLog.Info("Schließen abgebrochen: " + was + " läuft weiter.");
                        return;
                    }
                    AppLog.Info("Fenster wird geschlossen, obwohl " + was + " läuft: der Lauf wird abgebrochen.");
                    if (runnerLaeuft) _runner.Cancel();
                    CancelFlow();

                    // Abbruch ist unterwegs; jetzt das Ende abwarten, bevor die Pipe schliesst.
                    // Bis 3 s je Weg: der Helfer beantwortet den Abbruch sofort (Baum-Kill),
                    // der Rest ist Verlauf und Protokoll. Steckt der Lauf noch im UAC-Dialog,
                    // laeuft die Frist aus, und das Fenster geht trotzdem zu.
                    if (runnerLaeuft)
                    {
                        bool zuEnde = false;
                        try { zuEnde = _runner.LaufAbwarten(3000); }
                        catch (Exception ex) { AppLog.Warn("Auf das Ende des Laufs warten: " + ex.Message); }
                        if (!zuEnde) AppLog.Warn("Der Lauf " + was + " war nach 3 s noch nicht zu Ende; das Fenster schließt trotzdem.");
                        else if (_runner.Running && !_schliessenNachDone)
                        {
                            // Der Thread ist durch, Done liegt als BeginInvoke in der Warteschlange
                            // (Running faellt erst dort). Ginge das Fenster jetzt zu, verwuerfe
                            // WinForms die wartende Zustellung: kein Verlaufseintrag, kein "done".
                            // Also vertagen: Done schliesst das Fenster selbst (dann laeuft nichts
                            // mehr, und dieser Weg geht ohne Rueckfrage durch). Kommt Done wider
                            // Erwarten nicht, schliesst der naechste Klick auf X ohne Vertagung.
                            _schliessenNachDone = true;
                            e.Cancel = true;
                            AppLog.Info("Schließen vertagt, bis der Abschluss von " + was + " zugestellt ist.");
                            return;
                        }
                    }
                    Thread hauptweg = _flowThread;
                    if (hauptweg != null && hauptweg.IsAlive)
                    {
                        bool zuEnde = false;
                        try { zuEnde = hauptweg.Join(3000); }
                        catch (Exception ex) { AppLog.Warn("Auf das Ende des Hauptwegs warten: " + ex.Message); }
                        if (!zuEnde) AppLog.Warn("Der Hauptweg war nach 3 s noch nicht zu Ende; das Fenster schließt trotzdem.");
                    }
                }
            }

            // Jeder Weg, der das Fenster wirklich schliesst (X, Application.Exit beim Update,
            // Abmelden): der Helfer bekommt "ende" und beendet sich. Ohne diesen Aufruf bliebe
            // ein erhoehter Prozess bis zu 10 Minuten (Leerlauf) stehen.
            try { HelferClient.Beenden(); }
            catch (Exception ex) { AppLog.Warn("Helfer beim Schließen beenden: " + ex.Message); }
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
                _tray.ShowBalloonTip(5000, "Windows-Wartung", title + ": " + message, ic);
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
            // Schrittzaehler des Ablaufbildschirms (Warteschlange: "Schritt i von n"), nicht
            // das Protokoll: die Kopfzeile je Schritt schreibt der Helfer selbst. Der Helfer
            // meldet (i, n, Titel) nur bei n > 1 (Entwurf, Abschnitt 12); die Oberflaeche
            // kennt die Nachricht flowStep schon vom Hauptweg.
            _runner.OnStep = delegate (int schritt, int gesamt, string label)
            {
                Post(new { type = "flowStep", index = schritt, label = label ?? "" });
            };

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
            PruefeSelbststartAufgabe();   // 8.0-Aufgabe mit HIGHEST erkennen (Entwurf, Abschnitt 12)
        }

        /// <summary>
        /// Einmal beim Start: stammt die Selbststart-Aufgabe aus 8.0 (/RL HIGHEST), wuerde sie
        /// die asInvoker-App bei jeder Anmeldung erhoeht starten. Scheduler.StartTaskAuffrischen
        /// legt sie ohne Rechte neu an, wenn das aus diesem Kontext geht; sonst bleibt sie
        /// veraltet, das steht im Protokoll (Warnung aus dem Scheduler) und geht als Feld
        /// veraltet an die Oberflaeche, damit sie den Hinweis zeigen kann.
        /// Drei schtasks-Aufrufe, deshalb im Hintergrund.
        ///
        /// Laeuft der Host selbst erhoeht (genau das tut die 8.0-Aufgabe bei jeder Anmeldung,
        /// bis sie umgestellt ist), wird NICHTS neu angelegt (Entwurf, Abschnitt 14, B33):
        /// "/Create /XML /F" gelaenge erhoeht, die neue Aufgabe gehoerte aber wieder der
        /// Administratorengruppe, truege LeastPrivilege (also nicht mehr "veraltet"), und der
        /// nicht erhoehte Host koennte sie nie mehr abschalten. Die Aufgabe bleibt veraltet,
        /// die Oberflaeche bekommt hinweis "erhoeht" dazu.
        /// </summary>
        void PruefeSelbststartAufgabe()
        {
            string exe = Application.ExecutablePath;
            bool erhoeht = IsElevated();
            Thread t = new Thread(delegate ()
            {
                try
                {
                    if (!Scheduler.StartTaskVeraltet()) return;
                    if (erhoeht)
                    {
                        _selfStartVeraltet = true;
                        AppLog.Warn(SelbststartErhoehtGrund("Die Selbststart-Aufgabe aus 8.0 wurde nicht erneuert"));
                        UiPost(new { type = "selfstart", on = true, veraltet = true, hinweis = "erhoeht" });
                        return;
                    }
                    bool erneuert = Scheduler.StartTaskAuffrischen(exe);
                    _selfStartVeraltet = !erneuert && Scheduler.StartTaskVeraltet();
                    if (!_selfStartVeraltet) return;
                    UiPost(new { type = "selfstart", on = true, veraltet = true });
                }
                catch (Exception ex) { AppLog.Warn("Selbststart-Aufgabe prüfen: " + ex.Message); }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>Ein Satz fuer app.log, warum der erhoehte Host keine Selbststart-Aufgabe anlegt.</summary>
        static string SelbststartErhoehtGrund(string was)
        {
            return was + ": dieser Start läuft mit Administratorrechten, und eine jetzt angelegte Aufgabe gehörte " +
                   "der Administratorengruppe; der normale Start könnte sie nie mehr abschalten. Bitte Windows-Wartung " +
                   "einmal normal starten (Doppelklick, ohne Administratorrechte) und den Selbststart dort einschalten.";
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
                    // Belegaufnahme (--shot): nur dann darf die Oberflaeche ihre Haken fuer
                    // Screenshots ziehen (Pruefung von selbst starten, Frage beantworten).
                    shot = _shotPath != null,
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
        ///
        /// Seit 8.1 laeuft die Oberflaeche ohne Rechte (on = false im Normalfall); helfer sagt,
        /// ob ein Ausfuehrer ohne UAC-Dialog da ist (erhoeht oder lebende Helfer-Pipe), abnahme,
        /// ob der Helfer von aussen kam (--pipe, Testweg ohne Dialog).
        /// </summary>
        void SendAdmin()
        {
            string laeuftAls = "", angemeldet = "";
            bool fremd = false;
            try { fremd = Nutzerkontext.AnderesKontoAlsAngemeldet(out laeuftAls, out angemeldet); }
            catch (Exception ex) { AppLog.Warn("Kontopruefung fehlgeschlagen: " + ex.Message); }

            if (fremd)
                AppLog.Info("Läuft als '" + laeuftAls + "', angemeldet ist '" + angemeldet + "'.");

            bool helfer = false, abnahme = false;
            try { helfer = HelferClient.Verbunden; abnahme = HelferClient.Abnahmeweg; }
            catch (Exception ex) { AppLog.Warn("Helfer-Stand nicht lesbar: " + ex.Message); }
            _helferGemeldet = helfer;

            try
            {
                Post(new
                {
                    type = "admin",
                    on = IsElevated(),
                    helfer = helfer,
                    abnahme = abnahme,
                    fremdesKonto = fremd,
                    laeuftAls = laeuftAls,
                    angemeldet = angemeldet,
                });
            }
            catch { }
        }

        /// <summary>
        /// Am Ende eines Laufs (Done, NachlaufPruefen): hat sich HelferClient.Verbunden seit der
        /// letzten Nachricht admin geaendert (Helfer per UAC-Dialog gekommen, oder nach 10 Minuten
        /// Leerlauf gegangen), bekommt die Oberflaeche den Stand erneut. UI-Thread.
        /// </summary>
        void AdminNachmelden()
        {
            bool helfer = false;
            try { helfer = HelferClient.Verbunden; }
            catch (Exception ex) { AppLog.Warn("Helfer-Stand nicht lesbar: " + ex.Message); return; }
            if (helfer == _helferGemeldet) return;
            SendAdmin();
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

        /// <summary>
        /// Was gerade laeuft, als Satzteil fuer eine Absage: „Tiefenprüfung“ (Titel des Runners),
        /// "eine Prüfung oder Reparatur" (Hauptweg), "eine Suche nach Speicherfressern oder
        /// ungültigen Einträgen" (Suchlauf), "die geplante Wartung im Hintergrund" (der
        /// --auto-Prozess haelt den Mutex, Entwurf Abschnitt 14 B15) und "das Update" (B11);
        /// null, wenn nichts laeuft. Liest nur Thread-Zustaende und darf von jedem Thread aus
        /// gerufen werden. ohneUpdate = true fuer die Pruefungen des Update-Wegs selbst, der
        /// sich sonst im eigenen Flag saehe.
        /// </summary>
        string LaufendesWas()
        {
            return LaufendesWas(false);
        }

        string LaufendesWas(bool ohneUpdate)
        {
            if (FlowRunning) return "eine Prüfung oder Reparatur";
            if (ScanRunning) return "eine Suche nach Speicherfressern oder ungültigen Einträgen";
            if (_runner != null && _runner.Running)
                return string.IsNullOrEmpty(_runner.Title) ? "eine andere Aufgabe" : "„" + _runner.Title + "“";
            // Ohne Helfer gibt die offene App die geplante Wartung an den --auto-Prozess zurueck;
            // der ist dieselbe EXE und haelt sie 10 bis 20 Minuten (DISM, SFC). Ein Update
            // traefe mit robocopy auf die gesperrte Datei und rollte danach zurueck.
            if (GeplanteWartungLaeuft()) return "die geplante Wartung im Hintergrund";
            if (!ohneUpdate && UpdateLaeuft) return "das Update";
            return null;
        }

        /// <summary>
        /// Update abgesagt, weil etwas laeuft: "ende" an den Helfer bei laufendem Plan ist ein
        /// Baum-Kill (Entwurf, Abschnitt 12), und ein halbes "sfc /scannow" darf kein Klick auf
        /// "Jetzt aktualisieren" ausloesen. Die Leiste kommt danach zurueck (Nachricht update),
        /// damit der Klick nach dem Lauf wiederholt werden kann. Von jedem Thread aus rufbar.
        /// </summary>
        void UpdateWartetAuf(string laeuft)
        {
            AppLog.Info("Update nicht gestartet: es läuft " + laeuft + ".");
            UiPost(new { type = "updateError", message = "Gerade läuft " + laeuft + "; das Update startet erst, wenn der Lauf zu Ende ist." });
            UiPost(new { type = "update", version = _updateTag ?? "", notes = "" });
        }

        /// <summary>
        /// Zweiter Klick auf "Jetzt aktualisieren", waehrend das Update laeuft: nur ein Hinweis,
        /// die Leiste bleibt (Feld laeuft = true; die Oberflaeche zeigt dann eine Meldung am
        /// Rand statt des Fehlerdialogs und laesst die Leiste stehen). Nichts wird abgeraeumt.
        /// </summary>
        void UpdateLaeuftBereits()
        {
            AppLog.Info("Update nicht erneut gestartet: es läuft bereits.");
            Post(new { type = "updateError", message = "Das Update läuft bereits.", laeuft = true });
        }

        /// <summary>Flag des laufenden Updates loeschen; mehrfach rufbar (finally und Wiederholungsangebot).</summary>
        void UpdateFreigeben()
        {
            Interlocked.Exchange(ref _updateLaeuft, 0);
        }

        /// <summary>
        /// UAC-Dialog fuer Batch oder Installer abgelehnt (Entwurf, Abschnitt 14, B28): keine
        /// Fehlermeldung mit "von Hand installieren", sondern das Angebot, es erneut zu
        /// versuchen. Erst das Flag freigeben, dann die Leiste zurueckholen (Nachricht update),
        /// damit der naechste Klick nicht an "läuft bereits" scheitert. Der Helfer war vor dem
        /// Dialog schon beendet; beim naechsten Versuch fragt Windows erneut.
        /// </summary>
        void UpdateAbgelehnt(string wofuer)
        {
            AppLog.Info("Update: der UAC-Dialog für " + wofuer + " wurde abgelehnt; die Leiste bleibt, ein neuer Versuch ist möglich.");
            UpdateFreigeben();
            UiPost(new { type = "updateError", message = UpdateOhneRechte + " Sie können es erneut versuchen: ein Klick auf „Jetzt aktualisieren“ genügt.", abgelehnt = true });
            UiPost(new { type = "update", version = _updateTag ?? "", notes = "" });
        }

        void BeginUpdate()
        {
            if (string.IsNullOrEmpty(_updateAsset))
            {
                Post(new { type = "updateError", message = "Kein Download-Paket im Release gefunden." });
                return;
            }
            // Reihenfolge (alles im UI-Thread, nur der Update-Thread loescht das Flag wieder):
            // 1. laeuft schon ein Update, nur der Hinweis; 2. laeuft etwas anderes, Absage mit
            //    Grund; 3. das Flag setzen, dann erst der Thread. LaufendesWas(true) laesst das
            //    eigene Flag aus, sonst saehe sich der Update-Weg selbst im Weg.
            if (UpdateLaeuft) { UpdateLaeuftBereits(); return; }
            // Erst pruefen, dann laden: waehrend eines Laufs wird gar nicht erst heruntergeladen.
            // Vor dem Start von Batch oder Installer wird noch einmal geprueft (FinishUpdate,
            // UpdateViaInstaller): der Download dauert, und in der Zeit kann ein Werkzeug gestartet sein.
            string laeuft = LaufendesWas(true);
            if (laeuft != null) { UpdateWartetAuf(laeuft); return; }
            if (Interlocked.CompareExchange(ref _updateLaeuft, 1, 0) != 0) { UpdateLaeuftBereits(); return; }
            Thread t = new Thread(delegate ()
            {
                string tmp = UpdateWorkDir();
                // true = Batch oder Installer laufen, die App beendet sich gleich: das Flag
                // bleibt dann gesetzt, damit kein weiterer Klick mehr durchkommt.
                bool austauschLaeuft = false;
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
                        austauschLaeuft = UpdateViaInstaller(tmp);
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

                    // Bleibt auf diesem Hintergrund-Thread: der Start der Austausch-Batch
                    // zeigt den UAC-Dialog, und der darf den Thread der Oberflaeche nicht halten.
                    austauschLaeuft = FinishUpdate(tmp, zip);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Update fehlgeschlagen", ex);
                    UiPost(new { type = "updateError", message = ex.Message });
                }
                finally
                {
                    if (!austauschLaeuft) UpdateFreigeben();
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        const string UserAgent = "WindowsWartung-Updater";
        const long MaxUpdateBytes = 200L * 1024 * 1024;   // Reissleine gegen endlose Antworten

        /// <summary>Satz fuer die Oberflaeche, wenn der UAC-Dialog beim Update abgelehnt wurde.</summary>
        const string UpdateOhneRechte = "Ohne Administratorrechte kann das Update nicht installiert werden.";

        /// <summary>
        /// Aktualisierung ueber den Installer des Releases. Laeuft still durch und ersetzt
        /// die Installation sauber, inklusive Eintrag unter "Apps und Features". Der Installer
        /// traegt sein eigenes Manifest (requireAdministrator); aus dem nicht erhoehten Host
        /// geht sein Start nur ueber ShellExecute (Verb runas), sonst antwortet Windows mit
        /// Fehler 740 ("erfordert erhoehte Rechte"). Rueckgabe true = der Installer laeuft,
        /// die App beendet sich; false = nichts getauscht, das Update-Flag wird freigegeben.
        /// </summary>
        bool UpdateViaInstaller(string tmp)
        {
            SetupTls();
            UiPost(new { type = "updateStatus", phase = "download" });

            string setup = Path.Combine(tmp, "setup.exe");
            string fehler = DownloadWithProgress(_updateSetup, setup);
            if (fehler != null)
            {
                AppLog.Warn("Installer-Download fehlgeschlagen: " + fehler);
                UiPost(new { type = "updateError", message = fehler });
                return false;
            }

            string pruef = PruefeDatei(setup, _updateSetupHashUrl);
            if (pruef != null) { UiPost(new { type = "updateError", message = pruef }); return false; }

            // Der Helfer bekommt gleich "ende"; bei laufendem Plan waere das ein Baum-Kill.
            // Waehrend des Downloads kann ein Lauf begonnen haben, deshalb hier noch einmal.
            string laeuft = LaufendesWas(true);
            if (laeuft != null) { UpdateWartetAuf(laeuft); return false; }

            UiPost(new { type = "updateStatus", phase = "admin" });
            WriteMarker(_updateTag);
            AppLog.Info("Installer wird still ausgefuehrt.");

            // /VERYSILENT: keine Oberflaeche. /NORESTART: der Installer startet den PC nicht neu.
            // Der Installer wartet, bis diese Instanz beendet ist - deshalb erst starten,
            // dann beenden. Der Helfer ist dieselbe EXE: er muss vorher weg, sonst kann der
            // Installer die Datei nicht ersetzen.
            HelferClient.Beenden();
            bool abgelehnt;
            string start = StarteErhoeht(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL", out abgelehnt);
            if (start != null)
            {
                DeleteMarker();
                if (abgelehnt) { UpdateAbgelehnt("den Installer"); return false; }
                AppLog.Error("Installer ließ sich nicht starten: " + start);
                UiPost(new { type = "updateError", message = start });
                return false;
            }
            UiPost(new { type = "updateStatus", phase = "restart" });
            BeginInvoke((Action)delegate { Application.Exit(); });
            return true;
        }

        /// <summary>
        /// Startet datei erhoeht (Verb runas, UseShellExecute, ohne Fenster). Rueckgabe null =
        /// gestartet; sonst der Grund als Satz, abgelehnt = true bei Win32-Fehler 1223 (der
        /// Nutzer hat im UAC-Dialog "Nein" gesagt). Die einzige Stelle in ShellForm, die noch
        /// einen Prozess startet (Entwurf, Abschnitt 8 Punkt 3: Ausnahme "Update-Batch").
        /// Blockiert, solange der UAC-Dialog offen ist: nur aus einem Hintergrund-Thread rufen.
        /// </summary>
        static string StarteErhoeht(string datei, string argumente, out bool abgelehnt)
        {
            abgelehnt = false;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(datei, argumente ?? "")
                {
                    UseShellExecute = true,      // Pflicht fuer Verb = runas (UAC-Dialog)
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                using (Process p = Process.Start(psi)) { }
                return null;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)   // ERROR_CANCELLED
            {
                abgelehnt = true;
                return UpdateOhneRechte;
            }
            catch (Exception ex)
            {
                return "Windows meldet beim Start „" + ex.Message.TrimEnd('.') + "“.";
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
        /// Arbeitsordner fuer das Update.
        ///
        /// Erhoeht (EnableLUA=0, eingebauter Administrator, erhoehte Shell): wie bis 8.0 unter
        /// ProgramData, nur fuer Administratoren und SYSTEM beschreibbar. Ein nicht erhoehter
        /// Prozess konnte sonst das entpackte Paket oder die Batchdatei zwischen Schreiben und
        /// Ausfuehren austauschen und damit Code als Administrator ausfuehren.
        ///
        /// Nicht erhoeht (Normalfall seit 8.1, Manifest asInvoker): der Host selbst muss den
        /// Download dort ablegen, ein Ordner nur fuer Administratoren ginge also gar nicht,
        /// und unter ProgramData\WindowsWartung haben seit 8.1 ALLE Benutzer Aenderungsrecht
        /// (vererbt), also auch fremde Konten desselben PCs. Deshalb das eigene Profil, mit
        /// Rechten nur fuer das eigene Konto, Administratoren und SYSTEM (siehe
        /// CreateAdminOnlyDirectory). Die Batch laeuft danach erhoeht (Verb runas); gegen einen
        /// Tausch durch einen anderen Prozess DESSELBEN Kontos schuetzt kein Ordner, das ist
        /// die Grenze des Rechte-Modells B und im Entwurf so hingenommen.
        /// </summary>
        static string UpdateWorkDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(IsElevated()
                    ? Environment.SpecialFolder.CommonApplicationData
                    : Environment.SpecialFolder.LocalApplicationData),
                "WindowsWartung", "update");
        }

        /// <summary>
        /// Legt den Update-Ordner mit engen Rechten an; vererbte Rechte werden ausdruecklich
        /// abgeworfen. Erhoeht: nur Administratoren und SYSTEM, Besitzer Administratoren.
        /// Nicht erhoeht: dazu das eigene Konto (es muss schreiben), Besitzer bleibt das
        /// eigene Konto (einen anderen darf ein nicht erhoehter Prozess nicht eintragen,
        /// Windows lehnt das mit Fehler 1307 ab).
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
                if (IsElevated())
                {
                    sec.SetOwner(admins);
                }
                else
                {
                    SecurityIdentifier ich;
                    using (WindowsIdentity id = WindowsIdentity.GetCurrent()) ich = id.User;
                    if (ich == null) throw new InvalidOperationException("Das eigene Konto ist nicht lesbar.");
                    sec.AddAccessRule(new FileSystemAccessRule(ich, FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
                }

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

        /// <summary>
        /// ZIP-Weg: entpacken, Herkunft pruefen, Austausch-Batch erhoeht starten (Verb runas,
        /// UAC-Dialog), dann diese Instanz beenden. Laeuft auf dem Download-Thread; alles
        /// fuer die Oberflaeche geht ueber UiPost. Rueckgabe true = die Batch laeuft, die App
        /// beendet sich; false = nichts getauscht, das Update-Flag wird freigegeben.
        /// </summary>
        bool FinishUpdate(string tmp, string zip)
        {
            try
            {
                string newDir = Path.Combine(tmp, "new");
                if (Directory.Exists(newDir)) Directory.Delete(newDir, true);
                ZipFile.ExtractToDirectory(zip, newDir);

                string neueExe = Path.Combine(newDir, "WindowsWartung.exe");
                if (!File.Exists(neueExe))
                {
                    UiPost(new { type = "updateError", message = "Das Paket enthält keine WindowsWartung.exe; es wurde nichts verändert." });
                    return false;
                }

                // Herkunft pruefen, BEVOR getauscht wird: die neue Fassung muss vom selben
                // Herausgeber stammen wie die laufende. Die Pruefsumme allein sagt darueber
                // nichts, weil sie im selben Release liegt wie die Datei.
                string herkunft = UpdateTrust.PruefeHerausgeber(neueExe);
                if (herkunft != null)
                {
                    UiPost(new { type = "updateError", message = herkunft });
                    return false;
                }

                string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                string appExe = Path.Combine(appDir, "WindowsWartung.exe");
                int pid = Process.GetCurrentProcess().Id;

                // Der Helfer bekommt gleich "ende"; bei laufendem Plan waere das ein Baum-Kill.
                // Waehrend Download und Entpacken kann ein Lauf begonnen haben, deshalb hier
                // noch einmal, bevor Merker und Batch entstehen.
                string laeuft = LaufendesWas(true);
                if (laeuft != null) { UpdateWartetAuf(laeuft); return false; }

                WriteMarker(_updateTag);

                // Die Batchdatei liegt im abgesicherten Update-Ordner, NICHT in %TEMP%:
                // sie wird gleich mit Adminrechten ausgefuehrt.
                //
                // Die neue Fassung startet ueber explorer.exe, nicht ueber "start": die Batch
                // laeuft per runas erhoeht, und "start" vererbt das (asInvoker) an die App. Die
                // liefe dann erhoeht, und ihr Selbststart-Schalter legte die Aufgabe wieder mit
                // Besitzer Administratoren an, also genau die 8.0-Aufgabe, die der nicht erhoehte
                // Host nie mehr loswird. Der Explorer startet Programme mit mittlerer Integritaet.
                string bat = Path.Combine(tmp, "ww_update.cmd");
                string sicherung = Path.Combine(tmp, "vorher");
                string neuStarten = "explorer.exe \"" + appExe + "\"\r\n";
                string content =
                    "@echo off\r\n" +
                    ":w\r\n" +
                    "tasklist /FI \"PID eq " + pid + "\" 2>nul | find \"" + pid + "\" >nul\r\n" +
                    "if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto w )\r\n" +
                    // Nicht nur der Host: Helfer (--helfer) und geplante Wartung (--auto) sind
                    // dieselbe EXE, und solange einer davon laeuft, kann robocopy die Datei nicht
                    // ersetzen (/R:3 scheitert, Rueckrollen, "Update rückgängig gemacht" beim
                    // naechsten Start, obwohl nichts kaputt war; Entwurf Abschnitt 14, B15).
                    // Deshalb bis 120 s warten, bis kein WindowsWartung.exe mehr laeuft; danach
                    // geht es weiter, und robocopy meldet den Fehler selbst.
                    "set /a ww_warte=0\r\n" +
                    ":w2\r\n" +
                    "tasklist /FI \"IMAGENAME eq WindowsWartung.exe\" 2>nul | find /I \"WindowsWartung.exe\" >nul\r\n" +
                    "if errorlevel 1 goto w2ok\r\n" +
                    "set /a ww_warte+=1\r\n" +
                    "if %ww_warte% geq 120 goto w2ok\r\n" +
                    "timeout /t 1 /nobreak >nul\r\n" +
                    "goto w2\r\n" +
                    ":w2ok\r\n" +
                    // Rechte des Laufzeitordners (Entwurf, Abschnitt 14, B34): nach einem
                    // 8.0-Bestand gehoeren Verlauf, Antworten und app.log der Administratoren-
                    // gruppe, und der nicht erhoehte Host kann sie nicht mehr ueberschreiben.
                    // Diese Batch laeuft erhoeht und gibt BUILTIN\Users (S-1-5-32-545, sprach-
                    // unabhaengig) Aenderungsrecht auf den ganzen Baum, wie Ablage.RechteSichern.
                    // Ein Fehler hier (Ordner fehlt) bricht das Update nicht ab.
                    "icacls \"%ProgramData%\\WindowsWartung\" /grant *S-1-5-32-545:(OI)(CI)M /T >nul 2>&1\r\n" +
                    // Erst die bisherige Fassung zur Seite legen. Ohne diesen Schritt gab es
                    // keinen Rueckweg: Bricht das Kopieren mittendrin ab, blieb eine halb
                    // ueberschriebene Installation stehen - neue Oberflaeche, alte Programmdatei
                    // oder umgekehrt. Scheitert schon die Sicherung, wird gar nichts angefasst.
                    "rmdir /s /q \"" + sicherung + "\" >nul 2>&1\r\n" +
                    "robocopy \"" + appDir + "\" \"" + sicherung + "\" /E /NFL /NDL /NJH /NJS /R:1 /W:1 >nul\r\n" +
                    "if errorlevel 8 (\r\n" +
                    "  echo Die bisherige Fassung liess sich nicht sichern. Es wurde nichts veraendert.> \"" + Path.Combine(tmp, "fehler.txt") + "\"\r\n" +
                    "  " + neuStarten +
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
                    neuStarten +
                    ":ende\r\n" +
                    "rmdir /s /q \"" + sicherung + "\" >nul 2>&1\r\n" +
                    "rmdir /s /q \"" + Path.Combine(tmp, "new") + "\" >nul 2>&1\r\n" +
                    "del \"" + Path.Combine(tmp, "update.zip") + "\" >nul 2>&1\r\n" +
                    "del \"%~f0\" >nul 2>&1\r\n";

                // cmd.exe liest Batchdateien in der OEM-Codepage. Mit Encoding.Default (ANSI)
                // wurden Umlaute im Pfad verstuemmelt - bei einem Benutzernamen wie "Müller"
                // schlug das Kopieren fehl und die App startete nach dem Update nicht mehr.
                File.WriteAllText(bat, content, OemEncoding());

                // Die Batch tauscht Programmdateien unter Program Files: seit 8.1 (asInvoker)
                // braucht sie dafuer den UAC-Dialog (Verb runas). Der Helfer ist dieselbe EXE
                // und muss vorher weg, sonst kann robocopy sie nicht ersetzen.
                UiPost(new { type = "updateStatus", phase = "admin" });
                AppLog.Info("Update auf " + _updateTag + " wird angewendet (Austausch-Batch per runas).");
                HelferClient.Beenden();

                bool abgelehnt;
                string start = StarteErhoeht("cmd.exe", "/c \"" + bat + "\"", out abgelehnt);
                if (start != null)
                {
                    DeleteMarker();
                    try { File.Delete(bat); } catch { }
                    if (abgelehnt) { UpdateAbgelehnt("die Austausch-Batch"); return false; }
                    AppLog.Error("Austausch-Batch ließ sich nicht starten: " + start);
                    UiPost(new { type = "updateError", message = start });
                    return false;
                }

                UiPost(new { type = "updateStatus", phase = "restart" });
                BeginInvoke((Action)delegate { Application.Exit(); });
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Update konnte nicht angewendet werden", ex);
                DeleteMarker();
                UiPost(new { type = "updateError", message = ex.Message });
                return false;
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
        // Der Merker wird VOR dem Start von Batch oder Installer geschrieben. Kommt der Start
        // nicht zustande (UAC abgelehnt, Fehler), muss er weg: sonst meldete der naechste
        // Start "Update rueckgaengig gemacht", obwohl nie eines lief.
        void DeleteMarker()
        {
            try { if (File.Exists(MarkerPath())) File.Delete(MarkerPath()); }
            catch (Exception ex) { AppLog.Warn("Update-Merker ließ sich nicht löschen: " + ex.Message); }
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
                    AppLog.Warn("Update auf " + tag + " wurde nicht wirksam, es läuft weiter " + cur + ".");
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
                // Ein Werkzeug = ein Plan mit einem werkzeug-Schritt. Der Helfer baut die
                // Schritte selbst aus der Nummer (src/Schritte.cs); hier gehen nur Nummer und
                // der Schalter fuer den Sicherungspunkt hinueber.
                int id = ToInt(m, "id");
                MaintenanceAction a = Schritte.Aktion(id);
                if (a == null)
                {
                    Abgewiesen("Werkzeug", "Werkzeug Nr. " + id + " gibt es nicht (Katalog hat " + _actions.Count + " Einträge).", "Unbekanntes Werkzeug");
                    return;
                }
                if (a.Special != null)
                {
                    // braucht eine Eingabe, laeuft ueber den eigenen Befehl (netDiag, driverBackup)
                    Abgewiesen(a.Title, "„" + a.Title + "“ braucht eine Eingabe und läuft über seinen eigenen Befehl, nicht über „run“.", "Falscher Befehl");
                    return;
                }
                if (StartAbgelehnt(a.Title)) return;
                bool restore = ToBool(m, "restore");
                ReadPost(m);
                Kern.Plan plan = Kern.Plan.Neu(a.Title).Mit("werkzeug",
                    "id", a.Id.ToString(),
                    "sicherung", restore && a.WantsRestorePoint ? "1" : "0");
                _runner.RunPlan(a.Title, plan);
            }
            else if (type == "runQueue")
            {
                bool restore = ToBool(m, "restore");
                var gewaehlt = new List<MaintenanceAction>();
                object idsObj;
                if (m.TryGetValue("ids", out idsObj) && idsObj is object[])
                {
                    foreach (object o in (object[])idsObj)
                    {
                        int id;
                        try { id = Convert.ToInt32(o); } catch { continue; }
                        MaintenanceAction a = Schritte.Aktion(id);
                        if (a == null || a.Special != null) continue;   // unbekannt oder Sonderaktion, nicht sammelbar
                        gewaehlt.Add(a);
                    }
                }
                if (gewaehlt.Count == 0)
                {
                    Abgewiesen("Warteschlange", "Die Warteschlange enthält 0 ausführbare Werkzeuge.", "Nichts auszuführen");
                    return;
                }
                string titel = "Warteschlange (" + gewaehlt.Count + ")";
                if (StartAbgelehnt(titel)) return;
                ReadPost(m);

                // Ein Sicherungspunkt fuer die GANZE Warteschlange, ganz vorn: nur der ERSTE
                // Schritt, der einen will, bekommt sicherung=1. Vorher legte jede einzelne
                // Aktion einen an; Windows drosselt auf einen je 24 Stunden, also wurden die
                // uebrigen mit einer Warnung uebersprungen - das las sich wie ein Fehler,
                // obwohl alles in Ordnung war.
                Kern.Plan plan = Kern.Plan.Neu(titel);
                bool restoreDone = false;
                foreach (MaintenanceAction a in gewaehlt)
                {
                    bool sicherung = restore && a.WantsRestorePoint && !restoreDone;
                    if (sicherung) restoreDone = true;
                    plan.Mit("werkzeug", "id", a.Id.ToString(), "sicherung", sicherung ? "1" : "0");
                }
                _runner.RunPlan(titel, plan);
            }
            // --- Hauptweg -------------------------------------------------------
            else if (type == "startCheck") StartCheck();
            else if (type == "startDeepCheck") StartDeepCheck();
            else if (type == "startFix") StartFix();
            // Werte, die nur erhoeht lesbar sind, nachmessen (CheckFlow.Ergaenzen, B1).
            else if (type == "ergaenzen") Ergaenzen();
            // Antwort auf eine Frage des Systems ("so lassen" oder "reparieren"), Grundsatz 1.
            else if (type == "antwort") Antwort(Str(m, "id"), Str(m, "wert"));
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
            else if (type == "selfStartSet") SelfStartSet(ToBool(m, "on"), ToBool(m, "umstellen"));
            else if (type == "openStartupFolder") OpenStartupFolder(Str(m, "scope"));
        }

        // ---------- App-Selbststart (Autostart-Ansicht) ----------
        // Bleibt lokal und ohne Rechte (Scheduler.StartTaskSet, Aufgabe mit LeastPrivilege im
        // eigenen Konto). veraltet = die Aufgabe stammt noch aus 8.0 und startet erhoeht; die
        // kann nur der Helfer loeschen (SelfStartUmstellen).
        void SelfStartGet()
        {
            Thread t = new Thread(delegate ()
            {
                UiPost(new { type = "selfstart", on = Scheduler.StartTaskExists(), veraltet = _selfStartVeraltet });
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>
        /// Schalter "Mit dem PC starten". umstellen = Schaltflaeche "Umstellen" am Hinweis zur
        /// 8.0-Aufgabe (Entwurf, Abschnitt 13). Ist die Aufgabe veraltet, geht JEDER Weg ueber
        /// den Helfer: der nicht erhoehte Host kann sie weder loeschen noch ersetzen (Besitzer
        /// Administratoren), ein lokales StartTaskSet antwortete nur mit "Nicht geändert".
        ///
        /// Erhoehter Host (Entwurf, Abschnitt 14, B33): Einschalten legt keine Aufgabe an (sie
        /// gehoerte der Administratorengruppe und liesse sich spaeter nicht mehr abschalten),
        /// Antwort ist selfstart mit hinweis "erhoeht" und dem wahren Stand. Ausschalten geht
        /// auch erhoeht, das Loeschen ist ja gerade das, was der normale Host nicht darf.
        /// </summary>
        void SelfStartSet(bool on, bool umstellen)
        {
            if (umstellen || _selfStartVeraltet) { SelfStartUmstellen(on); return; }
            if (on && IsElevated()) { SelbststartErhoehtAbgelehnt(); return; }
            string exe = Application.ExecutablePath;
            Thread t = new Thread(delegate ()
            {
                bool ok = Scheduler.StartTaskSet(on, exe);
                // Nach dem Umschalten neu lesen: eine neu angelegte Aufgabe ist nie veraltet,
                // eine 8.0-Aufgabe, die sich nicht loeschen liess, bleibt es.
                try { _selfStartVeraltet = Scheduler.StartTaskVeraltet(); }
                catch (Exception ex) { AppLog.Warn("Selbststart-Aufgabe prüfen: " + ex.Message); }
                UiPost(new { type = "selfstart", on = Scheduler.StartTaskExists(), changed = ok, veraltet = _selfStartVeraltet });
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>
        /// Einschalten im erhoehten Host abgelehnt: Grund ins app.log, an die Oberflaeche der
        /// wahre Stand mit hinweis "erhoeht" (changed false, damit der Schalter zurueckspringt).
        /// Nie still: ohne Antwort stuende der Schalter auf "an", obwohl nichts angelegt wurde.
        /// </summary>
        void SelbststartErhoehtAbgelehnt()
        {
            AppLog.Warn(SelbststartErhoehtGrund("Selbststart nicht eingeschaltet"));
            Thread t = new Thread(delegate ()
            {
                UiPost(new { type = "selfstart", on = Scheduler.StartTaskExists(), changed = false, veraltet = _selfStartVeraltet, hinweis = "erhoeht" });
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>
        /// 8.0-Aufgabe (/RL HIGHEST, Besitzer Administratoren) umstellen: Plan selbststart.loeschen
        /// (Stufe 1, keine Parameter) ueber den Runner, der Helfer loescht sie erhoeht. Im
        /// Nachlauf nach Done legt der Host sie bei on lokal neu an (StartTaskSet(true, exe):
        /// Besitzer Nutzer, LeastPrivilege); bei kind != good bleibt sie veraltet, und die
        /// Oberflaeche erfaehrt das ueber selfstart {veraltet:true}. Braucht einmal den UAC-Dialog.
        ///
        /// Erhoehter Host (Entwurf, Abschnitt 14, B33): der Plan laeuft lokal ohne Dialog und
        /// loescht die 8.0-Aufgabe, neu angelegt wird NICHTS (sie gehoerte wieder der
        /// Administratorengruppe). Der Selbststart ist danach aus; die Oberflaeche bekommt
        /// hinweis "erhoeht", damit sie sagen kann: normal starten und dort wieder einschalten.
        /// Das ist der Ausweg aus der Schleife "8.0-Aufgabe startet 8.1 bei jeder Anmeldung erhöht".
        /// </summary>
        void SelfStartUmstellen(bool on)
        {
            const string titel = "Selbststart umstellen";
            if (StartAbgelehnt(titel))
            {
                // Der Schalter der Oberflaeche steht schon um: den wahren Stand nachschicken.
                SelfStartGet();
                return;
            }
            string exe = Application.ExecutablePath;
            bool erhoeht = IsElevated();
            Kern.Plan plan = Kern.Plan.Neu(titel).Mit("selbststart.loeschen");
            _nachLauf = delegate (LogKind k)
            {
                bool gut = k == LogKind.Good;
                Thread t = new Thread(delegate ()
                {
                    bool angelegt = false;
                    if (gut && on && erhoeht)
                        AppLog.Warn(SelbststartErhoehtGrund("Selbststart-Aufgabe aus 8.0 gelöscht, aber keine neue angelegt"));
                    else if (gut && on)
                    {
                        angelegt = Scheduler.StartTaskSet(true, exe);
                        if (angelegt) AppLog.Info("Selbststart-Aufgabe ohne Rechte neu angelegt (Besitzer Nutzer).");
                    }
                    else if (gut) AppLog.Info("Selbststart-Aufgabe aus 8.0 gelöscht, keine neue angelegt.");
                    if (gut) _selfStartVeraltet = false;
                    else
                    {
                        // Nachlesen statt raten: bei einer Ablehnung steht die 8.0-Aufgabe noch.
                        try { _selfStartVeraltet = Scheduler.StartTaskVeraltet(); }
                        catch (Exception ex) { AppLog.Warn("Selbststart-Aufgabe prüfen: " + ex.Message); _selfStartVeraltet = true; }
                    }
                    // changed = der Helfer hat geloescht; on sagt der Oberflaeche, was jetzt gilt
                    // (scheitert das Neuanlegen, steht der Schalter ehrlich auf "aus").
                    if (gut && on && !angelegt && !erhoeht) AppLog.Warn("Selbststart-Aufgabe gelöscht, aber nicht neu angelegt: der Selbststart ist jetzt aus.");
                    if (erhoeht)
                        UiPost(new { type = "selfstart", on = Scheduler.StartTaskExists(), changed = gut, veraltet = _selfStartVeraltet, hinweis = "erhoeht" });
                    else
                        UiPost(new { type = "selfstart", on = Scheduler.StartTaskExists(), changed = gut, veraltet = _selfStartVeraltet });
                });
                t.IsBackground = true;
                t.Start();
            };
            _runner.RunPlan(titel, plan);
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
        // "Lauf beendet" schreibt seit 8.1 der Runner selbst im Hintergrund-Thread (Entwurf,
        // Abschnitt 14, B19), der Verlaufseintrag entsteht hier. Beim Schliessen des Fensters
        // waere diese Zustellung verloren gegangen (ein wartendes BeginInvoke wird beim
        // Zerstoeren des Fensters verworfen); deshalb vertagt OnClosingWhileBusy das Schliessen,
        // bis Done gelaufen ist, und Done schliesst dann selbst (_schliessenNachDone).
        void Done(string title, LogKind k, string message, double seconds)
        {
            History.Add(title, KindStr(k), message, seconds);
            Post(new { type = "done", title = title, kind = KindStr(k), message = message });

            // Einmaliger Nachlauf (Zeitplan-Stand melden, Selbststart neu anlegen, Liste der
            // Nebenansicht neu laden), vor dem nachgeholten Wartungslauf: der wuerde sonst den
            // Stand erst nach Minuten liefern.
            Action<LogKind> nachlauf = _nachLauf;
            _nachLauf = null;
            if (nachlauf != null)
            {
                try { nachlauf(k); }
                catch (Exception ex) { AppLog.Warn("Nachlauf nach „" + title + "“: " + ex.Message); }
            }

            // Der erste UAC-Dialog eines Werkzeugs bringt den Helfer: die Startzeile soll das wissen.
            AdminNachmelden();
            Notify(title, message, k);

            // Das Fenster wartet nur noch auf diesen Abschluss: jetzt schliessen, kein Nachlauf,
            // kein Countdown (ein abgebrochener Lauf ist ohnehin "bad").
            if (_schliessenNachDone)
            {
                AppLog.Info("Abschluss von „" + title + "“ zugestellt, das Fenster wird jetzt geschlossen.");
                _pendingPost = "none";
                _autoRunPending = false;
                try { BeginInvoke((Action)Close); }
                catch (Exception ex) { AppLog.Warn("Vertagtes Schließen: " + ex.Message); _schliessenNachDone = false; }
                return;
            }

            // Steht noch ein nachgeholter Wartungslauf an, darf JETZT kein Abschalt-Countdown
            // starten: sonst faehrt der PC mitten in DISM oder SFC herunter und beschaedigt genau
            // das, was das Werkzeug reparieren soll. Der Wunsch bleibt gemerkt und greift nach
            // dem letzten Lauf (Done des nachgeholten Laufs). Wird die Wartung dagegen
            // uebersprungen (kein Helfer mehr verbunden), gibt es kein weiteres Done: dann
            // gilt der Wunsch jetzt, wie ohne Vormerkung.
            if (_autoRunPending)
            {
                NachlaufPruefen();
                bool nachgeholt = _autoRunPending || (_runner != null && _runner.Running);
                if (nachgeholt)
                {
                    if (_pendingPost != "none")
                        Log("●  Der Abschalt-Countdown startet erst nach der geplanten Wartung.", LogKind.Warn);
                    return;
                }
            }

            if (k != LogKind.Bad && _pendingPost != "none") ScheduleShutdown();
            _pendingPost = "none";
        }

        // ---------- Geplante Wartung, an die offene App uebergeben (--auto -> WM_WW_RUNAUTO) ----------
        bool _autoRunPending;

        /// <summary>
        /// Am Ende JEDES Wegs (Done des Runners hier; Hauptweg und Suchlaeufe rufen es per
        /// BeginInvoke aus CheckFlow und ScanFlow): ein geplanter Wartungslauf, der auf das
        /// Ende der laufenden Aktion gewartet hat (_autoRunPending), startet jetzt. Laeuft
        /// noch etwas anderes (Runner zu Ende, Suchlauf noch nicht), bleibt er vorgemerkt,
        /// das Ende des anderen Wegs ruft erneut. Dazu der Helfer-Stand fuer die Startzeile.
        /// UI-Thread; darf beliebig oft gerufen werden, ohne Vormerkung tut er nichts.
        ///
        /// Gestartet wird nur, wenn ein Helfer verbunden ist (Entwurf, Abschnitt 14, B10/B55):
        /// der Helfer kann waehrend einer lesenden Pruefung oder eines Suchlaufs nach 10 Minuten
        /// Leerlauf gegangen sein. Ohne ihn zeigte der Nachholweg einen UAC-Dialog, den niemand
        /// bestellt hat, und bei "Nein" hing der Runner im Dialog. Der --auto-Prozess ist zu
        /// dem Zeitpunkt schon weg (er hat "übergeben" gemeldet): der Termin faellt aus, und
        /// das steht im Verlauf, damit es nicht unbemerkt bleibt.
        /// </summary>
        internal void NachlaufPruefen()
        {
            AdminNachmelden();
            if (!_autoRunPending || _runner == null) return;
            if (EtwasLaeuft)
            {
                AppLog.Info("Geplante Wartung wartet weiter: es läuft noch " + (LaufendesWas() ?? "etwas") + ".");
                return;
            }
            _autoRunPending = false;
            if (!HelferVerbunden())
            {
                const string grund = "Übersprungen: kein Helfer verbunden";
                AppLog.Warn("Geplante Wartung nicht nachgeholt: kein Helfer verbunden (nach 10 Minuten Leerlauf beendet); der Termin fällt aus, der nächste läuft wie geplant.");
                History.Add("Geplante Wartung", "warn", grund, 0);
                Log("●  Die vorgemerkte geplante Wartung wurde übersprungen: es ist kein Helfer mehr verbunden, und ein UAC-Dialog ohne Ihren Klick kommt nicht in Frage. Der nächste Termin läuft wie geplant.", LogKind.Warn);
                Notify("Geplante Wartung", grund, LogKind.Warn);
                return;
            }
            RunScheduledJobsNow();
        }

        /// <summary>HelferClient.Verbunden, ohne zu werfen (ein Lesefehler zaehlt als "nicht verbunden" und steht im app.log).</summary>
        static bool HelferVerbunden()
        {
            try { return HelferClient.Verbunden; }
            catch (Exception ex) { AppLog.Warn("Helfer-Stand nicht lesbar: " + ex.Message); return false; }
        }

        // Rueckgabe true = Lauf uebernommen (gestartet oder fuer direkt danach vorgemerkt).
        // false laesst den --auto-Prozess selbst laufen (still, ohne Fenster) oder, wenn er
        // nicht erhoeht ist, mit seinem eigenen Verlaufseintrag "Übersprungen" enden.
        // erhoeht = wParam der Nachricht (1 = der --auto-Prozess laeuft erhoeht).
        bool OnScheduledRunRequested(bool erhoeht)
        {
            if (_runner == null) return false;
            // Von Hand aus einer normalen Eingabeaufforderung gestartet: ohne Erhoehung laeuft
            // die Wartung nirgends, auch nicht hier (der Nachholweg zeigte sonst irgendwann
            // einen UAC-Dialog, den niemand bestellt hat).
            if (!erhoeht)
            {
                AppLog.Warn("Geplante Wartung nicht übernommen: der --auto-Aufruf war nicht erhöht.");
                Log("●  Der Aufruf der geplanten Wartung war nicht erhöht; sie läuft nicht.", LogKind.Warn);
                return false;
            }
            // Seit 8.1 laeuft die Oberflaeche ohne Rechte. Uebernehmen darf sie den Lauf nur,
            // wenn schon ein Ausfuehrer da ist (erhoeht oder lebende Helfer-Pipe): sonst
            // muesste sie den UAC-Dialog zeigen, jetzt oder am Ende der laufenden Aktion,
            // waehrend der --auto-Prozess bereits erhoeht wartet. Deshalb zuerst der Helfer
            // (Entwurf, Abschnitt 14, B10/B55), erst dann EtwasLaeuft: ohne Helfer laeuft der
            // --auto-Prozess selbst, still, wie ohne offenes Fenster. Ein Lauf der offenen
            // App ohne Helfer (Pruefung, Suchlauf) kollidiert nicht mit DISM.
            if (!HelferVerbunden())
            {
                AppLog.Info("Geplante Wartung: kein Helfer verbunden, der --auto-Prozess führt sie selbst aus.");
                Log("●  Geplante Wartung läuft im Hintergrund (ohne Fenster).", LogKind.Warn);
                return false;
            }
            // Dann EtwasLaeuft (Hauptweg, Suchlaeufe, Runner; Entwurf, Abschnitt 13): waehrend
            // des Hauptwegs laufen DISM und SFC bereits, ein zweiter Plan auf derselben Pipe
            // bekaeme vom Helfer "beschäftigt". Die Wartung startet direkt danach, das Ende
            // jedes Wegs ruft NachlaufPruefen.
            if (EtwasLaeuft)
            {
                if (!_autoRunPending)
                {
                    _autoRunPending = true;
                    AppLog.Info("Geplante Wartung vorgemerkt: es läuft " + (LaufendesWas() ?? "etwas") + ".");
                    Log("●  Geplante Wartung wartet, bis die laufende Aktion abgeschlossen ist.", LogKind.Warn);
                }
                return true;
            }
            RunScheduledJobsNow();
            return true;
        }

        void RunScheduledJobsNow()
        {
            // Der gewaehlte Aufgaben-Satz aus zeitplan.json; leer = Standardsatz (der Helfer
            // nimmt dann Catalog.AutoSet(null)).
            string[] keys = Scheduler.ReadActions();
            string schluessel = keys == null ? "" : string.Join(";", keys);
            const string titel = "Geplante Wartung";
            Kern.Plan plan = Kern.Plan.Neu(titel).Mit("wartung.auto", "schluessel", schluessel);
            // Ins Protokoll, nicht nur ins Fenster: Bei der Fehlersuche am 22.08.2026 war
            // genau diese Luecke die teuerste. Die geplante Wartung war zehn Minuten lang
            // mit DISM und SFC ueber das System gelaufen, ohne in app.log eine einzige
            // Zeile zu hinterlassen - im Protokoll sah es aus, als sei nichts geschehen.
            AppLog.Info("Geplante Wartung gestartet (Zeitplan, in der offenen App" + (keys == null ? ", Standardsatz" : ", " + keys.Length + " Aufgaben") + ").");
            // Der Lauf kommt ohne Klick: die Oberflaeche richtet den Ablaufbildschirm dafuer her
            // (Titel, ein Schritt, Balken zurueck, "Abbrechen" statt "Zurück"; Entwurf, Abschnitt
            // 14, B29). Ohne diese Nachricht lief der Balken unter dem Titel des vorigen Werkzeugs.
            Post(new { type = "flowStart", mode = "action", total = 1, titel = titel });
            Log("●  Geplante Wartung wird jetzt automatisch ausgeführt (Zeitplan).", LogKind.Warn);
            _runner.RunPlan(titel, plan);
        }

        /// <summary>
        /// Abgewiesen, bevor ein Lauf beginnt (ungültige Eingabe, unbekannte Nummer): eine
        /// Zeile ins Protokoll und „done“ mit kind bad, damit der Ablaufbildschirm nicht mit
        /// „Läuft …“ stehen bleibt (Regel aus 7.3.2: nie still ablehnen). Kein Verlaufseintrag,
        /// es lief nichts.
        /// </summary>
        void Abgewiesen(string titel, string grund, string kurz)
        {
            AppLog.Warn(titel + " nicht gestartet: " + grund);
            Post(new { type = "log", text = "✖  " + grund, kind = "bad" });
            Post(new { type = "done", title = titel, kind = "bad", message = kurz });
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

        // shutdown.exe braucht keine Administratorrechte (das Recht zum Herunterfahren hat
        // jedes angemeldete Konto) und antwortet in Millisekunden. Der Aufruf geht ueber
        // Shell.Run in einem Hintergrund-Thread: ShellForm startet seit 8.1 keinen Prozess
        // mehr selbst, und eine Absage von Windows (Exit 1190: es läuft schon ein Countdown)
        // steht so im Protokoll statt still zu verschwinden.
        //
        // Banner und Merker _countdownAktiv erst NACH der Antwort von shutdown.exe (Entwurf,
        // Abschnitt 14, B18): vorher zeigte das Banner die neue Wartezeit, obwohl Windows den
        // Befehl mit 1190 abgelehnt hatte und der fruehere Countdown weiterlief.
        void ScheduleShutdown()
        {
            string modus = _pendingPost;
            int wartezeit = _pendingDelay;
            string args = (modus == "restart" ? "-r" : "-s") + " -t " + wartezeit;
            string word = modus == "restart" ? "neu gestartet" : "heruntergefahren";
            ShutdownBefehl(args, "Der Countdown zum Herunterfahren wurde von Windows nicht angenommen", delegate (Shell.Result r)
            {
                if (r.Ok)
                {
                    _countdownAktiv = true;
                    AppLog.Info("Abschalt-Countdown gesetzt: " + word + " in " + wartezeit + " s.");
                    UiLog("●  Der PC wird in " + wartezeit + "s " + word + "; Abbrechen über das Banner.", LogKind.Warn);
                    UiPost(new { type = "shutdownScheduled", mode = modus, delay = wartezeit });
                    return true;
                }
                if (r.Started && !r.TimedOut && r.ExitCode == 1190)
                {
                    // Es lief schon ein Countdown (Windows nimmt keinen zweiten an). Der ist nicht
                    // unserer: der eigene wird vor jedem Lauf abgebrochen (StartAbgelehnt). Also
                    // kein Merker, kein neues Banner; der fruehere Countdown laeuft weiter.
                    _countdownAktiv = false;
                    AppLog.Warn("shutdown.exe " + args + ": Exit 1190, es läuft bereits ein Countdown; die Wartezeit von " + wartezeit + " s gilt nicht.");
                    UiLog("✖  Windows meldet: es läuft bereits ein Countdown zum Herunterfahren. Die gewählte Wartezeit von " + wartezeit + "s gilt nicht, der frühere Countdown läuft weiter.", LogKind.Bad);
                    return true;
                }
                return false;
            });
        }

        void CancelShutdown()
        {
            _countdownAktiv = false;
            Log("●  Herunterfahren abgebrochen.", LogKind.Good);
            Post(new { type = "shutdownCancelled" });
            ShutdownBefehl("-a", "Der Abbruch des Countdowns wurde von Windows nicht angenommen", null);
        }

        // auswerten (Hintergrund-Thread, darf null sein): true = Ergebnis ist verarbeitet; false
        // oder kein Rueckruf = die allgemeine Fehlerzeile mit Exit-Code oder Zeitueberschreitung.
        void ShutdownBefehl(string args, string fehlerSatz, Func<Shell.Result, bool> auswerten)
        {
            Thread t = new Thread(delegate ()
            {
                Shell.Result r = Shell.Run("shutdown.exe", args, 15000);
                if (auswerten != null)
                {
                    bool erledigt = false;
                    try { erledigt = auswerten(r); }
                    catch (Exception ex) { AppLog.Warn("shutdown.exe " + args + " auswerten: " + ex.Message); }
                    if (erledigt) return;
                }
                if (r.Ok) return;
                string grund = r.Started ? (r.TimedOut ? "keine Antwort binnen 15 s" : "Exit " + r.ExitCode) : "nicht gestartet";
                AppLog.Warn("shutdown.exe " + args + ": " + grund + " " + (r.Error ?? "").Trim());
                UiLog("✖  " + fehlerSatz + " (" + grund + ").", LogKind.Bad);
            });
            t.IsBackground = true;
            t.Start();
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

        // Log aus einem Hintergrund-Thread: dieselbe Zeile im Fenster UND im Berichtstext
        // (_log, "Bericht speichern"); ein rohes UiPost(log) liesse den Bericht aus.
        void UiLog(string text, LogKind k)
        {
            if (_web != null && _web.IsHandleCreated)
            {
                try { _web.BeginInvoke((Action)delegate { Log(text, k); }); }
                catch { }
            }
        }

        void SaveLog()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "Textdatei (*.txt)|*.txt";
                d.FileName = "wartung-bericht.txt";
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    try { File.WriteAllText(d.FileName, BerichtText(), new UTF8Encoding(true)); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fehler"); }
                }
            }
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

        // Der Punkt entsteht im Helfer ueber WMI (Drossel-Schluessel vorher auf 0, Nachweis
        // ueber die Folgenummer); hier wird nur die Beschreibung vorab geprueft, damit die
        // Oberflaeche eine brauchbare Meldung bekommt. Der Helfer prueft sie erneut.
        void RestoreCreate(string desc)
        {
            const string titel = "Wiederherstellungspunkt anlegen";
            string beschreibung = Schritte.BeschreibungPruefen(desc);
            if (beschreibung == null)
            {
                Abgewiesen(titel, "Die Beschreibung darf höchstens 60 Zeichen haben und nur Buchstaben, Ziffern, Leerzeichen, Punkt, Unterstrich und Bindestrich enthalten.", "Ungültige Beschreibung");
                return;
            }
            if (StartAbgelehnt(titel)) return;
            Kern.Plan plan = Kern.Plan.Neu(titel).Mit("wiederherstellungspunkt.anlegen", "beschreibung", beschreibung);
            // Nach Done die Liste neu laden (Entwurf, Abschnitt 14, B26): der neue Punkt erschien
            // sonst erst nach Verlassen und Neuoeffnen der Ansicht. Auch nach einem Fehlschlag,
            // die Liste zeigt dann ehrlich den alten Stand.
            _nachLauf = delegate (LogKind k) { StartRestoreList(); };
            _runner.RunPlan(titel, plan);
        }

        void RestoreRevert(int seq)
        {
            const string titel = "Windows auf einen früheren Stand zurücksetzen";
            if (seq <= 0)
            {
                // Die Folgenummer ist eine reine Zahl; alles andere kann nur ein Fehler der Oberflaeche sein.
                Abgewiesen(titel, "Die Folgenummer des Wiederherstellungspunkts muss größer als 0 sein, nicht " + seq + ".", "Ungültige Folgenummer");
                return;
            }
            if (StartAbgelehnt(titel)) return;
            // Ein Titel fuer Absage, Plan, Verlauf und Toast: bis 8.1 hiess der Lauf hier
            // "Wiederherstellung", die Rueckfrage aber "zurücksetzen".
            Kern.Plan plan = Kern.Plan.Neu(titel).Mit("wiederherstellungspunkt.zurueck", "folge", seq.ToString());
            _runner.RunPlan(titel, plan);
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

            string title = "Bloatware entfernen (" + fulls.Count + ")";
            if (fulls.Count == 0)
            {
                Abgewiesen("Bloatware entfernen", "0 der übergebenen Pakete steht in der Liste der entfernbaren Apps; es wurde nichts entfernt.", "Nichts entfernt");
                return;
            }
            if (StartAbgelehnt(title)) return;

            // Ein Schritt apps.entfernen mit der Paketliste (';'-getrennt; PackageFullNames
            // enthalten nie ';'). Sicherungspunkt und Entfernen baut der Helfer (Schritte.AppsEntfernen).
            Kern.Plan plan = Kern.Plan.Neu(title).Mit("apps.entfernen",
                "pakete", string.Join(";", fulls.ToArray()),
                "sicherung", restore ? "1" : "0");
            // Nach Done die Liste neu laden (Entwurf, Abschnitt 14, B26): entfernte Apps blieben
            // sonst angehakt in der Liste stehen.
            _nachLauf = delegate (LogKind k) { StartBloatList(); };
            _runner.RunPlan(title, plan);
        }

        // ---------- Netzwerk-Diagnose ----------
        void NetDiag(string target)
        {
            string t = Schritte.HostPruefen(target);
            if (t == null)
            {
                Abgewiesen("Netzwerk-Diagnose",
                    "Ungültiges Ziel: erlaubt sind 1 bis 253 Zeichen aus Buchstaben, Ziffern, Punkt, Doppelpunkt und Bindestrich, kein Bindestrich am Anfang.",
                    "Ungültiges Ziel");
                return;
            }
            string titel = "Netzwerk-Diagnose: " + t;
            if (StartAbgelehnt(titel)) return;
            // ping.exe/tracert.exe startet der Helfer direkt (ohne Shell); hier geht nur das geprüfte Ziel hinüber.
            Kern.Plan plan = Kern.Plan.Neu(titel).Mit("netz.diagnose", "ziel", t);
            _runner.RunPlan(titel, plan);
        }

        // ---------- Treiber-Backup ----------
        void DriverBackup()
        {
            const string titel = "Treiber-Backup";
            string folder;
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Ordner für die Treiber-Sicherung aussuchen";
                d.ShowNewFolderButton = true;
                if (d.ShowDialog(this) != DialogResult.OK) folder = null;
                else folder = d.SelectedPath;
            }
            if (string.IsNullOrEmpty(folder))
            {
                // Die Oberflaeche steht schon auf dem Ablaufbildschirm: ohne Antwort bliebe sie dort.
                AppLog.Info("Treiber-Backup nicht gestartet: kein Ordner gewählt.");
                Post(new { type = "log", text = "●  Kein Ordner gewählt, das Treiber-Backup wurde nicht gestartet.", kind = "warn" });
                Post(new { type = "done", title = titel, kind = "warn", message = "Kein Ordner gewählt" });
                return;
            }
            // Pfad stammt aus dem System-Ordnerdialog; geprueft wird er trotzdem (absolut,
            // nicht unter %WINDIR%, keine Laufwerkswurzel), der Helfer prueft ihn erneut und
            // startet pnputil.exe direkt (ohne Shell).
            string ordner = Schritte.OrdnerPruefen(folder);
            if (ordner == null)
            {
                Abgewiesen(titel, Schritte.IstLaufwerkswurzel(folder)
                    ? "Der Zielordner „" + folder + "“ ist eine Laufwerkswurzel; bitte einen Unterordner wählen."
                    : "Der Zielordner „" + folder + "“ muss ein absoluter Pfad sein und darf nicht im Windows-Ordner liegen.",
                    "Ungültiger Zielordner");
                return;
            }
            if (StartAbgelehnt(titel)) return;
            Kern.Plan plan = Kern.Plan.Neu("Treiber-Backup nach " + ordner).Mit("treiber.sichern", "ordner", ordner);
            _runner.RunPlan(titel, plan);
        }

        // ---------- Geplante Wartung ----------
        // justCreated (nur nach zeitplan.anlegen) und fehler (Eingabe abgewiesen, nichts lief)
        // sind Zusatzfelder; ohne beide ist es die reine Statusabfrage.
        void SendScheduleStatus(bool? justCreated = null, string fehler = null)
        {
            Thread t = new Thread(delegate ()
            {
                bool exists = Scheduler.Exists();
                object cfg = Scheduler.Read();
                if (justCreated.HasValue)
                    UiPost(new { type = "schedule", exists = exists, config = cfg, justCreated = justCreated.Value });
                else if (fehler != null)
                    UiPost(new { type = "schedule", exists = exists, config = cfg, fehler = fehler });
                else
                    UiPost(new { type = "schedule", exists = exists, config = cfg });
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>
        /// Zeitplan anlegen: die Aufgabe braucht /RL HIGHEST (DISM und sfc), also den Helfer.
        /// Die Eingaben werden hier mit derselben Pruefung vorab gefiltert wie im Helfer
        /// (Schritte.ZeitplanPruefen: Whitelist fuer Modus, Wochentage, Monatstag, Uhrzeit,
        /// Aufgaben-Schluessel), damit die Oberflaeche bei einem Fehler sofort einen Grund
        /// bekommt statt eines UAC-Dialogs. Den Stand meldet der Nachlauf nach Done.
        /// </summary>
        void ScheduleCreate(Dictionary<string, object> m)
        {
            const string titel = "Zeitplan anlegen";
            // Nur setzen, was die Oberflaeche geschickt hat: ein fehlendes Feld soll in der
            // Meldung aus ZeitplanPruefen als fehlend erscheinen, nicht als "-1".
            var s = new PlanSchritt { Kennung = "zeitplan.anlegen" };
            if (m.ContainsKey("mode")) s.Parameter["modus"] = Str(m, "mode");
            if (m.ContainsKey("hh")) s.Parameter["stunde"] = ToInt(m, "hh").ToString();
            if (m.ContainsKey("mm")) s.Parameter["minute"] = ToInt(m, "mm").ToString();
            if (m.ContainsKey("dom")) s.Parameter["tag"] = ToInt(m, "dom").ToString();
            if (m.ContainsKey("days")) s.Parameter["tage"] = ListeAusNachricht(m, "days");
            if (m.ContainsKey("actions")) s.Parameter["aktionen"] = ListeAusNachricht(m, "actions");

            // Explizit leer gewaehlte Aufgaben (Feld da, aber 0 Eintraege) waeren "Standardsatz":
            // das verhindert die Oberflaeche, hier zaehlt es wie bisher als ungueltig.
            object av;
            bool aktionenLeer = m.TryGetValue("actions", out av) && av is object[] && ((object[])av).Length == 0;

            string modus, dSpec; string[] tage, aktionen; int dom, hh, mm;
            string grund = Schritte.ZeitplanPruefen(s, out modus, out dSpec, out tage, out dom, out hh, out mm, out aktionen);
            if (grund == null && aktionenLeer) grund = "Es sind 0 Aufgaben gewählt; mindestens 1 muss es sein.";
            if (grund != null)
            {
                AppLog.Warn(titel + " nicht gestartet: " + grund);
                Post(new { type = "log", text = "✖  " + grund, kind = "bad" });
                SendScheduleStatus(null, grund);
                return;
            }
            if (StartAbgelehnt(titel)) return;

            Kern.Plan plan = Kern.Plan.Neu(titel);
            plan.Schritte.Add(s);
            _nachLauf = delegate (LogKind k) { SendScheduleStatus(k == LogKind.Good); };
            _runner.RunPlan(titel, plan);
        }

        void ScheduleDelete()
        {
            const string titel = "Zeitplan löschen";
            if (StartAbgelehnt(titel)) return;
            Kern.Plan plan = Kern.Plan.Neu(titel).Mit("zeitplan.loeschen");
            _nachLauf = delegate (LogKind k) { SendScheduleStatus(); };
            _runner.RunPlan(titel, plan);
        }

        /// <summary>Feld der Oberflaeche (Array aus Texten) als ';'-Liste; leer, wenn es fehlt. Eintraege mit ';' werden verworfen.</summary>
        static string ListeAusNachricht(Dictionary<string, object> m, string k)
        {
            object v;
            if (!m.TryGetValue(k, out v) || !(v is object[])) return "";
            var teile = new List<string>();
            foreach (object o in (object[])v)
            {
                string t = o as string;
                if (string.IsNullOrEmpty(t) || t.IndexOf(';') >= 0) continue;
                teile.Add(t.Trim());
            }
            return string.Join(";", teile.ToArray());
        }

        // ---------- Rahmenloses Fenster: Größe ändern ----------
        protected override void WndProc(ref Message m)
        {
            // Geplanter Wartungslauf, vom --auto-Prozess an diese offene Instanz uebergeben.
            // wParam 1 = der Aufrufer laeuft erhoeht (AutoRunner schickt das seit 8.1 so).
            // Result 1 = uebernommen (Handshake); sonst faellt --auto auf den stillen Lauf zurueck.
            if (Native.WM_WW_RUNAUTO != 0 && m.Msg == (int)Native.WM_WW_RUNAUTO)
            {
                bool erhoeht = m.WParam.ToInt64() == 1;
                m.Result = OnScheduledRunRequested(erhoeht) ? (IntPtr)1 : IntPtr.Zero;
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
