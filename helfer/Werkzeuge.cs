using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    /// <summary>
    /// Startet dism.exe, sfc.exe, chkdsk.exe, pnputil.exe, netsh.exe und die Katalog-Schritte
    /// direkt: ohne Shell, mit Argument-Text, beide Ausgaben umgeleitet, Wachhund je Schritt
    /// und Baum-Kill (DISM hinterlaesst DismHost.exe, das sonst das Dateiende der Umleitung
    /// offen haelt). Kein Control, kein WinForms: laeuft lokal, ueber die Pipe und in --auto gleich.
    ///
    /// Verhalten bis 8.0 in src/CommandRunner.cs (RunStep, ReadWithProgress); seit 8.1 liegt es
    /// nur noch hier, ohne BeginInvoke: die Zeilen gehen an kontext.Schreibe, der Fortschritt
    /// als Prozent an dieselbe Stelle. Vollstaendige Fassung: docs/M2-ENTWURF.md Abschnitt 4.
    ///
    /// Jeder gestartete Prozess haengt an einem Job-Objekt (Nachtrag B4, docs/M2-ENTWURF.md
    /// Abschnitt 14): CreateJobObject mit KILL_ON_JOB_CLOSE, AssignProcessToJobObject direkt nach
    /// dem Start. Wachhund und Abbruch rufen TerminateJobObject ueber KillTree: das trifft
    /// DismHost.exe auch dann, wenn DISM selbst schon beendet ist (taskkill /PID <tot> /T findet
    /// dann nichts mehr, Exit 128), und nie einen fremden Prozess mit wiederverwendeter PID.
    /// Das Schliessen des Job-Handles am Ende des Schritts raeumt ab, was noch im Job lebt.
    /// Live nachgestellt mit cmd /c start /b ping -t localhost: taskkill auf die tote cmd-PID
    /// liess ping leben, TerminateJobObject beendete es und der Leser sah nach 1 ms sein EOF.
    ///
    /// Zeitgrenze in drei Netzen: der Wachhund-Timer beendet den Job bei timeoutMs (auch wenn
    /// der Hauptprozess schon weg ist und nur ein Enkel die Umleitung offen haelt), das
    /// WaitForExit dahinter beendet ihn 60 s spaeter noch einmal, und das Leeren der Leser ist
    /// auf 60 s begrenzt. Kein Schritt kann diese Methode unbegrenzt festhalten. Derselbe Timer
    /// prueft jede Sekunde den Abbruch: Ausfuehrungskontext.Abbrechen ruft KillTree nur, solange
    /// der Hauptprozess lebt; haelt danach ein Enkel die Umleitung, beendet der Timer den Job.
    ///
    /// -1 (nicht gestartet) und -2 (Zeitgrenze) zaehlen immer als Problem, auch bei IgnoreExit
    /// (Nachtrag B6): ein Schritt, der nie lief oder abgeschossen wurde, ist nicht "erledigt".
    ///
    /// Trockenlauf (kontext.Trocken): kein Prozess, nur die Zeile "(trocken) file args".
    /// </summary>
    public static class Werkzeuge
    {
        public const int StandardZeitgrenzeMs = 45 * 60 * 1000;

        /// <summary>Hoechstlaenge des Ausgabepuffers (tail), Vertrag Abschnitt 2.</summary>
        public const int TailMax = 6000;

        /// <summary>
        /// Startet file mit args direkt (UseShellExecute=false, CreateNoWindow, beide Ausgaben
        /// umgeleitet, enc null = OEM-Codepage). progress = zeichenweise lesen (DISM/SFC
        /// schreiben ihren Fortschritt mit \r auf EINE Zeile). Rueckgabe: Exit-Code
        /// (3010 wird zu 0 mit Hinweis und Werte["neustart"]="1"; -1 Startfehler; -2 Zeitgrenze).
        /// tail = die letzten Ausgabezeilen, hoechstens TailMax Zeichen.
        /// </summary>
        public static int Lauf(Ausfuehrungskontext k, string file, string args, int timeoutMs, Encoding enc,
                               bool progress, out string tail)
        {
            return Starten(k, file, args, timeoutMs, enc, progress, false, out tail);
        }

        /// <summary>
        /// Ein Katalog-Schritt (Step aus src). Detached: im eigenen Fenster starten, nicht
        /// abwarten, 0. IgnoreExit: ExitCode != 0 zaehlt nicht als Problem, -1 (nicht gestartet)
        /// und -2 (Zeitgrenze) aber immer (Nachtrag B6). TimeoutMs 0 = Standard.
        /// internal statt public, weil Step in src/MaintenanceAction.cs internal ist.
        /// </summary>
        internal static int Schritt(Ausfuehrungskontext k, Step s, out string tail)
        {
            tail = "";
            if (s == null || string.IsNullOrEmpty(s.File))
            {
                k.Schreibe("   Fehler: leerer Schritt, nichts gestartet.", "bad");
                NichtGestartet(k);
                return -1;
            }
            // Trockenlauf VOR der Detached-Weiche: auch wsreset.exe darf im Trockenlauf nicht starten.
            if (k.Trocken)
            {
                Trockenzeile(k, s.File, s.Args);
                return 0;
            }
            if (s.Detached)
            {
                k.Schreibe("›  " + s.File + " " + s.Args, "dim");
                try
                {
                    var p = new Process();
                    p.StartInfo.FileName = s.File;
                    p.StartInfo.Arguments = s.Args ?? "";
                    p.StartInfo.UseShellExecute = true;   // Detached: eigenes Fenster (cleanmgr, mdsched, chkdsk)
                    p.Start();
                    k.Schreibe("   (in eigenem Fenster gestartet)", "dim");
                    Protokollieren(k, s.File, s.Args, 0, 0, false);
                    return 0;
                }
                catch (Exception ex)
                {
                    k.Schreibe("   Fehler: " + ex.Message, "bad");
                    AppLog.Error("Werkzeug (eigenes Fenster) " + s.File, ex);
                    NichtGestartet(k);
                    Protokollieren(k, s.File, s.Args, -1, 0, false);
                    return -1;
                }
            }
            return Starten(k, s.File, s.Args ?? "", s.TimeoutMs, s.Enc, s.Progress, s.IgnoreExit, out tail);
        }

        /// <summary>
        /// Prozess samt Kindern beenden. Zuerst der Job der PID (TerminateJobObject): trifft
        /// DismHost.exe auch nach dem Ende von DISM und nie einen fremden Prozess mit
        /// wiederverwendeter PID. taskkill /T /F bleibt der Rueckfall, wenn zu der PID kein Job
        /// gehoert (Zuweisung fehlgeschlagen) oder TerminateJobObject scheitert. Ein Fehlschlag
        /// wird protokolliert (Exit-Code von taskkill oder Ausnahme), damit ein wirkungsloser
        /// Abbruch nicht still bleibt; mehr als melden kann diese Stelle nicht.
        /// </summary>
        public static void KillTree(int pid)
        {
            if (JobBeenden(pid)) return;
            try
            {
                var psi = new ProcessStartInfo("taskkill.exe", "/PID " + pid + " /T /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (var p = Process.Start(psi))
                {
                    if (p == null) { AppLog.Warn("taskkill für PID " + pid + " wurde nicht gestartet."); return; }
                    if (!p.WaitForExit(10000)) { AppLog.Warn("taskkill für PID " + pid + " antwortete 10 s lang nicht."); return; }
                    if (p.ExitCode != 0) AppLog.Warn("taskkill für PID " + pid + " meldete Exit " + p.ExitCode + " (Prozess schon weg oder nicht beendbar).");
                }
            }
            catch (Exception ex) { AppLog.Warn("taskkill für PID " + pid + " scheiterte: " + ex.Message); }
        }

        // ------------------------------------------------------------------ Kern

        static int Starten(Ausfuehrungskontext k, string file, string args, int timeoutMs, Encoding enc,
                           bool progress, bool ignoreExit, out string tail)
        {
            tail = "";
            args = args ?? "";
            if (k.Trocken)
            {
                Trockenzeile(k, file, args);
                return 0;
            }
            if (timeoutMs <= 0) timeoutMs = StandardZeitgrenzeMs;

            k.Schreibe("›  " + file + " " + args, "dim");
            var puffer = new Puffer();
            var sw = Stopwatch.StartNew();
            int code;
            int pid = 0;   // 0 = noch kein Prozess; der Job haengt an dieser PID
            try
            {
                var psi = new ProcessStartInfo();
                psi.FileName = file;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Encoding e = enc ?? Oem;
                psi.StandardOutputEncoding = e;
                psi.StandardErrorEncoding = e;

                using (var proc = new Process())
                {
                    proc.StartInfo = psi;
                    proc.ErrorDataReceived += delegate (object o, DataReceivedEventArgs ev)
                    {
                        if (!string.IsNullOrEmpty(ev.Data)) { puffer.Add(ev.Data); k.Schreibe(ev.Data, "normal"); }
                    };
                    // Fortschritts-Schritte (DISM/SFC) werden zeichenweise gelesen (LesenMitFortschritt);
                    // sonst zeilenweise asynchron.
                    if (!progress)
                    {
                        proc.OutputDataReceived += delegate (object o, DataReceivedEventArgs ev)
                        {
                            if (ev.Data != null) { puffer.Add(ev.Data); k.Schreibe(ev.Data, "normal"); }
                        };
                    }
                    proc.Start();
                    pid = proc.Id;
                    // Job direkt nach dem Start: Kinder, die der Prozess ab jetzt anlegt, erben ihn.
                    // Das Zeitfenster zwischen Start und Zuweisung ist kuerzer als jedes Laden einer
                    // EXE; ein in dieser Spanne angelegter Enkel bliebe ausserhalb (dann greift nur
                    // taskkill /T, solange der Elternprozess lebt). Scheitert die Zuweisung, laeuft
                    // der Schritt trotzdem: KillTree faellt dann auf taskkill zurueck, mit Warnung.
                    JobAnlegen(proc, file);
                    k.Aktuell = proc;
                    // Ein Abbruch, der VOR dem Start kam, haette den Prozess verpasst.
                    if (k.IstAbgebrochen) KillTree(pid);
                    proc.BeginErrorReadLine();

                    // Der Wachhund beendet den Job des Schritts (TerminateJobObject ueber KillTree
                    // erwischt auch DismHost.exe, das DISM hinterlaesst). Erst dadurch sieht der
                    // Leser sein Dateiende und WaitForExit kehrt ueberhaupt zurueck. Bewusst
                    // KEINE fruehe Rueckkehr bei HasExited: genau dann, wenn DISM schon weg ist
                    // und nur der Enkel die Umleitung offen haelt, muss der Job beendet werden.
                    // Der Timer tickt jede Sekunde: bis zur Zeitgrenze prueft er den Abbruch
                    // (Ausfuehrungskontext.Abbrechen ruft KillTree nur, solange der Hauptprozess
                    // lebt; danach beendet erst dieser Tick den Enkel), ab der Zeitgrenze beendet
                    // er den Job. Jeder der beiden Wege feuert genau einmal (Interlocked).
                    bool zeitAbgelaufen = false;
                    int zeitgrenzeGefeuert = 0;
                    int abbruchGefeuert = 0;
                    var timerFertig = new ManualResetEvent(false);
                    var wachhund = new System.Threading.Timer(delegate
                    {
                        try
                        {
                            if (sw.ElapsedMilliseconds >= timeoutMs)
                            {
                                if (Interlocked.Exchange(ref zeitgrenzeGefeuert, 1) != 0) return;
                                zeitAbgelaufen = true;
                                ZeitgrenzeMelden(k, proc, file, args, timeoutMs);
                            }
                            else
                            {
                                if (!k.IstAbgebrochen || Interlocked.Exchange(ref abbruchGefeuert, 1) != 0) return;
                                AppLog.Info("Abbruch: der Job von " + file + " wird beendet (PID " + pid + ").");
                            }
                            KillTree(pid);
                        }
                        catch (Exception ex) { AppLog.Warn("Wachhund für PID " + pid + " (" + file + ") scheiterte: " + ex.Message); }
                    }, null, 1000, 1000);
                    try
                    {
                        if (progress) LesenMitFortschritt(k, proc, puffer);
                        else proc.BeginOutputReadLine();
                        // Abbruch waehrend des Lesens: den Baum selbst beenden, sonst wartet
                        // WaitForExit auf ein Dateiende, das ein Enkelprozess offen haelt.
                        if (k.IstAbgebrochen && !proc.HasExited) KillTree(pid);

                        // Zweites Netz hinter dem Wachhund: laeuft der Prozess 60 s nach der
                        // Zeitgrenze noch (Job und taskkill fehlgeschlagen, Timer nicht gefeuert),
                        // wird der Baum erneut beendet und der Hauptprozess zur Sicherheit direkt.
                        int rest = timeoutMs + 60000 - (int)Math.Min(sw.ElapsedMilliseconds, int.MaxValue / 2);
                        if (rest < 60000) rest = 60000;
                        if (!proc.WaitForExit(rest))
                        {
                            zeitAbgelaufen = true;
                            AppLog.Warn("Schritt lief " + ((timeoutMs + 60000) / 60000) + " Minuten nach der Zeitgrenze noch, zweiter Abbruch: " + file + " " + args);
                            KillTree(pid);
                            try { if (!proc.HasExited) proc.Kill(); } catch (Exception ex) { AppLog.Warn("Kill für PID " + pid + " scheiterte: " + ex.Message); }
                            proc.WaitForExit(30000);
                        }
                        // WaitForExit ohne Argument wartet auch auf das Ende der Ausgabe-Leser;
                        // haelt ein Enkel die Umleitung trotz allem offen, wird nach 60 s ohne
                        // die letzten Zeilen weitergemacht statt fuer immer zu warten.
                        if (!LeserLeeren(proc, 60000))
                            AppLog.Warn("Ausgabe-Leser blieben 60 s nach dem Ende offen (Kindprozess?), Rest verworfen: " + file + " " + args);
                    }
                    finally
                    {
                        // Erst den Timer samt laufendem Rueckruf beenden, dann den Job schliessen:
                        // ein Tick nach dem Schliessen faende keinen Job mehr und fiele auf taskkill
                        // gegen eine womoeglich schon neu vergebene PID zurueck.
                        try
                        {
                            if (!wachhund.Dispose(timerFertig) || !timerFertig.WaitOne(15000))
                                AppLog.Warn("Der Wachhund-Timer von PID " + pid + " (" + file + ") ließ sich nicht sauber beenden.");
                        }
                        catch (Exception ex) { AppLog.Warn("Wachhund-Timer beenden: " + ex.Message); }
                        timerFertig.Close();
                    }

                    code = proc.HasExited ? proc.ExitCode : -2;
                    k.Aktuell = null;
                    // Schliessen mit KILL_ON_JOB_CLOSE: was vom Schritt noch lebt (ein Enkel, der
                    // die Umleitung 60 s lang offen hielt), endet jetzt, nicht erst mit dem Helfer.
                    JobSchliessen(pid);
                    if (zeitAbgelaufen)
                    {
                        puffer.Add("Zeitlimit: " + file + " wurde nach " + (timeoutMs / 60000) + " Minuten beendet.");
                        code = -2;
                    }
                }
            }
            catch (Exception ex)
            {
                k.Aktuell = null;
                // Mit KILL_ON_JOB_CLOSE endet ein schon gestarteter Prozess hier mit dem Job,
                // statt verwaist weiterzulaufen.
                if (pid != 0) JobSchliessen(pid);
                k.Schreibe("   Fehler: " + ex.Message, "bad");
                AppLog.Error("Werkzeug " + file, ex);
                if (pid == 0) NichtGestartet(k);
                else
                {
                    k.Schreibe("   ↳ mit Fehler abgebrochen: der Schritt zählt als fehlgeschlagen.", "bad");
                    Katalog.Problem(k);
                }
                tail = puffer.Text;
                Protokollieren(k, file, args, -1, (int)sw.Elapsed.TotalSeconds, false);
                return -1;
            }

            tail = puffer.Text;

            if (code == 3010)
            {
                // Dokumentierte Windows-Semantik: 3010 = ERROR_SUCCESS_REBOOT_REQUIRED. Der
                // Neustartbedarf ist eine Information, kein Fehler: er gilt auch bei IgnoreExit.
                k.Schreibe("   ↳ ExitCode 3010: erfolgreich, Windows braucht 1 Neustart, um die Änderung abzuschließen.", "good");
                k.Werte["neustart"] = "1";
                code = 0;
            }
            else if (code == -2)
            {
                // Interner Wert, kein Windows-Code: keine Hex-Darstellung, und ein abgeschossener
                // Schritt ist auch bei IgnoreExit nicht erledigt (Nachtrag B6).
                k.Schreibe("   ↳ Zeitgrenze erreicht: der Schritt wurde nach " + (timeoutMs / 60000) + " Minuten beendet und zählt als fehlgeschlagen.", "bad");
                Katalog.Problem(k);
            }
            else
            {
                string hex = code < 0 ? string.Format(" (0x{0:X8})", code) : "";
                k.Schreibe("   ↳ ExitCode " + code + hex, (code == 0 || ignoreExit) ? "dim" : "bad");
            }

            // Laienverstaendliche Deutung: erst die Tool-Ausgabe (SFC/DISM-Ergebnissaetze),
            // dann, falls der Schritt fehlschlug, der bekannte Exit-Code.
            bool oGood;
            string oxp = Explain.ForOutput(file, tail, out oGood);
            if (oxp != null) k.Schreibe((oGood ? "   ✔  " : "   ●  ") + oxp, oGood ? "good" : "warn");
            if (code != 0 && !ignoreExit)
            {
                string xp = Explain.ForExit(code);
                if (xp != null) k.Schreibe("   ●  " + xp, "warn");
            }

            Protokollieren(k, file, args, code, (int)sw.Elapsed.TotalSeconds, false);
            return code;
        }

        /// <summary>
        /// Rueckgabe -1 heisst "nicht gestartet": eine Zeile dazu und immer ein Problem, auch
        /// bei IgnoreExit (Nachtrag B6). Aufrufer bekommen -1 zusaetzlich als Rueckgabe.
        /// </summary>
        static void NichtGestartet(Ausfuehrungskontext k)
        {
            k.Schreibe("   ↳ nicht gestartet: der Schritt zählt als fehlgeschlagen.", "bad");
            Katalog.Problem(k);
        }

        // Die Zeilen des Wachhunds bei erreichter Zeitgrenze (app.log und Konsole des Aufrufers).
        static void ZeitgrenzeMelden(Ausfuehrungskontext k, Process proc, string file, string args, int timeoutMs)
        {
            bool weg = false;
            try { weg = proc.HasExited; } catch (Exception) { }
            AppLog.Warn("Zeitgrenze erreicht, Schritt wird beendet: " + file + " " + args
                        + (weg ? " (Hauptprozess schon beendet, ein Kindprozess hält die Ausgabe offen)" : ""));
            k.Schreibe("   Zeitgrenze von " + (timeoutMs / 60000) + " Minuten erreicht, dieser Schritt wurde beendet.", "bad");
        }

        // ------------------------------------------------------------------ Job-Objekt (Nachtrag B4)

        // Job je laufendem Schritt, Schluessel = PID des Hauptprozesses; Zugriff nur unter
        // _jobGate. Ein Woerterbuch statt eines einzelnen Feldes: liefen Hauptweg und geplante
        // Wartung im erhoehten Host doch einmal nebeneinander, ueberschriebe keiner den Job des
        // anderen. Der Eintrag verschwindet mit JobSchliessen am Ende des Schritts; KillTree mit
        // einer PID ohne Eintrag faellt auf taskkill zurueck.
        static readonly object _jobGate = new object();
        static readonly Dictionary<int, IntPtr> _jobs = new Dictionary<int, IntPtr>();

        /// <summary>
        /// Job anlegen (KILL_ON_JOB_CLOSE) und den Prozess zuweisen. false = kein Job; der Grund
        /// steht im app.log, der Schritt laeuft trotzdem (Rueckfall taskkill in KillTree).
        /// </summary>
        static bool JobAnlegen(Process proc, string file)
        {
            IntPtr job = IntPtr.Zero;
            try
            {
                job = Native.CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero)
                {
                    AppLog.Warn("Job-Objekt für " + file + " nicht angelegt (Win32-Fehler " + Marshal.GetLastWin32Error() + "); Abbruch läuft über taskkill.");
                    return false;
                }
                var info = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                int groesse = Marshal.SizeOf(typeof(Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                if (!Native.SetInformationJobObject(job, Native.JobObjectExtendedLimitInformation, ref info, groesse))
                {
                    AppLog.Warn("Job-Objekt für " + file + ": KILL_ON_JOB_CLOSE nicht gesetzt (Win32-Fehler " + Marshal.GetLastWin32Error() + "); Abbruch läuft über taskkill.");
                    Native.CloseHandle(job);
                    return false;
                }
                if (!Native.AssignProcessToJobObject(job, proc.Handle))
                {
                    AppLog.Warn("PID " + proc.Id + " (" + file + ") nicht dem Job zugewiesen (Win32-Fehler " + Marshal.GetLastWin32Error() + "); Abbruch läuft über taskkill.");
                    Native.CloseHandle(job);
                    return false;
                }
                lock (_jobGate)
                {
                    IntPtr alt;
                    if (_jobs.TryGetValue(proc.Id, out alt))
                    {
                        // Kann nur nach einer PID-Wiederverwendung ohne JobSchliessen vorkommen;
                        // den alten Handle schliessen statt ihn zu verlieren.
                        AppLog.Warn("Zu PID " + proc.Id + " gab es noch einen Job-Eintrag; er wird ersetzt.");
                        Native.CloseHandle(alt);
                    }
                    _jobs[proc.Id] = job;
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Job-Objekt für " + file + " scheiterte: " + ex.Message + "; Abbruch läuft über taskkill.");
                if (job != IntPtr.Zero) { try { Native.CloseHandle(job); } catch (Exception) { } }
                return false;
            }
        }

        /// <summary>
        /// TerminateJobObject auf den Job der PID. true = Job gefunden und beendet (alle Prozesse
        /// darin, auch Enkel nach dem Ende des Hauptprozesses); false = kein Job zu dieser PID
        /// oder TerminateJobObject scheiterte (dann mit Warnung, der Aufrufer nimmt taskkill).
        /// </summary>
        static bool JobBeenden(int pid)
        {
            try
            {
                lock (_jobGate)
                {
                    IntPtr job;
                    if (!_jobs.TryGetValue(pid, out job) || job == IntPtr.Zero) return false;
                    // Exit-Code 1 wie bei taskkill /F: der Schritt zaehlt danach als fehlgeschlagen.
                    if (Native.TerminateJobObject(job, 1)) return true;
                    AppLog.Warn("TerminateJobObject für PID " + pid + " scheiterte (Win32-Fehler " + Marshal.GetLastWin32Error() + "), Rückfall auf taskkill.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Job für PID " + pid + " beenden: " + ex.Message + ", Rückfall auf taskkill.");
                return false;
            }
        }

        /// <summary>Job-Handle der PID schliessen: mit KILL_ON_JOB_CLOSE endet alles, was darin noch lebt.</summary>
        static void JobSchliessen(int pid)
        {
            try
            {
                IntPtr job;
                lock (_jobGate)
                {
                    if (!_jobs.TryGetValue(pid, out job)) return;
                    _jobs.Remove(pid);
                }
                if (job != IntPtr.Zero && !Native.CloseHandle(job))
                    AppLog.Warn("Job-Handle für PID " + pid + " nicht geschlossen (Win32-Fehler " + Marshal.GetLastWin32Error() + ").");
            }
            catch (Exception ex) { AppLog.Warn("Job für PID " + pid + " schließen: " + ex.Message); }
        }

        // DISM/SFC schreiben ihren Fortschritt mit Carriage-Return (\r) auf EINE Zeile, ohne
        // Zeilenumbruch. Der zeilenbasierte Leser saehe das erst am Ende: hier zeichenweise lesen.
        static void LesenMitFortschritt(Ausfuehrungskontext k, Process proc, Puffer puffer)
        {
            var sb = new StringBuilder();
            int lastPct = -1;
            TextReader rdr = proc.StandardOutput;
            int ch;
            while ((ch = rdr.Read()) >= 0)
            {
                if (k.IstAbgebrochen) break;
                char c = (char)ch;
                if (c == '\r' || c == '\n')
                {
                    if (sb.Length > 0) { lastPct = Fortschrittszeile(k, puffer, sb.ToString(), lastPct); sb.Length = 0; }
                }
                else sb.Append(c);
            }
            if (sb.Length > 0) Fortschrittszeile(k, puffer, sb.ToString(), lastPct);
        }

        static int Fortschrittszeile(Ausfuehrungskontext k, Puffer puffer, string line, int lastPct)
        {
            int pct = ParsePercent(line);
            if (pct >= 0)
            {
                if (pct != lastPct) k.Schreibe("", "normal", pct);   // Fortschrittszeile selbst nicht als Zeile ausgeben
                return pct;
            }
            string t = line.TrimEnd();
            if (t.Length > 0) { puffer.Add(t); k.Schreibe(t, "normal"); }
            return lastPct;
        }

        static readonly Regex PctRx = new Regex("(\\d{1,3})([.,]\\d+)?\\s*%", RegexOptions.Compiled);
        static int ParsePercent(string line)
        {
            Match m = PctRx.Match(line);
            if (!m.Success) return -1;
            int v;
            if (!int.TryParse(m.Groups[1].Value, out v)) return -1;
            if (v < 0 || v > 100) return -1;
            return v;
        }

        // WaitForExit() ohne Argument kennt keine Grenze: es wartet auf das Dateiende beider
        // Umleitungen, das ein Enkelprozess beliebig lange offen halten kann. Deshalb auf einem
        // Hintergrund-Thread mit Join-Grenze; false = nicht fertig geworden, Rest wird verworfen.
        static bool LeserLeeren(Process proc, int ms)
        {
            Exception fehler = null;
            var t = new Thread(delegate ()
            {
                try { proc.WaitForExit(); }
                catch (Exception ex) { fehler = ex; }
            }) { IsBackground = true, Name = "werkzeug:leser-leeren" };
            t.Start();
            bool fertig = t.Join(ms);
            if (fehler != null) AppLog.Warn("Leeren der Ausgabe-Leser scheiterte: " + fehler.Message);
            return fertig;
        }

        static void Trockenzeile(Ausfuehrungskontext k, string file, string args)
        {
            k.Schreibe("(trocken) " + file + " " + (args ?? ""), "dim");
            Protokollieren(k, file, args, 0, 0, true);
        }

        // Eine schritt-Zeile je Werkzeug, wie bisher CheckFlow.RunProbe: Befehl, Exit-Code, Dauer.
        static void Protokollieren(Ausfuehrungskontext k, string file, string args, int exit, int sekunden, bool trocken)
        {
            if (k.Protokoll == null) return;
            string kennung;
            try { kennung = Path.GetFileName(file ?? "").ToLowerInvariant(); }
            catch (Exception) { kennung = (file ?? "").ToLowerInvariant(); }
            if (kennung.Length == 0) kennung = "werkzeug";
            k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, kennung,
                (trocken ? "(trocken) " : "") + file + " " + (args ?? "") + " ist beendet",
                new { exit, sekunden, trocken });
        }

        static readonly Encoding Oem = GetOem();
        static Encoding GetOem()
        {
            try { return Encoding.GetEncoding((int)Native.GetOEMCP()); }
            catch (Exception) { return Encoding.Default; }
        }

        /// <summary>
        /// Die letzten Ausgabezeilen eines Schritts (hoechstens TailMax Zeichen), Grundlage der
        /// Deutung ueber Explain.ForOutput. Die Leser laufen auf Threadpool-Threads: Zugriff
        /// nur unter der Sperre. Anders als der alte CommandRunner (erste 6000 Zeichen) bleiben
        /// hier die LETZTEN Zeichen stehen: die Ergebnissaetze von SFC und DISM stehen am Ende.
        /// </summary>
        sealed class Puffer
        {
            readonly object _gate = new object();
            readonly StringBuilder _sb = new StringBuilder();

            public void Add(string line)
            {
                if (string.IsNullOrEmpty(line)) return;
                lock (_gate)
                {
                    _sb.AppendLine(line);
                    if (_sb.Length > TailMax) _sb.Remove(0, _sb.Length - TailMax);
                }
            }

            public string Text
            {
                get { lock (_gate) { return _sb.ToString(); } }
            }
        }
    }
}
