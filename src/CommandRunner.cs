using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WartungsToolbox
{
    class CommandRunner
    {
        readonly Control _ui;
        readonly Action<string, LogKind> _log;
        readonly Action<bool> _onState;
        readonly Action<string, LogKind, string> _onComplete;
        Process _current;
        volatile bool _cancel;

        public bool Running { get; private set; }

        public CommandRunner(Control ui, Action<string, LogKind> log, Action<bool> onState,
                             Action<string, LogKind, string> onComplete)
        {
            _ui = ui;
            _log = log;
            _onState = onState;
            _onComplete = onComplete;
        }

        void Log(string s, LogKind k)
        {
            if (_ui != null && _ui.IsHandleCreated)
            {
                try { _ui.BeginInvoke((Action)delegate { _log(s, k); }); }
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
                        if (code != 0) problem = true;
                    }
                }
                sw.Stop();
                LogKind fk;
                string fmsg;
                if (_cancel)
                {
                    Log("✖  Abgebrochen.", LogKind.Bad);
                    fk = LogKind.Bad; fmsg = "Abgebrochen";
                }
                else if (problem)
                {
                    Log(string.Format("●  Fertig mit Hinweisen ({0:0.0}s) – siehe ExitCodes oben.", sw.Elapsed.TotalSeconds), LogKind.Warn);
                    fk = LogKind.Warn; fmsg = "Mit Hinweisen abgeschlossen";
                }
                else
                {
                    Log(string.Format("✔  Fertig in {0:0.0}s", sw.Elapsed.TotalSeconds), LogKind.Good);
                    fk = LogKind.Good; fmsg = string.Format("Erfolgreich in {0:0.0}s", sw.Elapsed.TotalSeconds);
                }
                Log("", LogKind.Normal);

                Running = false;
                _current = null;
                string ftitle = overallTitle;
                if (_ui != null && _ui.IsHandleCreated)
                {
                    try
                    {
                        _ui.BeginInvoke((Action)delegate
                        {
                            _onState(false);
                            if (_onComplete != null) _onComplete(ftitle, fk, fmsg);
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
                    proc.OutputDataReceived += delegate (object o, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) Log(e.Data, LogKind.Normal);
                    };
                    proc.ErrorDataReceived += delegate (object o, DataReceivedEventArgs e)
                    {
                        if (!string.IsNullOrEmpty(e.Data)) Log(e.Data, LogKind.Normal);
                    };
                    _current = proc;
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    proc.WaitForExit();
                    int code = proc.ExitCode;
                    _current = null;
                    Log("   ↳ ExitCode " + code, code == 0 ? LogKind.Dim : LogKind.Bad);
                    return code;
                }
            }
            catch (Exception ex)
            {
                Log("   Fehler: " + ex.Message, LogKind.Bad);
                return -1;
            }
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
