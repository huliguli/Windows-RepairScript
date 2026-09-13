using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
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
    /// Ablage: %ProgramData%\WindowsWartung\logs\app.log (Kern.Ablage.Logs), bei 512 KB einmal
    /// rotiert (app.log -> app.1.log). Bewusst nur eine Generation: das Protokoll ist eine
    /// Diagnosehilfe, kein Archiv.
    ///
    /// Maschinenweit seit 8.1 (Rechte-Modell B): Oberflaeche ohne Rechte, Helfer und --auto
    /// erhoeht - alle schreiben in dieselbe Datei, sonst laege die Spur eines Fehlers je nach
    /// Modus in einem anderen Profil. Fehlt das Schreibrecht auf ProgramData, weicht Ablage
    /// selbst ins Nutzerprofil aus; wirft sie trotzdem, bleibt der alte Ort. Eine 8.0-Datei
    /// aus %LOCALAPPDATA%\WindowsWartung\logs wird beim ersten Zugriff uebernommen und dort
    /// auf app.log.uebernommen umbenannt (UebernahmeAbschliessen).
    ///
    /// Zwei Prozesse an einer Datei (Host ohne Rechte, Helfer erhoeht): Angehaengt wird mit
    /// FileShare.ReadWrite und einer Wiederholung nach 50 ms, damit eine gleichzeitige Zeile
    /// des anderen Prozesses nicht stumm verloren geht; rotiert wird unter dem benannten Mutex
    /// Global\WindowsWartung_AppLog, damit nicht der eine Prozess die Generation loescht, die
    /// der andere gerade weggeschoben hat. Liegt der Protokollordner hinter einer Abzweigung
    /// (Junction eines anderen Kontos, Ablage.IstAbzweigung), rotiert der erhoehte Prozess
    /// dort nicht (kein Loeschen und Verschieben an fremdem Ort).
    ///
    /// Schreibfehler duerfen die App nie stoeren - deshalb schluckt Write alles.
    /// </summary>
    static class AppLog
    {
        const long MaxBytes = 512 * 1024;
        const string RotationsMutex = "Global\\WindowsWartung_AppLog";
        static readonly object Gate = new object();
        static string _path;
        static bool _abzweigungGemeldet;   // die Warnung zur Abzweigung nur einmal je Prozess

        /// <summary>
        /// Nur fuer die Proben in tests/: verlegt das Protokoll in eine Wegwerf-Datei. Geht der
        /// Ablage vor und laesst die Uebernahme aus (wie History.PfadFuerProbe): sonst schriebe
        /// jede Probe, die AppLog mituebersetzt, in die echte app.log des Betreibers und zoege
        /// auf einem Rechner mit 8.0-Daten die einmalige Uebernahme durch. Im laufenden
        /// Programm bleibt das Feld immer null.
        /// </summary>
        internal static string PfadFuerProbe;

        public static string Path
        {
            get
            {
                string probe = PfadFuerProbe;
                if (!string.IsNullOrEmpty(probe)) return probe;
                if (_path == null)
                {
                    lock (Gate)
                    {
                        if (_path == null) _path = PfadErmitteln();
                    }
                }
                return _path;
            }
        }

        /// <summary>
        /// Ermittelt den Ort einmal je Prozess und uebernimmt dabei eine vorhandene alte Datei.
        /// Laeuft sehr frueh (vor jedem anderen Modul) und in jedem Modus, deshalb kein Weg
        /// hinaus, der werfen koennte: jeder Fehler endet beim alten Ort im Nutzerprofil.
        /// </summary>
        static string PfadErmitteln()
        {
            string alt = System.IO.Path.Combine(AlterOrdner(), "app.log");
            string neu;
            string abzweigung = null;
            try
            {
                // Ablage.Logs liefert null, wenn logs\ oder der Ordner darueber hinter einer
                // Abzweigung liegt: dorthin schreibt kein erhoehter Prozess. Dann bleibt der
                // alte Ort im Profil, und die erste Zeile dort sagt, warum.
                string ordner = Kern.Ablage.Logs();
                if (ordner == null)
                {
                    abzweigung = "Protokollordner unter " + Kern.Ablage.Maschinenweit() + " liegt hinter einer Abzweigung (Junction oder symbolischer Link); das Protokoll bleibt im Nutzerprofil.";
                    neu = null;
                }
                else neu = System.IO.Path.Combine(ordner, "app.log");
            }
            catch
            {
                neu = null;
            }
            if (neu == null)
            {
                try { Directory.CreateDirectory(AlterOrdner()); } catch { }
                _path = alt;   // vor der ersten Zeile setzen, sonst liefe Write erneut hier hinein
                if (abzweigung != null) Warn(abzweigung);
                return alt;
            }

            bool uebernommen = false;
            try { uebernommen = Kern.Ablage.Uebernehmen(alt, neu); } catch { }
            // Liegt am neuen Ort schon eine Datei (zweites Konto, das 8.0 selbst genutzt hat),
            // wird die alte trotzdem abgehakt, nur ohne Kopie: sonst bliebe sie liegen und
            // kaeme beim naechsten Prozess dieses Kontos herein, sobald die neue einmal fehlt.
            bool nurAbgehakt = false;
            if (!uebernommen)
            {
                try { nurAbgehakt = File.Exists(alt) && File.Exists(neu); } catch { }
            }
            bool abgehakt = (uebernommen || nurAbgehakt) && UebernahmeAbschliessen(alt);
            _path = neu;   // vor der ersten Zeile setzen, sonst liefe Write erneut hier hinein
            if (uebernommen) Info("Protokoll aus dem Nutzerprofil übernommen: " + alt
                                  + (abgehakt ? "" : " (alte Datei ließ sich nicht umbenennen)"));
            else if (nurAbgehakt) Info("Protokoll aus dem Nutzerprofil nicht übernommen, am neuen Ort liegt schon eines: " + alt
                                       + (abgehakt ? " auf .uebernommen umbenannt" : " ließ sich nicht umbenennen"));
            return neu;
        }

        /// <summary>
        /// Schliesst eine Uebernahme ab: die alte Datei im Nutzerprofil wird auf *.uebernommen
        /// umbenannt, sofort im selben Prozess. Erst damit ist die Uebernahme wirklich einmalig.
        /// Ablage.Uebernehmen kopiert nur, und zwar immer dann, wenn am neuen Ort nichts liegt:
        /// ohne das Umbenennen kaeme nach "Verlauf leeren" der alte Stand beim naechsten Start
        /// zurueck, und bei UAC ueber die Schulter (Standardnutzer, Adminkonto im Dialog) zoege
        /// der erhoehte Helfer die Datei aus dem FALSCHEN Profil nach ProgramData. Wer hier
        /// umbenennt, ist der Besitzer des Profils, die Rechte hat er also. Eine liegen
        /// gebliebene *.uebernommen aus einem frueheren Lauf wird ersetzt. Nie werfen.
        /// Liegt hier, weil AppLog die unterste Ebene ist und Verlauf, Zeitplan und Protokoll
        /// dieselbe Uebernahme brauchen.
        /// </summary>
        internal static bool UebernahmeAbschliessen(string alterPfad)
        {
            try
            {
                if (string.IsNullOrEmpty(alterPfad) || !File.Exists(alterPfad)) return false;
                string ziel = alterPfad + ".uebernommen";
                if (File.Exists(ziel)) File.Delete(ziel);
                File.Move(alterPfad, ziel);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Der Ordner bis 8.0: %LOCALAPPDATA%\WindowsWartung\logs.</summary>
        static string AlterOrdner()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Kern.Ablage.Ordnername, "logs");
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
                    Anhaengen(Path, line);
                }
            }
            catch { }   // Protokollieren darf nie zum Problem werden
        }

        // Anhaengen mit FileShare.ReadWrite (und Delete, damit die Rotation des anderen Prozesses
        // die Datei unter einer offenen Zeile wegschieben darf). File.AppendAllText oeffnete mit
        // FileShare.Read: schrieben Host und Helfer im selben Augenblick, warf der zweite eine
        // IOException, und die Zeile fehlte stumm. Bleibt es trotzdem bei einer IOException
        // (Virenschutz, Rotation), einmal nach 50 ms wiederholen; erst dann ist die Zeile weg.
        static void Anhaengen(string pfad, string line)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(line);
            for (int versuch = 0; ; versuch++)
            {
                try
                {
                    using (var fs = new FileStream(pfad, FileMode.Append, FileAccess.Write,
                                                   FileShare.ReadWrite | FileShare.Delete))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                    }
                    return;
                }
                catch (IOException)
                {
                    if (versuch >= 1) throw;
                    Thread.Sleep(50);
                }
            }
        }

        static void Rotate()
        {
            try
            {
                FileInfo fi = new FileInfo(Path);
                if (!fi.Exists || fi.Length < MaxBytes) return;

                // Loeschen und Verschieben sind die beiden Schreibzugriffe, die ein erhoehter
                // Prozess hier ausfuehrt: liegt der Ordner hinter einer Abzweigung, die ein
                // anderes lokales Konto gelegt hat (BUILTIN\Users darf im Laufzeitordner
                // aendern), traefen sie dessen Zielort. Dann keine Rotation, eine Warnung, und
                // die Datei waechst weiter. Die Probe (PfadFuerProbe) liegt ausserhalb der
                // Ablage und wird nicht geprueft.
                if (string.IsNullOrEmpty(PfadFuerProbe) && OrdnerIstAbzweigung(fi.DirectoryName))
                {
                    if (!_abzweigungGemeldet)
                    {
                        _abzweigungGemeldet = true;   // vor Warn: Warn -> Write -> Rotate liefe sonst im Kreis
                        Warn("Protokoll nicht rotiert: der Ordner " + fi.DirectoryName + " liegt hinter einer Abzweigung (Junction oder symbolischer Link); die Datei wächst weiter.");
                    }
                    return;
                }

                // Unter dem benannten Mutex, weil Host und Helfer dieselbe Datei rotieren: ohne
                // ihn las Prozess Y "512 KB voll", Prozess X verschob app.log nach app.1.log, und
                // Y loeschte anschliessend genau diese frische app.1.log, um seine eigene,
                // inzwischen kleine app.log darueberzuschieben. Im Mutex wird die Groesse deshalb
                // noch einmal gelesen. Scheitert der Mutex (kein Recht, fremde Sicherheits-
                // beschreibung), wird ohne ihn rotiert: lieber das seltene Rennen als ein
                // Protokoll, das nie mehr rotiert.
                Mutex m = RotationsMutexHolen();
                bool gehalten = false;
                try
                {
                    if (m != null)
                    {
                        try { gehalten = m.WaitOne(2000); }
                        catch (AbandonedMutexException) { gehalten = true; }   // der andere Prozess starb darin; wir haben ihn jetzt
                    }
                    fi.Refresh();
                    if (!fi.Exists || fi.Length < MaxBytes) return;
                    string old = System.IO.Path.ChangeExtension(Path, ".1.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(Path, old);
                }
                finally
                {
                    if (m != null)
                    {
                        try { if (gehalten) m.ReleaseMutex(); } catch { }
                        try { m.Close(); } catch { }
                    }
                }
            }
            catch { }
        }

        // Benannter Mutex fuer die Rotation, mit Zugriff fuer BUILTIN\Users: der erhoehte Helfer
        // legt ihn womoeglich zuerst an, und mit seiner Standard-Sicherheitsbeschreibung (nur
        // SYSTEM und Administratoren) kaeme der nicht erhoehte Host nicht mehr hinein. Global\,
        // weil beide Prozesse in derselben Sitzung, aber mit verschiedenen Token laufen und der
        // Name fuer beide derselbe sein muss (Mutexe brauchen dafuer kein Sonderrecht, anders
        // als Dateizuordnungen). null, wenn er sich weder anlegen noch oeffnen laesst; stumm.
        static Mutex RotationsMutexHolen()
        {
            try
            {
                var sicherheit = new MutexSecurity();
                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                sicherheit.AddAccessRule(new MutexAccessRule(users, MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                sicherheit.AddAccessRule(new MutexAccessRule(admins, MutexRights.FullControl, AccessControlType.Allow));
                var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                sicherheit.AddAccessRule(new MutexAccessRule(system, MutexRights.FullControl, AccessControlType.Allow));
                bool neu;
                return new Mutex(false, RotationsMutex, out neu, sicherheit);
            }
            catch
            {
                try { return Mutex.OpenExisting(RotationsMutex, MutexRights.Synchronize | MutexRights.Modify); }
                catch { return null; }
            }
        }

        // Liegt der Protokollordner hinter einer Abzweigung? Ablage.IstAbzweigung prueft den Pfad
        // und jede Komponente unterhalb des maschinenweiten Ordners; wirft es, gilt "ja": was sich
        // nicht pruefen laesst, bekommt keinen erhoehten Schreibzugriff.
        static bool OrdnerIstAbzweigung(string ordner)
        {
            try { return string.IsNullOrEmpty(ordner) || Kern.Ablage.IstAbzweigung(ordner); }
            catch { return true; }
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
                    "Einzelheiten für die Fehlersuche stehen in:" + Environment.NewLine + Path;
                System.Windows.Forms.MessageBox.Show(msg, "Windows-Wartung",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            catch { }
        }

        /// <summary>
        /// Sorgt dafuer, dass die Protokolldatei existiert, und gibt ihren Pfad zurueck.
        ///
        /// Das OEFFNEN erledigt bewusst der Aufrufer (ShellForm) ueber
        /// Shell.OeffneImNutzerkontext. Diese Klasse ist die unterste Ebene des Projekts -
        /// sie darf ausser kern\ (Ablage) von nichts abhaengen, sonst laesst sie sich nicht
        /// mehr mit wenigen Dateien uebersetzen. Genau daran ist die Signatur-Probe
        /// zerbrochen, als hier kurzzeitig ein Aufruf von Shell stand. Die Proben in
        /// tests\ muessen deshalb kern\Entscheidungen.cs und kern\Json.cs mituebersetzen
        /// (plus System.Runtime.Serialization.dll und System.Xml.dll fuer kern\Json.cs).
        /// </summary>
        public static string PfadZumOeffnen()
        {
            if (!File.Exists(Path)) Info("Protokoll geoeffnet (war noch leer).");
            return Path;
        }
    }
}
