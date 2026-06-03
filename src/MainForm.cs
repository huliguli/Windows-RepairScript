using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WartungsToolbox
{
    public class MainForm : Form
    {
        readonly List<MaintenanceAction> _actions = Catalog.All();
        readonly List<NavButton> _navs = new List<NavButton>();
        readonly List<ToastForm> _toasts = new List<ToastForm>();
        string _activeCat;

        FlowLayoutPanel _cards;
        RichTextBox _out;
        Label _catTitle, _catHint, _status;
        CheckBox _restoreChk;
        Button _stopBtn;

        Panel _consolePanel, _collapsedBar;
        Splitter _split;
        bool _consoleVisible = true;

        Font _monoReg, _monoBold;
        CommandRunner _runner;

        System.Windows.Forms.Timer _spin;
        int _spinPhase;
        static readonly string[] SpinFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

        public MainForm()
        {
            _monoReg = new Font(Theme.Mono, FontStyle.Regular);
            _monoBold = new Font(Theme.Mono, FontStyle.Bold);

            Text = "Windows-Wartung";
            BackColor = Theme.Bg0;
            ForeColor = Theme.Text;
            Font = Theme.UI;
            ClientSize = new Size(1060, 720);
            MinimumSize = new Size(940, 640);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = Theme.Bg0;

            BuildRight(body);
            BuildSidebar(body);

            Controls.Add(body);
            Controls.Add(BuildHeader());

            _runner = new CommandRunner(this, Append, SetRunning, OnComplete);

            _spin = new System.Windows.Forms.Timer();
            _spin.Interval = 90;
            _spin.Tick += delegate
            {
                _spinPhase = (_spinPhase + 1) % SpinFrames.Length;
                _status.Text = SpinFrames[_spinPhase] + "  läuft …";
            };

            Load += delegate
            {
                Native.UseDarkTitleBar(Handle);
                try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch { }
                Welcome();
                SelectCategory(Catalog.Categories[0]);
            };
        }

        // ---------------- Kopfzeile ----------------
        Control BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 68;
            header.BackColor = Theme.Bg1;
            header.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            Label title = new Label();
            title.Text = "Windows-Wartung";
            title.Font = Theme.H1;
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Location = new Point(22, 12);
            title.BackColor = Theme.Bg1;

            Label sub = new Label();
            sub.Text = "Reparatur · Netzwerk · Aufräumen · Diagnose";
            sub.Font = Theme.UISmall;
            sub.ForeColor = Theme.TextDim;
            sub.AutoSize = true;
            sub.Location = new Point(24, 42);
            sub.BackColor = Theme.Bg1;

            Panel right = new Panel();
            right.Dock = DockStyle.Right;
            right.Width = 300;
            right.BackColor = Theme.Bg1;

            Label admin = new Label();
            admin.Text = "●  Administrator";
            admin.Font = Theme.UISmall;
            admin.ForeColor = Theme.Green;
            admin.AutoSize = true;
            admin.Location = new Point(12, 14);
            admin.BackColor = Theme.Bg1;

            _restoreChk = new CheckBox();
            _restoreChk.Text = "Wiederherstellungspunkt vor Reparatur";
            _restoreChk.Font = Theme.UISmall;
            _restoreChk.ForeColor = Theme.Text;
            _restoreChk.BackColor = Theme.Bg1;
            _restoreChk.FlatStyle = FlatStyle.Flat;
            _restoreChk.AutoSize = true;
            _restoreChk.Location = new Point(12, 38);

            right.Controls.Add(admin);
            right.Controls.Add(_restoreChk);

            header.Controls.Add(right);
            header.Controls.Add(title);
            header.Controls.Add(sub);
            return header;
        }

        // ---------------- Sidebar ----------------
        void BuildSidebar(Panel parent)
        {
            Panel side = new Panel();
            side.Dock = DockStyle.Left;
            side.Width = 212;
            side.BackColor = Theme.Bg1;
            side.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, side.Width - 1, 0, side.Width - 1, side.Height);
            };

            for (int i = Catalog.Categories.Length - 1; i >= 0; i--)
            {
                NavButton nb = new NavButton(Catalog.Categories[i], Catalog.CategoryGlyphs[i]);
                nb.Selected2 += SelectCategory;
                _navs.Add(nb);
                side.Controls.Add(nb);
            }

            Label head = new Label();
            head.Text = "  KATEGORIEN";
            head.Font = Theme.UISmall;
            head.ForeColor = Theme.TextDim;
            head.Dock = DockStyle.Top;
            head.Height = 34;
            head.TextAlign = ContentAlignment.MiddleLeft;
            head.BackColor = Theme.Bg1;
            side.Controls.Add(head);

            parent.Controls.Add(side);
        }

        // ---------------- Rechte Seite (Inhalt + Konsole) ----------------
        void BuildRight(Panel parent)
        {
            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.BackColor = Theme.Bg0;

            // --- Konsole unten (dezent) ---
            _consolePanel = new Panel();
            _consolePanel.Dock = DockStyle.Bottom;
            _consolePanel.Height = 200;
            _consolePanel.BackColor = Theme.Console;
            _consolePanel.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, _consolePanel.Width, 0);
            };

            Panel cHead = new Panel();
            cHead.Dock = DockStyle.Top;
            cHead.Height = 30;
            cHead.BackColor = Theme.Console;

            Label cTitle = new Label();
            cTitle.Text = "  AUSGABE";
            cTitle.Font = Theme.UISmall;
            cTitle.ForeColor = Theme.TextDim;
            cTitle.AutoSize = false;
            cTitle.Dock = DockStyle.Left;
            cTitle.Width = 96;
            cTitle.TextAlign = ContentAlignment.MiddleLeft;
            cTitle.BackColor = Theme.Console;

            _status = new Label();
            _status.Text = "●  bereit";
            _status.Font = Theme.UISmall;
            _status.ForeColor = Theme.TextDim;
            _status.AutoSize = false;
            _status.Dock = DockStyle.Left;
            _status.Width = 150;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.BackColor = Theme.Console;

            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.Dock = DockStyle.Right;
            btns.FlowDirection = FlowDirection.RightToLeft;
            btns.Width = 440;
            btns.Padding = new Padding(0, 3, 8, 3);
            btns.BackColor = Theme.Console;

            Button collapseBtn = LinkButton("Ausblenden  ▾", Theme.TextDim, Theme.Console);
            collapseBtn.Click += delegate { SetConsoleVisible(false); };

            _stopBtn = LinkButton("Stoppen", Theme.Red, Theme.Console);
            _stopBtn.Enabled = false;
            _stopBtn.Click += delegate { _runner.Cancel(); };

            Button clearBtn = LinkButton("Leeren", Theme.TextDim, Theme.Console);
            clearBtn.Click += delegate { _out.Clear(); Welcome(); };

            Button saveBtn = LinkButton("Log speichern", Theme.TextDim, Theme.Console);
            saveBtn.Click += delegate { SaveLog(); };

            btns.Controls.Add(collapseBtn);
            btns.Controls.Add(saveBtn);
            btns.Controls.Add(clearBtn);
            btns.Controls.Add(_stopBtn);

            cHead.Controls.Add(_status);
            cHead.Controls.Add(cTitle);
            cHead.Controls.Add(btns);

            _out = new RichTextBox();
            _out.Dock = DockStyle.Fill;
            _out.ReadOnly = true;
            _out.BorderStyle = BorderStyle.None;
            _out.BackColor = Theme.Console;
            _out.ForeColor = Theme.TextDim;
            _out.Font = _monoReg;
            _out.WordWrap = false;
            _out.DetectUrls = false;
            _out.ScrollBars = RichTextBoxScrollBars.Both;

            _consolePanel.Controls.Add(_out);
            _consolePanel.Controls.Add(cHead);

            // --- Schmale Leiste, wenn Konsole eingeklappt ---
            _collapsedBar = new Panel();
            _collapsedBar.Dock = DockStyle.Bottom;
            _collapsedBar.Height = 30;
            _collapsedBar.BackColor = Theme.Bg0;
            _collapsedBar.Visible = false;
            _collapsedBar.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, _collapsedBar.Width, 0);
            };
            Button expandBtn = LinkButton("▴  Ausgabe einblenden", Theme.TextDim, Theme.Bg0);
            expandBtn.Dock = DockStyle.Left;
            expandBtn.Width = 220;
            expandBtn.TextAlign = ContentAlignment.MiddleLeft;
            expandBtn.Padding = new Padding(8, 0, 0, 0);
            expandBtn.Click += delegate { SetConsoleVisible(true); };
            _collapsedBar.Controls.Add(expandBtn);

            _split = new Splitter();
            _split.Dock = DockStyle.Bottom;
            _split.Height = 6;
            _split.BackColor = Theme.Bg0;
            _split.MinExtra = 180;
            _split.MinSize = 120;

            // --- Inhalt (Karten) ---
            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.Bg0;

            Panel ctHead = new Panel();
            ctHead.Dock = DockStyle.Top;
            ctHead.Height = 56;
            ctHead.BackColor = Theme.Bg0;

            _catTitle = new Label();
            _catTitle.Font = Theme.H1;
            _catTitle.ForeColor = Theme.Text;
            _catTitle.AutoSize = true;
            _catTitle.Location = new Point(22, 14);
            _catTitle.BackColor = Theme.Bg0;

            _catHint = new Label();
            _catHint.Font = Theme.UISmall;
            _catHint.ForeColor = Theme.TextDim;
            _catHint.AutoSize = true;
            _catHint.Location = new Point(24, 38);
            _catHint.BackColor = Theme.Bg0;

            ctHead.Controls.Add(_catTitle);
            ctHead.Controls.Add(_catHint);

            _cards = new FlowLayoutPanel();
            _cards.Dock = DockStyle.Fill;
            _cards.BackColor = Theme.Bg0;
            _cards.AutoScroll = true;
            _cards.WrapContents = true;
            _cards.FlowDirection = FlowDirection.LeftToRight;
            _cards.Padding = new Padding(12, 6, 12, 12);

            content.Controls.Add(_cards);
            content.Controls.Add(ctHead);

            // Fill zuerst, Kanten danach
            right.Controls.Add(content);
            right.Controls.Add(_collapsedBar);
            right.Controls.Add(_consolePanel);
            right.Controls.Add(_split);

            parent.Controls.Add(right);
        }

        Button LinkButton(string text, Color fg, Color bg)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = bg;
            b.FlatAppearance.MouseDownBackColor = bg;
            b.BackColor = bg;
            b.ForeColor = fg;
            b.Font = Theme.UISmall;
            b.AutoSize = false;
            b.Height = 24;
            b.Width = 100;
            b.Margin = new Padding(2, 1, 2, 1);
            b.Cursor = Cursors.Hand;
            Color baseFg = fg;
            b.MouseEnter += delegate { b.ForeColor = Theme.Accent; };
            b.MouseLeave += delegate { b.ForeColor = baseFg; };
            return b;
        }

        // ---------------- Logik ----------------
        void SelectCategory(string cat)
        {
            _activeCat = cat;
            foreach (NavButton nb in _navs)
                nb.IsSelected = (nb.Category == cat);

            _cards.SuspendLayout();
            while (_cards.Controls.Count > 0)
            {
                Control c = _cards.Controls[0];
                _cards.Controls.RemoveAt(0);
                c.Dispose();
            }

            int count = 0;
            foreach (MaintenanceAction a in _actions)
            {
                if (a.Category != cat) continue;
                Card card = new Card(a);
                card.Clicked += OnRun;
                _cards.Controls.Add(card);
                count++;
            }
            _cards.ResumeLayout();

            _catTitle.Text = cat;
            _catHint.Text = count + (count == 1 ? " Aktion" : " Aktionen") + " · klicken zum Ausführen";
        }

        void OnRun(MaintenanceAction a)
        {
            if (_runner.Running)
            {
                if (_consoleVisible)
                    Append("Es läuft bereits eine Aktion – bitte warten oder stoppen.", LogKind.Warn);
                return;
            }

            if (a.Danger)
            {
                DialogResult r = MessageBox.Show(this,
                    a.Title + "\n\n" + a.Desc + "\n\nWirklich ausführen?",
                    "Bestätigen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }

            List<Step> steps = new List<Step>();
            if (_restoreChk.Checked && a.IsRepair)
            {
                steps.Add(new Step
                {
                    File = "powershell.exe",
                    Args = "-NoProfile -ExecutionPolicy Bypass -Command \"try { Checkpoint-Computer -Description 'Wartungstool' -RestorePointType MODIFY_SETTINGS -EA Stop; 'Wiederherstellungspunkt erstellt.' } catch { 'Wiederherstellungspunkt uebersprungen: ' + $_.Exception.Message }\""
                });
            }
            steps.AddRange(a.Steps);

            _runner.Run(a.Title, steps);
        }

        void SetRunning(bool running)
        {
            _stopBtn.Enabled = running;
            if (running)
            {
                _spinPhase = 0;
                _status.ForeColor = Theme.Accent;
                _status.Text = SpinFrames[0] + "  läuft …";
                _spin.Start();
            }
            else
            {
                _spin.Stop();
                _status.ForeColor = Theme.TextDim;
                _status.Text = "●  bereit";
            }
        }

        // Konsole ein-/ausklappen
        void SetConsoleVisible(bool v)
        {
            _consoleVisible = v;
            _consolePanel.Visible = v;
            _split.Visible = v;
            _collapsedBar.Visible = !v;
        }

        // Abschluss einer Aktion -> bei eingeklappter Konsole ein Toast
        void OnComplete(string title, LogKind kind, string message)
        {
            if (_consoleVisible) return;

            Color accent;
            string glyph;
            if (kind == LogKind.Good) { accent = Theme.Green; glyph = "E73E"; }
            else if (kind == LogKind.Warn) { accent = Theme.Yellow; glyph = "E7BA"; }
            else { accent = Theme.Red; glyph = "E711"; }

            ShowToast(title, message, accent, glyph);
        }

        void ShowToast(string title, string msg, Color accent, string glyph)
        {
            ToastForm t = new ToastForm(title, msg, accent, glyph, delegate
            {
                SetConsoleVisible(true);
                try { _out.SelectionStart = _out.TextLength; _out.ScrollToCaret(); }
                catch { }
            });
            t.Owner = this;
            t.Done += delegate (ToastForm tt) { _toasts.Remove(tt); ReflowToasts(); };
            _toasts.Add(t);

            Point bse = ToastBase();
            int slot = _toasts.Count - 1;
            t.Pop(bse.X, bse.Y + slot * (t.Height + 10));
        }

        void ReflowToasts()
        {
            Point bse = ToastBase();
            for (int i = 0; i < _toasts.Count; i++)
                _toasts[i].MoveSlot(bse.Y + i * (_toasts[i].Height + 10));
        }

        Point ToastBase()
        {
            return new Point(Bounds.Right - 330 - 18, Bounds.Top + 96);
        }

        void Append(string text, LogKind k)
        {
            Color c = Theme.TextDim;
            bool bold = false;
            switch (k)
            {
                case LogKind.Header: c = Theme.Accent; bold = true; break;
                case LogKind.Good:   c = Theme.Green;  bold = true; break;
                case LogKind.Bad:    c = Theme.Red;    bold = true; break;
                case LogKind.Warn:   c = Theme.Yellow; bold = true; break;
                case LogKind.Dim:    c = Theme.TextDim; break;
                case LogKind.Normal: c = Theme.Text; break;
            }
            _out.SelectionStart = _out.TextLength;
            _out.SelectionLength = 0;
            _out.SelectionColor = c;
            _out.SelectionFont = bold ? _monoBold : _monoReg;
            _out.AppendText(text + Environment.NewLine);
            _out.SelectionColor = _out.ForeColor;
            _out.ScrollToCaret();
        }

        void Welcome()
        {
            Append("Windows-Wartung  ·  bereit", LogKind.Header);
            Append("Aktion links auswählen und auf eine Kachel klicken.", LogKind.Dim);
            Append("", LogKind.Normal);
        }

        void SaveLog()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "Textdatei (*.txt)|*.txt";
                d.FileName = "wartung-log.txt";
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    try { System.IO.File.WriteAllText(d.FileName, _out.Text); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fehler"); }
                }
            }
        }
    }
}
