using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WartungsToolbox
{
    // Aktionskachel
    class Card : Panel
    {
        public MaintenanceAction Action;
        public event Action<MaintenanceAction> Clicked;
        Label _glyph, _title, _desc;
        bool _hover;

        public Card(MaintenanceAction a)
        {
            Action = a;
            Size = new Size(332, 110);
            Margin = new Padding(9);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _glyph = new Label();
            _glyph.Text = Catalog.Glyph(a.Glyph);
            _glyph.Font = Theme.GlyphBig;
            _glyph.ForeColor = a.Danger ? Theme.Yellow : Theme.Accent;
            _glyph.AutoSize = false;
            _glyph.Size = new Size(42, 42);
            _glyph.Location = new Point(16, 18);
            _glyph.TextAlign = ContentAlignment.MiddleCenter;
            _glyph.BackColor = Theme.Card;

            _title = new Label();
            _title.Text = a.Title;
            _title.Font = Theme.UIBold;
            _title.ForeColor = Theme.Text;
            _title.AutoSize = false;
            _title.Location = new Point(66, 16);
            _title.Size = new Size(252, 22);
            _title.BackColor = Theme.Card;

            _desc = new Label();
            _desc.Text = a.Desc;
            _desc.Font = Theme.UISmall;
            _desc.ForeColor = Theme.TextDim;
            _desc.AutoSize = false;
            _desc.Location = new Point(66, 40);
            _desc.Size = new Size(252, 60);
            _desc.BackColor = Theme.Card;

            Controls.Add(_glyph);
            Controls.Add(_title);
            Controls.Add(_desc);

            Control[] all = { this, _glyph, _title, _desc };
            foreach (Control c in all)
            {
                c.Click += delegate { if (Clicked != null) Clicked(Action); };
                c.MouseEnter += delegate { SetHover(true); };
                c.MouseLeave += delegate
                {
                    Point p = PointToClient(Cursor.Position);
                    if (!ClientRectangle.Contains(p)) SetHover(false);
                };
            }

            Resize += delegate { ApplyRegion(); };
            ApplyRegion();
        }

        void ApplyRegion()
        {
            using (GraphicsPath p = Native.Rounded(new Rectangle(0, 0, Width, Height), 12))
                Region = new Region(p);
        }

        void SetHover(bool h)
        {
            if (_hover == h) return;
            _hover = h;
            Color bg = h ? Theme.CardHover : Theme.Card;
            BackColor = bg;
            _glyph.BackColor = bg;
            _title.BackColor = bg;
            _desc.BackColor = bg;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Native.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            using (var pen = new Pen(_hover ? Theme.Accent : Theme.Border))
                e.Graphics.DrawPath(pen, p);
        }
    }

    // Sidebar-Eintrag
    class NavButton : Control
    {
        public string Category;
        string _glyph;
        bool _selected, _hover;
        public event Action<string> Selected2;

        public NavButton(string category, string glyphHex)
        {
            Category = category;
            _glyph = Catalog.Glyph(glyphHex);
            Height = 46;
            Dock = DockStyle.Top;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            MouseEnter += delegate { _hover = true; Invalidate(); };
            MouseLeave += delegate { _hover = false; Invalidate(); };
            Click += delegate { if (Selected2 != null) Selected2(Category); };
        }

        public bool IsSelected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg1);

            if (_selected)
            {
                using (var b = new SolidBrush(Color.FromArgb(36, 42, 51)))
                    g.FillRectangle(b, 0, 0, Width, Height);
                using (var a = new SolidBrush(Theme.Accent))
                    g.FillRectangle(a, 0, 8, 3, Height - 16);
            }
            else if (_hover)
            {
                using (var b = new SolidBrush(Color.FromArgb(32, 36, 44)))
                    g.FillRectangle(b, 0, 0, Width, Height);
            }

            Color fg = _selected ? Theme.Accent : (_hover ? Theme.Text : Theme.TextDim);
            TextRenderer.DrawText(g, _glyph, Theme.Glyph, new Rectangle(16, 0, 26, Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            TextRenderer.DrawText(g, Category, Theme.UI, new Rectangle(50, 0, Width - 54, Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }
}
