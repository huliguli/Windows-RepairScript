using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
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
    public class ShellForm : Form
    {
        WebView2 _web;
        CommandRunner _runner;
        readonly List<MaintenanceAction> _actions = Catalog.All();
        readonly StringBuilder _log = new StringBuilder();
        readonly JavaScriptSerializer _js = new JavaScriptSerializer();

        readonly string _shotPath;
        readonly string _view;

        string _pendingPost = "none";
        int _pendingDelay = 60;

        const string Repo = "huliguli/Windows-RepairScript";
        string _updateUrl;
        string _updateTag;
        string _updateAsset;
        string _updateHashUrl;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FILETIME64 idle, out FILETIME64 kernel, out FILETIME64 user);
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx([In, Out] MemStatusEx buffer);

        [StructLayout(LayoutKind.Sequential)]
        struct FILETIME64 { public uint Low; public uint High; }
        static ulong FT(FILETIME64 f) { return ((ulong)f.High << 32) | f.Low; }

        [StructLayout(LayoutKind.Sequential)]
        class MemStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        System.Windows.Forms.Timer _stats;
        ulong _lastIdle, _lastKernel, _lastUser;
        string _osInfo, _modelInfo;

        NotifyIcon _tray;
        bool _notifyEnabled = true;

        public ShellForm(string shotPath, string view)
        {
            _shotPath = shotPath;
            _view = view ?? "";

            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(13, 15, 20);
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(700, 500);
            if (_view == "small") ClientSize = new Size(780, 540); // Test: kleines Fenster -> Scrollen
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Windows-Wartung";
            DoubleBuffered = true;

            _web = new WebView2();
            _web.Dock = DockStyle.Fill;
            _web.DefaultBackgroundColor = Color.FromArgb(13, 15, 20);
            Controls.Add(_web);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            Load += OnLoad;
            Shown += delegate { ForceForeground(); };
            FormClosed += delegate { try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { } };
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

        // ---------- Dashboard: Live-Systemzustand ----------
        void SetDashboard(bool active)
        {
            if (active)
            {
                if (_stats == null)
                {
                    _stats = new System.Windows.Forms.Timer();
                    _stats.Interval = 1500;
                    _stats.Tick += StatsTick;
                }
                if (_osInfo == null) GetStaticInfo();
                FILETIME64 i, k, u;
                if (GetSystemTimes(out i, out k, out u)) { _lastIdle = FT(i); _lastKernel = FT(k); _lastUser = FT(u); }
                StatsTick(null, null);
                _stats.Start();
            }
            else if (_stats != null) _stats.Stop();
        }

        void StatsTick(object sender, EventArgs e)
        {
            try
            {
                int cpu = 0;
                FILETIME64 fi, fk, fu;
                if (GetSystemTimes(out fi, out fk, out fu))
                {
                    ulong i = FT(fi), k = FT(fk), u = FT(fu);
                    ulong di = i - _lastIdle, dk = k - _lastKernel, du = u - _lastUser;
                    _lastIdle = i; _lastKernel = k; _lastUser = u;
                    ulong total = dk + du; // Kernel-Zeit enthaelt Idle
                    if (total > 0) cpu = (int)((100UL * (total - di)) / total);
                    if (cpu < 0) cpu = 0; if (cpu > 100) cpu = 100;
                }

                int ram = 0; double ramUsedGB = 0, ramTotalGB = 0;
                MemStatusEx ms = new MemStatusEx();
                ms.dwLength = (uint)Marshal.SizeOf(typeof(MemStatusEx));
                if (GlobalMemoryStatusEx(ms))
                {
                    ram = (int)ms.dwMemoryLoad;
                    ramTotalGB = ms.ullTotalPhys / 1073741824.0;
                    ramUsedGB = (ms.ullTotalPhys - ms.ullAvailPhys) / 1073741824.0;
                }

                int disk = 0; double diskFreeGB = 0, diskTotalGB = 0;
                try
                {
                    DriveInfo d = new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)));
                    if (d.IsReady && d.TotalSize > 0)
                    {
                        diskTotalGB = d.TotalSize / 1073741824.0;
                        diskFreeGB = d.TotalFreeSpace / 1073741824.0;
                        disk = (int)Math.Round(100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize);
                    }
                }
                catch { }

                // Alle fest verbauten Laufwerke
                List<object> drives = new List<object>();
                try
                {
                    foreach (DriveInfo dr in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (dr.DriveType != DriveType.Fixed || !dr.IsReady || dr.TotalSize <= 0) continue;
                            string lbl = "";
                            try { lbl = dr.VolumeLabel ?? ""; } catch { }
                            drives.Add(new
                            {
                                name = dr.Name.TrimEnd('\\'),
                                label = lbl,
                                freeGB = Math.Round(dr.TotalFreeSpace / 1073741824.0, 1),
                                totalGB = Math.Round(dr.TotalSize / 1073741824.0, 1),
                                used = (int)Math.Round(100.0 * (dr.TotalSize - dr.TotalFreeSpace) / dr.TotalSize)
                            });
                        }
                        catch { }
                    }
                }
                catch { }

                ulong upMs = GetTickCount64();
                double upDays = upMs / 86400000.0;

                int score = 100;
                List<object> recs = new List<object>();
                double diskFreePct = diskTotalGB > 0 ? 100.0 * diskFreeGB / diskTotalGB : 100;
                if (diskFreePct < 10) { score -= 30; recs.Add(new { text = "Festplatte fast voll (" + disk + "% belegt) - Aufraeumen schafft Platz.", action = 11 }); }
                else if (diskFreePct < 20) { score -= 12; recs.Add(new { text = "Festplatte recht voll - Aufraeumen kann helfen.", action = 11 }); }
                if (ram > 90) { score -= 12; recs.Add(new { text = "Arbeitsspeicher stark ausgelastet - evtl. Programme schliessen.", action = -1 }); }
                if (upDays >= 7) { score -= 10; recs.Add(new { text = "Seit " + (int)upDays + " Tagen kein Neustart - ein Neustart tut dem PC gut.", action = -1 }); }
                else if (upDays >= 3) { score -= 4; }
                if (score < 0) score = 0;
                if (recs.Count == 0) recs.Add(new { text = "Alles im gruenen Bereich - aktuell ist nichts noetig.", action = -1 });

                Post(new
                {
                    type = "stats",
                    cpu = cpu,
                    ram = ram,
                    disk = disk,
                    ramUsedGB = Math.Round(ramUsedGB, 1),
                    ramTotalGB = Math.Round(ramTotalGB, 1),
                    diskFreeGB = Math.Round(diskFreeGB, 1),
                    diskTotalGB = Math.Round(diskTotalGB, 1),
                    uptime = FormatUptime(upMs),
                    os = _osInfo,
                    model = _modelInfo,
                    drives = drives,
                    score = score,
                    recs = recs
                });
            }
            catch { }
        }

        static string FormatUptime(ulong ms)
        {
            long sec = (long)(ms / 1000);
            long d = sec / 86400, h = (sec % 86400) / 3600, mi = (sec % 3600) / 60;
            if (d > 0) return d + " Tage " + h + " Std";
            if (h > 0) return h + " Std " + mi + " Min";
            return mi + " Min";
        }

        void GetStaticInfo()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string prod = k.GetValue("ProductName", "Windows") as string;
                    string disp = (k.GetValue("DisplayVersion", null) as string) ?? (k.GetValue("ReleaseId", "") as string);
                    int build; int.TryParse(k.GetValue("CurrentBuildNumber", "") as string, out build);
                    string winName = build >= 22000 ? "Windows 11" : "Windows 10";
                    string edition = (prod ?? "").Replace("Windows 10", "").Replace("Windows 11", "").Trim();
                    _osInfo = winName + (edition.Length > 0 ? " " + edition : "")
                              + " (" + (string.IsNullOrEmpty(disp) ? ("Build " + build) : disp) + ")";
                }
            }
            catch { _osInfo = "Windows"; }
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
                {
                    string man = k.GetValue("SystemManufacturer", "") as string;
                    string mod = k.GetValue("SystemProductName", "") as string;
                    _modelInfo = ((man ?? "") + " " + (mod ?? "")).Trim();
                    if (string.IsNullOrEmpty(_modelInfo)) _modelInfo = "-";
                }
            }
            catch { _modelInfo = "-"; }
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
            try { _web.ZoomFactor = _view == "zoombig" ? 1.5 : ((_shotPath != null) ? 1.0 : ReadZoom()); } catch { }

            if (_shotPath != null)
                core.NavigationCompleted += OnNavForShot;

            string suffix = "";
            if (_view == "seed") suffix = "?seed=1";
            else if (_view == "toast") suffix = "#toast";
            else if (_view == "queue") suffix = "#queue";
            else if (_view == "shutdown") suffix = "#shutdown";
            else if (_view == "update") suffix = "#updatebar";
            else if (_view == "updateprompt") suffix = "#updateprompt";
            else if (_view == "updating") suffix = "#updating";
            else if (_view == "updated") suffix = "#updated";
            else if (_view == "info") suffix = "#info";
            else if (_view == "autostart") suffix = "#autostart";
            else if (_view == "settings") suffix = "#settings";
            else if (_view == "rep") suffix = "#rep";
            else if (_view == "history") suffix = "#history";
            else if (_view == "restore") suffix = "#restore";
            else if (_view == "power") suffix = "#power";
            else if (_view == "netdiag") suffix = "#netdiag";
            else if (_view == "bloat") suffix = "#bloat";
            else if (_view == "sched") suffix = "#sched";
            else if (_view == "progress") suffix = "#progress";
            else if (_view == "dashseed") suffix = "#dashseed";

            _web.Source = new Uri("https://app/index.html" + suffix);
            // Update-Prüfung startet erst, wenn das UI 'ready' meldet (siehe OnReady)
        }

        void OnReady()
        {
            if (_shotPath != null) return;
            try { Post(new { type = "zoom", factor = _web.ZoomFactor }); } catch { }
            try { Post(new { type = "admin", on = IsElevated() }); } catch { }
            CheckUpdatedMarker();   // nach einem Update: Erfolgsmeldung zeigen
            StartUpdateCheck();     // auf neue Version prüfen
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
                    try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

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

                    // ZIP-Asset fuer das In-App-Update suchen
                    object assetsObj;
                    if (data.TryGetValue("assets", out assetsObj) && assetsObj is object[])
                    {
                        foreach (object ao in (object[])assetsObj)
                        {
                            Dictionary<string, object> ad = ao as Dictionary<string, object>;
                            if (ad == null) continue;
                            string au = ad.ContainsKey("browser_download_url") ? Convert.ToString(ad["browser_download_url"]) : "";
                            if (au.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                _updateAsset = au;
                            else if (au.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                                _updateHashUrl = au;
                        }
                    }

                    Version latest = ParseVer(tag);
                    Version cur = typeof(ShellForm).Assembly.GetName().Version;
                    if (latest == null || latest <= cur) return;

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
            try { Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true }); }
            catch { }
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
                string tmp = Path.Combine(Path.GetTempPath(), "WindowsWartung_update");
                try
                {
                    try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                    Directory.CreateDirectory(tmp);
                    string zip = Path.Combine(tmp, "update.zip");

                    try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                    UiPost(new { type = "updateStatus", phase = "download" });

                    // Echter Download laeuft parallel zum Fortschrittsbalken
                    Exception dlError = null;
                    Thread dl = new Thread(delegate ()
                    {
                        try
                        {
                            using (WebClient wc = new WebClient())
                            {
                                wc.Headers.Add("User-Agent", "WindowsWartung-Updater");
                                wc.DownloadFile(new Uri(_updateAsset), zip);
                            }
                        }
                        catch (Exception ex) { dlError = ex; }
                    });
                    dl.IsBackground = true;
                    dl.Start();

                    // Gemaechlicher Fortschritt fuer echtes Update-Gefuehl (~3,5 s)
                    int steps = 70;
                    for (int i = 0; i <= steps; i++)
                    {
                        UiPost(new { type = "updateProgress", percent = (int)(i * 100.0 / steps) });
                        Thread.Sleep(50);
                    }

                    dl.Join(); // auf das echte Ende warten (langsame Leitung)
                    if (dlError != null || !File.Exists(zip))
                    {
                        UiPost(new { type = "updateError", message = dlError != null ? dlError.Message : "Download fehlgeschlagen." });
                        return;
                    }

                    // Sicherheit: heruntergeladene Datei gegen die im Release hinterlegte SHA-256 prüfen
                    if (!string.IsNullOrEmpty(_updateHashUrl))
                    {
                        string expected = "";
                        try
                        {
                            using (WebClient hw = new WebClient())
                            {
                                hw.Headers.Add("User-Agent", "WindowsWartung-Updater");
                                expected = hw.DownloadString(_updateHashUrl).Trim();
                            }
                        }
                        catch { }
                        if (!string.IsNullOrEmpty(expected) &&
                            !string.Equals(expected, Sha256File(zip), StringComparison.OrdinalIgnoreCase))
                        {
                            UiPost(new { type = "updateError", message = "Prüfsumme stimmt nicht überein – Update aus Sicherheitsgründen abgebrochen." });
                            return;
                        }
                    }

                    UiPost(new { type = "updateStatus", phase = "extract" });
                    Thread.Sleep(600);

                    if (_web != null && _web.IsHandleCreated)
                        _web.BeginInvoke((Action)delegate { FinishUpdate(tmp, zip); });
                }
                catch (Exception ex) { UiPost(new { type = "updateError", message = ex.Message }); }
            });
            t.IsBackground = true;
            t.Start();
        }

        void FinishUpdate(string tmp, string zip)
        {
            try
            {
                string newDir = Path.Combine(tmp, "new");
                if (Directory.Exists(newDir)) Directory.Delete(newDir, true);
                ZipFile.ExtractToDirectory(zip, newDir);

                if (!File.Exists(Path.Combine(newDir, "WindowsWartung.exe")))
                {
                    Post(new { type = "updateError", message = "Paket enthält keine WindowsWartung.exe." });
                    return;
                }

                string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                string appExe = Path.Combine(appDir, "WindowsWartung.exe");
                int pid = Process.GetCurrentProcess().Id;

                WriteMarker(_updateTag);

                string bat = Path.Combine(Path.GetTempPath(), "ww_update.cmd");
                string content =
                    "@echo off\r\n" +
                    ":w\r\n" +
                    "tasklist /FI \"PID eq " + pid + "\" 2>nul | find \"" + pid + "\" >nul\r\n" +
                    "if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto w )\r\n" +
                    "robocopy \"" + newDir + "\" \"" + appDir + "\" /E /NFL /NDL /NJH /NJS /R:3 /W:2 >nul\r\n" +
                    "start \"\" \"" + appExe + "\"\r\n" +
                    "rmdir /s /q \"" + tmp + "\" >nul 2>&1\r\n" +
                    "del \"%~f0\" >nul 2>&1\r\n";
                File.WriteAllText(bat, content, Encoding.Default);

                Post(new { type = "updateStatus", phase = "restart" });

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);

                BeginInvoke((Action)delegate { Application.Exit(); });
            }
            catch (Exception ex) { Post(new { type = "updateError", message = ex.Message }); }
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
                if (target != null && cur >= target && _web != null && _web.IsHandleCreated)
                {
                    string ftag = tag;
                    try { _web.BeginInvoke((Action)delegate { Post(new { type = "updated", version = ftag }); }); }
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
            await Task.Delay(950);
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
                    foreach (object o in (object[])idsObj)
                    {
                        int id;
                        try { id = Convert.ToInt32(o); } catch { continue; }
                        if (id < 0 || id >= _actions.Count) continue;
                        MaintenanceAction a = _actions[id];
                        jobs.Add(new Job { Title = a.Title, Steps = BuildSteps(a, restore) });
                    }
                }
                if (jobs.Count == 0) return;
                _runner.RunJobs("Warteschlange (" + jobs.Count + ")", jobs);
            }
            else if (type == "cancel") { if (_runner != null) _runner.Cancel(); }
            else if (type == "cancelShutdown") CancelShutdown();
            else if (type == "ready") OnReady();
            else if (type == "dashboard") SetDashboard(ToBool(m, "active"));
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
            else if (type == "autostartSet") Autostart.SetEnabled(Str(m, "loc"), Str(m, "key"), ToBool(m, "enable"));
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
            History.Add(title, KindStr(k), message, seconds);
            Post(new { type = "done", title = title, kind = KindStr(k), message = message });
            if (k != LogKind.Bad && _pendingPost != "none") ScheduleShutdown();
            _pendingPost = "none";
            Notify(title, message, k);
        }

        List<Step> BuildSteps(MaintenanceAction a, bool restore)
        {
            List<Step> steps = new List<Step>();
            if (restore && a.IsRepair) steps.Add(RestoreStep());
            steps.AddRange(a.Steps);
            return steps;
        }

        void ReadPost(Dictionary<string, object> m)
        {
            _pendingPost = Str(m, "post");
            if (_pendingPost != "shutdown" && _pendingPost != "restart") _pendingPost = "none";
            int d = ToInt(m, "delay");
            _pendingDelay = (d >= 5 && d <= 86400) ? d : 60;
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
                Args = "-NoProfile -ExecutionPolicy Bypass -Command \"try { Checkpoint-Computer -Description 'Wartungstool' -RestorePointType MODIFY_SETTINGS -EA Stop; 'Wiederherstellungspunkt erstellt.' } catch { 'Wiederherstellungspunkt uebersprungen: ' + $_.Exception.Message }\""
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
            jobs.Add(new Job { Title = "Auf Wiederherstellungspunkt zuruecksetzen", Steps = new List<Step> { s } });
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
                "} catch { 'Wiederherstellungspunkt uebersprungen (Systemschutz aktiv?): ' + $_.Exception.Message }";
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
                d.Description = "Zielordner fuer das Treiber-Backup waehlen";
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
            string day = Str(m, "day");
            int hh = ToInt(m, "hh");
            int mi = ToInt(m, "mm");

            if (mode != "daily" && mode != "weekly") return;
            string[] days = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
            if (mode == "weekly" && Array.IndexOf(days, day) < 0) return;
            if (hh < 0 || hh > 23 || mi < 0 || mi > 59) return;

            string hhs = hh.ToString("00");
            string mms = mi.ToString("00");
            string exe = Application.ExecutablePath;

            Thread t = new Thread(delegate ()
            {
                bool ok = Scheduler.Create(mode, day, hhs, mms, exe);
                if (ok) Scheduler.Write(mode, day, hhs + ":" + mms);
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
