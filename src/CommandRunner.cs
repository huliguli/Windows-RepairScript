using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace WartungsToolbox
{
    class CommandRunner
    {
        readonly Control _ui;
        readonly Action<string, LogKind> _log;
        readonly Action<bool> _onState;
        readonly Action<string, LogKind, string, double> _onComplete;
        readonly Action<int> _onProgress;
        volatile Process _current;
        volatile bool _cancel;

        // Puffer der letzten Ausgabezeilen des aktuellen Steps - Grundlage fuer die
        // laienverstaendliche Deutung (Explain.ForOutput). Reader-Callbacks laufen
        // auf Threadpool-Threads -> Zugriff nur unter _tailLock.
        readonly object _tailLock = new object();
        StringBuilder _tail = new StringBuilder();
        void TailAdd(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (_tailLock) { if (_tail.Length < 6000) _tail.AppendLine(line); }
        }
        string TailText()
        {
            lock (_tailLock) { return _tail.ToString(); }
        }

        public bool Running { get; private set; }

        /// <summary>
        /// Titel des gerade laufenden Auftrags ("Geplante Wartung", ein Werkzeugname, ...).
        /// Der Hauptweg nennt ihn dem Nutzer, wenn er einen Start ablehnen muss: "es laeuft
        /// gerade etwas" ohne zu sagen WAS ist eine Auskunft, mit der niemand etwas anfangen
        /// kann - schon gar nicht bei einem Lauf, den der Zeitplan von selbst gestartet hat.
        /// </summary>
        public string Title { get; private set; }

        // Zeitgrenze je Schritt, dieselbe wie im AutoRunner. Ohne sie haelt ein einziger
        // haengender Befehl den ganzen Lauf fuer immer fest: ReadWithProgress blockiert in
        // rdr.Read(), und WaitForExit() ohne Argument wartet zusaetzlich darauf, dass die
        // Ausgabe-Leser das Dateiende sehen - was ein ueberlebender Enkelprozess (DISM
        // startet DismHost.exe) beliebig lange verhindern kann. Der Nutzer saehe dann einen
        // Balken, der bei irgendeiner Prozentzahl stehenbleibt, und keine Zeile im Protokoll.
        const int StepTimeoutMs = 45 * 60 * 1000;

        public CommandRunner(Control ui, Action<string, LogKind> log, Action<bool> onState,
                             Action<string, LogKind, string, double> onComplete, Action<int> onProgress)
        {
            _ui = ui;
            _log = log;
            _onState = onState;
            _onComplete = onComplete;
            _onProgress = onProgress;
        }

        void Log(string s, LogKind k)
        {
            if (_ui != null && _ui.IsHandleCreated)
            {
                try { _ui.BeginInvoke((Action)delegate { _log(s, k); }); }
                catch { }
            }
        }

        void Progress(int pct)
        {
            if (_onProgress != null && _ui != null && _ui.IsHandleCreated)
            {
                try { _ui.BeginInvoke((Action)delegate { _onProgress(pct); }); }
                catch { }
            }
        }

        public void Cancel()
        {
            if (!Running) return;
            _cancel = true;
            try
            {
                Process p = _current;
                if (p != null && !p.HasExited) KillTree(p.Id);
            }
            catch { }
        }

        public void Run(string title, List<Step> steps)
        {
            List<Job> jobs = new List<Job>();
            jobs.Add(new Job { Title = title, Steps = steps });
            RunJobs(title, jobs);
        }

        public void RunJobs(string overallTitle, List<Job> jobs)
        {
            if (Running) return;
            Running = true;
            Title = overallTitle;
            _cancel = false;
            _onState(true);

            var t = new Thread(delegate ()
            {
                var sw = Stopwatch.StartNew();
                bool problem = false;
                foreach (Job job in jobs)
                {
                    if (_cancel) break;
                    Log("▶  " + job.Title, LogKind.Header);
                    foreach (Step s in job.Steps)
                    {
                        if (_cancel) break;
                        int code = RunStep(s);
                        if (code != 0 && !s.IgnoreExit) problem = true;
                    }
                }
                sw.Stop();
                double fsec = sw.Elapsed.TotalSeconds;
                LogKind fk;
                string fmsg;
                if (_cancel)
                {
                    Log("✖  Abgebrochen.", LogKind.Bad);
                    fk = LogKind.Bad; fmsg = "Abgebrochen";
                }
                else if (problem)
                {
                    Log(string.Format("●  Fertig, aber nicht alles hat geklappt ({0:0.0}s) – die gelben Hinweise oben erklären Ursache und Lösung.", sw.Elapsed.TotalSeconds), LogKind.Warn);
                    fk = LogKind.Warn; fmsg = "Mit Hinweisen abgeschlossen";
                }
                else
                {
                    Log(string.Format("✔  Fertig in {0:0.0}s", sw.Elapsed.TotalSeconds), LogKind.Good);
                    fk = LogKind.Good; fmsg = string.Format("Erfolgreich in {0:0.0}s", sw.Elapsed.TotalSeconds);
                }
                Log("", LogKind.Normal);

                Running = false;
                Title = null;
                _current = null;
                string ftitle = overallTitle;
                if (_ui != null && _ui.IsHandleCreated)
                {
                    try
                    {
                        _ui.BeginInvoke((Action)delegate
                        {
                            _onState(false);
                            if (_onComplete != null) _onComplete(ftitle, fk, fmsg, fsec);
                        });
                    }
                    catch { }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        int RunStep(Step s)
        {
            Log("›  " + s.File + " " + s.Args, LogKind.Dim);
            lock (_tailLock) { _tail = new StringBuilder(); }
            try
            {
                if (s.Detached)
                {
                    var p = new Process();
                    p.StartInfo.FileName = s.File;
                    p.StartInfo.Arguments = s.Args;
                    p.StartInfo.UseShellExecute = true;
                    p.Start();
                    Log("   (in eigenem Fenster gestartet)", LogKind.Dim);
                    return 0;
                }

                Encoding enc = s.Enc != null ? s.Enc : Oem;
                var psi = new ProcessStartInfo();
                psi.FileName = s.File;
                psi.Arguments = s.Args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = enc;
                psi.StandardErrorEncoding = enc;

                using (var proc = new Process())
                {
                    proc.StartInfo = psi;
                    proc.ErrorDataReceived += delegate (object o, DataReceivedEventArgs e)
                    {
                        if (!string.IsNullOrEmpty(e.Data)) { TailAdd(e.Data); Log(e.Data, LogKind.Normal); }
                    };
                    // Fortschritts-Schritte (DISM/SFC) werden zeichenweise gelesen (siehe ReadWithProgress);
                    // sonst zeilenweise asynchron.
                    if (!s.Progress)
                    {
                        proc.OutputDataReceived += delegate (object o, DataReceivedEventArgs e)
                        {
                            if (e.Data != null) { TailAdd(e.Data); Log(e.Data, LogKind.Normal); }
                        };
                    }
                    _current = proc;
                    proc.Start();
                    proc.BeginErrorReadLine();

                    // Der Wachhund beendet den Schritt samt Kindern (taskkill /T erwischt
                    // auch DismHost.exe, das DISM hinterlaesst). Erst dadurch sieht der
                    // Leser sein Dateiende und WaitForExit kehrt ueberhaupt zurueck.
                    // System.Threading.Timer voll ausgeschrieben: System.Windows.Forms
                    // bringt einen gleichnamigen Typ mit, beide sind hier eingebunden.
                    using (var wachhund = new System.Threading.Timer(delegate
                    {
                        try
                        {
                            if (proc.HasExited) return;
                            AppLog.Warn("Zeitgrenze erreicht, Schritt wird beendet: " + s.File + " " + s.Args);
                            Log("   Zeitgrenze von " + (StepTimeoutMs / 60000) +
                                " Minuten erreicht - dieser Schritt wurde beendet.", LogKind.Bad);
                            KillTree(proc.Id);
                        }
                        catch { }
                    }, null, StepTimeoutMs, System.Threading.Timeout.Infinite))
                    {
                        if (s.Progress) ReadWithProgress(proc);
                        else proc.BeginOutputReadLine();
                        proc.WaitForExit();
                    }

                    int code = proc.ExitCode;
                    _current = null;

                    if (code == 3010 && !s.IgnoreExit)
                    {
                        // Dokumentierte Windows-Semantik: 3010 = ERROR_SUCCESS_REBOOT_REQUIRED.
                        // Vorher wurde das faelschlich als Fehler gewertet.
                        Log("   ↳ ExitCode 3010 – Erfolgreich; Windows braucht einen Neustart, um die Änderung abzuschließen.", LogKind.Good);
                        code = 0;
                    }
                    else
                    {
                        string hex = code < 0 ? string.Format(" (0x{0:X8})", code) : "";
                        Log("   ↳ ExitCode " + code + hex, (code == 0 || s.IgnoreExit) ? LogKind.Dim : LogKind.Bad);
                    }

                    // Laienverstaendliche Deutung: erst die Tool-Ausgabe (SFC/DISM-Ergebnissaetze),
                    // dann - falls der Schritt fehlschlug - der bekannte Exit-Code.
                    bool oGood;
                    string oxp = Explain.ForOutput(s.File, TailText(), out oGood);
                    if (oxp != null) Log((oGood ? "   ✔  " : "   ●  ") + oxp, oGood ? LogKind.Good : LogKind.Warn);
                    if (code != 0 && !s.IgnoreExit)
                    {
                        string xp = Explain.ForExit(code);
                        if (xp != null) Log("   ●  " + xp, LogKind.Warn);
                    }
                    return code;
                }
            }
            catch (Exception ex)
            {
                Log("   Fehler: " + ex.Message, LogKind.Bad);
                return -1;
            }
        }

        // DISM/SFC schreiben ihren Fortschritt mit Carriage-Return (\r) auf EINE Zeile, ohne Zeilenumbruch.
        // Der zeilenbasierte Reader (BeginOutputReadLine) wuerde das erst am Ende sehen -> hier zeichenweise lesen.
        void ReadWithProgress(Process proc)
        {
            StringBuilder sb = new StringBuilder();
            int lastPct = -1;
            System.IO.TextReader rdr = proc.StandardOutput;
            int ch;
            while ((ch = rdr.Read()) >= 0)
            {
                if (_cancel) break;
                char c = (char)ch;
                if (c == '\r' || c == '\n')
                {
                    if (sb.Length > 0) { lastPct = HandleProgressLine(sb.ToString(), lastPct); sb.Length = 0; }
                }
                else sb.Append(c);
            }
            if (sb.Length > 0) HandleProgressLine(sb.ToString(), lastPct);
        }

        int HandleProgressLine(string line, int lastPct)
        {
            int pct = ParsePercent(line);
            if (pct >= 0)
            {
                if (pct != lastPct) Progress(pct);   // Fortschrittszeile selbst nicht als Log-Spam ausgeben
                return pct;
            }
            string t = line.TrimEnd();
            if (t.Length > 0) { TailAdd(t); Log(t, LogKind.Normal); }
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

        static readonly Encoding Oem = GetOem();
        static Encoding GetOem()
        {
            try { return Encoding.GetEncoding((int)Native.GetOEMCP()); }
            catch { return Encoding.Default; }
        }

        static void KillTree(int pid)
        {
            try
            {
                var psi = new ProcessStartInfo("taskkill.exe", "/PID " + pid + " /T /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
        }
    }
}
