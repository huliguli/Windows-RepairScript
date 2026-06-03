using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WartungsToolbox
{
    // Dezentes, animiertes Hinweis-Widget (Fade + Slide) oben rechts.
    public class ToastForm : Form
    {
        readonly string _title, _msg, _glyph;
        readonly Color _accent;
        readonly Action _onClick;
        readonly System.Windows.Forms.Timer _timer;

        int _f;
        int _targetX, _targetY;
        bool _closing;

        const int InDur = 16, HoldDur = 230, OutDur = 28;
        const double MaxOp = 0.97;

        public event Action<ToastForm> Done;

        public ToastForm(string title, string msg, Color accent, string glyphHex, Action onClick)
        {
            _title = title;
            _msg = msg;
            _accent = accent;
            _glyph = Catalog.Glyph(glyphHex);
            _onClick = onClick;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Width = 330;
            Height = 84;
            BackColor = Theme.Bg0;
            Opacity = 0;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 15;
            _timer.Tick += Tick;

            Click += delegate { OnActivate(); };
            Resize += delegate { ApplyRegion(); };
            ApplyRegion();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW (kein Alt-Tab)
                return cp;
            }
        }

        void ApplyRegion()
        {
            using (GraphicsPath p = Native.Rounded(new Rectangle(0, 0, Width, Height), 12))
                Region = new Region(p);
        }

        public void Pop(int targetX, int targetY)
        {
            _targetX = targetX;
            _targetY = targetY;
            Left = targetX;
            Top = targetY - 14;
            Show();
            _timer.Start();
        }

        public void MoveSlot(int targetY)
        {
            _targetY = targetY;
            if (_f > InDur && !_closing) Top = targetY;
        }

        void OnActivate()
        {
            if (_onClick != null) _onClick();
            BeginClose();
        }

        public void BeginClose()
        {
            if (_closing) return;
            _closing = true;
            if (_f < InDur + HoldDur) _f = InDur + HoldDur + 1; // direkt zur Ausblende-Phase
        }

        static double Ease(double t) { return t * t * (3 - 2 * t); }

        void Tick(object s, EventArgs e)
        {
            _f++;
            double op;
            int y;
            if (_f <= InDur)
            {
                double t = Ease((double)_f / InDur);
                op = MaxOp * t;
                y = (int)(_targetY - 14 + 14 * t);
            }
            else if (_f <= InDur + HoldDur)
            {
                op = MaxOp;
                y = _targetY;
            }
            else if (_f <= InDur + HoldDur + OutDur)
            {
                double t = Ease((double)(_f - InDur - HoldDur) / OutDur);
                op = MaxOp * (1 - t);
                y = (int)(_targetY + 8 * t);
            }
            else
            {
                _timer.Stop();
                if (Done != null) Done(this);
                Close();
                return;
            }
            try { Opacity = op; Top = y; Left = _targetX; }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            using (GraphicsPath p = Native.Rounded(r, 12))
            {
                using (var b = new SolidBrush(Theme.Card))
                    g.FillPath(b, p);

                g.SetClip(p);
                using (var ab = new SolidBrush(_accent))
                    g.FillRectangle(ab, 0, 0, 5, Height);
                g.ResetClip();

                using (var pen = new Pen(Theme.Border))
                    g.DrawPath(pen, p);
            }

            TextRenderer.DrawText(g, _glyph, Theme.GlyphBig, new Rectangle(16, 0, 34, Height), _accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, _title, Theme.UIBold, new Rectangle(60, 13, Width - 74, 22), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _msg, Theme.UISmall, new Rectangle(60, 35, Width - 74, 18), Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, "Klicken für Details →", Theme.UISmall, new Rectangle(60, 54, Width - 74, 18), _accent,
                TextFormatFlags.Left);
        }
    }
}
