using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
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

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        public ShellForm(string shotPath, string view)
        {
            _shotPath = shotPath;
            _view = view ?? "";

            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(13, 15, 20);
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(940, 600);
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
        }

        async void OnLoad(object sender, EventArgs e)
        {
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }

            try
            {
                string udf = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindowsWartung", "WebView2");
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
            _runner = new CommandRunner(_web, Log, SetState, Done);

            if (_shotPath != null)
                core.NavigationCompleted += OnNavForShot;

            string suffix = "";
            if (_view == "seed") suffix = "?seed=1";
            else if (_view == "toast") suffix = "#toast";

            _web.Source = new Uri("https://app/index.html" + suffix);
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
                MaintenanceAction a = _actions[id];
                List<Step> steps = new List<Step>();
                if (ToBool(m, "restore") && a.IsRepair) steps.Add(RestoreStep());
                steps.AddRange(a.Steps);
                _runner.Run(a.Title, steps);
            }
            else if (type == "cancel") { if (_runner != null) _runner.Cancel(); }
            else if (type == "save") SaveLog();
            else if (type == "win") Win(Str(m, "action"));
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

        // ---------- Runner-Callbacks -> ans UI ----------
        void Log(string text, LogKind k)
        {
            _log.AppendLine(text);
            Post(new { type = "log", text = text, kind = KindStr(k) });
        }
        void SetState(bool running) { Post(new { type = "state", running = running }); }
        void Done(string title, LogKind k, string message) { Post(new { type = "done", title = title, kind = KindStr(k), message = message }); }

        void Post(object o)
        {
            try { _web.CoreWebView2.PostWebMessageAsString(_js.Serialize(o)); }
            catch { }
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
