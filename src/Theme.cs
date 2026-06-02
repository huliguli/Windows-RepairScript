using System;
using System.Drawing;

namespace WartungsToolbox
{
    static class Theme
    {
        public static readonly Color Bg0       = Color.FromArgb(22, 24, 28);
        public static readonly Color Bg1       = Color.FromArgb(27, 30, 36);
        public static readonly Color Card      = Color.FromArgb(32, 36, 44);
        public static readonly Color CardHover = Color.FromArgb(41, 47, 57);
        public static readonly Color Console   = Color.FromArgb(16, 18, 23);
        public static readonly Color Border    = Color.FromArgb(44, 49, 60);
        public static readonly Color Text      = Color.FromArgb(230, 232, 236);
        public static readonly Color TextDim   = Color.FromArgb(138, 143, 163);
        public static readonly Color Accent    = Color.FromArgb(78, 205, 196);
        public static readonly Color Green     = Color.FromArgb(152, 195, 121);
        public static readonly Color Yellow    = Color.FromArgb(229, 192, 123);
        public static readonly Color Red       = Color.FromArgb(224, 108, 117);

        public static readonly Font UI       = new Font("Segoe UI", 9.5f);
        public static readonly Font UIBold   = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        public static readonly Font UISmall  = new Font("Segoe UI", 8.5f);
        public static readonly Font H1       = new Font("Segoe UI Semibold", 15f, FontStyle.Bold);
        public static readonly Font Glyph    = new Font("Segoe MDL2 Assets", 13f);
        public static readonly Font GlyphBig = new Font("Segoe MDL2 Assets", 20f);
        public static readonly Font Mono     = new Font(MonoName(), 9f);

        static string MonoName()
        {
            string[] candidates = { "Cascadia Mono", "Consolas", "Lucida Console" };
            foreach (string n in candidates)
            {
                using (var f = new Font(n, 9f))
                {
                    if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase))
                        return n;
                }
            }
            return "Consolas";
        }
    }
}
