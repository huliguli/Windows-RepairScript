using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace WartungsToolbox
{
    /// <summary>
    /// Datei-Protokoll der App selbst (nicht der Wartungslaeufe).
    ///
    /// Zweck: Wenn beim Nutzer etwas schiefgeht, gab es bisher keinerlei Spur - Fehler
    /// verschwanden in leeren catch-Bloecken. Hier landen Start, Abschluss, Warnungen und
    /// vor allem unbehandelte Ausnahmen mit Stapelverfolgung.
    ///
    /// Ablage: %LOCALAPPDATA%\WindowsWartung\logs\app.log, bei 512 KB einmal rotiert
    /// (app.log -> app.1.log). Bewusst nur eine Generation: das Protokoll ist eine
    /// Diagnosehilfe, kein Archiv.
    ///
    /// Schreibfehler duerfen die App nie stoeren - deshalb schluckt Write alles.
    /// </summary>
    static class AppLog
    {
        const long MaxBytes = 512 * 1024;
        static readonly object Gate = new object();
        static string _path;

        public static string Path
        {
            get
            {
                if (_path == null)
                {
                    string dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "WindowsWartung", "logs");
                    try { Directory.CreateDirectory(dir); } catch { }
                    _path = System.IO.Path.Combine(dir, "app.log");
                }
                return _path;
            }
        }

        public static void Info(string message) { Write("INFO ", message); }
        public static void Warn(string message) { Write("WARN ", message); }
        public static void Error(string message) { Write("FEHLER", message); }

        public static void Error(string context, Exception ex)
        {
            if (ex == null) { Error(context); return; }
            Write("FEHLER", context + ": " + ex.GetType().Name + " - " + ex.Message
                             + Environment.NewLine + ex.StackTrace);
        }

        static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Rotate();
                    string line = string.Format("{0:yyyy-MM-dd HH:mm:ss}  {1}  {2}{3}",
                        DateTime.Now, level, message, Environment.NewLine);
                    File.AppendAllText(Path, line, Encoding.UTF8);
                }
            }
            catch { }   // Protokollieren darf nie zum Problem werden
        }

        static void Rotate()
        {
            try
            {
                FileInfo fi = new FileInfo(Path);
                if (!fi.Exists || fi.Length < MaxBytes) return;
                string old = System.IO.Path.ChangeExtension(Path, ".1.log");
                if (File.Exists(old)) File.Delete(old);
                File.Move(Path, old);
            }
            catch { }
        }

        /// <summary>
        /// Haengt die globalen Auffangnetze ein. Ohne sie beendet Windows die App bei einer
        /// unbehandelten Ausnahme wortlos - der Nutzer sieht nur ein verschwundenes Fenster.
        /// </summary>
        public static void InstallGlobalHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Error("Unbehandelte Ausnahme", e.ExceptionObject as Exception);
            };

            System.Windows.Forms.Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                Error("Unbehandelte Ausnahme im Oberflaechen-Thread", e.Exception);
                ShowFatal(e.Exception);
            };

            System.Windows.Forms.Application.SetUnhandledExceptionMode(
                System.Windows.Forms.UnhandledExceptionMode.CatchException);
        }

        /// <summary>
        /// Letzte Meldung an den Nutzer - in Alltagssprache, mit dem Weg zum Protokoll.
        /// </summary>
        static void ShowFatal(Exception ex)
        {
            try
            {
                string msg =
                    "In Windows-Wartung ist ein unerwarteter Fehler aufgetreten." + Environment.NewLine + Environment.NewLine +
                    "Ihrem PC ist dabei nichts passiert. Bitte starten Sie das Programm neu." + Environment.NewLine + Environment.NewLine +
                    "Einzelheiten fuer die Fehlersuche stehen in:" + Environment.NewLine + Path;
                System.Windows.Forms.MessageBox.Show(msg, "Windows-Wartung",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            catch { }
        }

        /// <summary>Oeffnet das Protokoll im Standard-Editor (Schaltflaeche in den Einstellungen).</summary>
        public static void Open()
        {
            try
            {
                if (!File.Exists(Path)) Info("Protokoll geoeffnet (war noch leer).");
                Process.Start(new ProcessStartInfo(Path) { UseShellExecute = true });
            }
            catch (Exception ex) { Error("Protokoll konnte nicht geoeffnet werden", ex); }
        }
    }
}
