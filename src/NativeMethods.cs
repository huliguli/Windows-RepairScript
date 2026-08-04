using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WartungsToolbox
{
    static class Native
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        [DllImport("kernel32.dll")]
        public static extern uint GetOEMCP();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // Fuer den Zweitstart: bestehendes Fenster wiederherstellen und nach vorn holen.
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        public const int SW_RESTORE = 9;

        // Synchron mit Timeout: Rueckgabe + lpdwResult zeigen, ob der Empfaenger die
        // Nachricht WIRKLICH verarbeitet hat (Handshake). PostMessage allein meldet
        // auch dann Erfolg, wenn die Nachricht verworfen wird (UIPI) oder eine alte
        // App-Version sie gar nicht kennt.
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                                                       uint flags, uint timeoutMs, out IntPtr result);
        public const uint SMTO_NORMAL = 0x0000;
        public const uint SMTO_ABORTIFHUNG = 0x0002;

        // Systemweit registrierte Fenster-Nachricht: der --auto-Prozess uebergibt damit
        // den geplanten Wartungslauf an eine bereits offene App-Instanz (gleicher String
        // in beiden Prozessen => gleiche Message-ID).
        public static readonly uint WM_WW_RUNAUTO = RegisterWindowMessage("WindowsWartung.RunAuto");

        // Dunkle Titelleiste unter Windows 10/11
        public static void UseDarkTitleBar(IntPtr hwnd)
        {
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, 4);
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            if (d <= 0 || r.Width <= d || r.Height <= d)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
