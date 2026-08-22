using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace WartungsToolbox
{
    /// <summary>
    /// Der Hauptweg der App: "PC prüfen" und "Gefundene Probleme beheben".
    ///
    /// Alles, was der Laie ohne Vorwissen bedienen soll, laeuft hierueber. Der
    /// Werkzeugkasten mit den 28 Einzelaktionen bleibt vollstaendig erhalten, liegt
    /// aber hinter "Alle Werkzeuge" und wird von ShellForm bedient.
    ///
    /// Wichtige Eigenschaft: Die Pruefung veraendert NICHTS. Sie liest nur.
    /// Erst "Beheben" greift ein, und nur nach ausdruecklichem Klick.
    /// </summary>
    public partial class ShellForm
    {
        Thread _flowThread;
        volatile bool _flowCancel;
        List<Diagnostics.Check> _lastChecks = new List<Diagnostics.Check>();

        // Befund der Windows-Dateien aus DISM/SFC. Getrennt gefuehrt, weil er nicht aus
        // Diagnostics kommt, sondern aus der Ausgabe der beiden Werkzeuge.
        string _filesState = Diagnostics.Unknown;
        string _filesSummary;
        string _filesAdvice;

        bool FlowRunning { get { return _flowThread != null && _flowThread.IsAlive; } }

        /// <summary>
        /// Laeuft irgendwo schon etwas? Der Hauptweg, die beiden Suchlaeufe und der
        /// CommandRunner (Werkzeugkasten, Warteschlange, geplante Wartung) greifen auf
        /// denselben PC zu - zwei DISM-Instanzen auf demselben Windows-Abbild waeren fatal.
        ///
        /// ScanRunning gehoert mit hinein: sonst kann der Nutzer waehrend eines laufenden
        /// Aufraeumens hierher wechseln, dort "Abbrechen" druecken - und das Abbrechen
        /// trifft ueber CancelScan den Suchlauf, den er gar nicht gemeint hat.
        /// </summary>
        bool EtwasLaeuft
        {
            get { return FlowRunning || ScanRunning || (_runner != null && _runner.Running); }
        }

        /// <summary>
        /// Meldet true, wenn jetzt NICHT gestartet werden darf - und sagt der Oberflaeche
        /// dann auch, warum.
        ///
        /// Hier stand frueher ein blosses "return". Das war der Fehler vom 22.08.2026:
        /// startFlow() in app.js zeigt den Ablauf-Bildschirm, BEVOR es startCheck/startFix
        /// sendet. Lief in dem Moment die geplante Wartung, verwarf der Host den Klick
        /// lautlos - kein Protokolleintrag, keine Meldung. Der Nutzer sass danach vor
        /// einem Fortschrittsbalken, den die Wartung noch auf 70 Prozent hochzog und der
        /// dort fuer immer stehenblieb. Eine stille Ablehnung ist bei einer Oberflaeche,
        /// die vorher umschaltet, immer eine Sackgasse.
        /// </summary>
        bool StartAbgelehnt(string was)
        {
            string grund;
            if (FlowRunning)
                grund = "Es läuft bereits eine Prüfung oder Reparatur.";
            else if (ScanRunning)
                grund = "Es wird gerade nach Speicherfressern oder ungültigen Einträgen gesucht.";
            else if (_runner != null && _runner.Running)
                grund = "Gerade läuft: " +
                        (string.IsNullOrEmpty(_runner.Title) ? "eine andere Aufgabe" : _runner.Title) + ".";
            else
                return false;

            AppLog.Info(was + " nicht gestartet: " + grund);
            UiPost(new { type = "flowBusy", message = grund });
            return true;
        }

        // ---------------------------------------------------------------- Pruefen

        void StartCheck()
        {
            if (StartAbgelehnt("Prüfung")) return;
            _flowCancel = false;
            _filesState = Diagnostics.Unknown;
            _filesSummary = null;
            _filesAdvice = null;

            AppLog.Info("Pruefung gestartet.");
            _flowThread = new Thread(CheckWorker) { IsBackground = true };
            _flowThread.Start();
        }

        void CheckWorker()
        {
            try
            {
                UiPost(new { type = "flowStart", mode = "check", total = 3 });

                // --- Schritt 1: lesende Bestandsaufnahme -------------------------------
                FlowStep(1, "Speicherplatz und Systemzustand werden gelesen");
                _lastChecks = Diagnostics.RunAll(label => UiPost(new { type = "flowDetail", text = label + " wird geprüft" }));
                if (_flowCancel) { FlowCancelled(); return; }

                // --- Schritt 2: Windows-Bausteine (lesend) ----------------------------
                FlowStep(2, "Die Grundbestandteile von Windows werden geprüft");
                var dism = RunProbe("DISM.exe", "/Online /Cleanup-Image /ScanHealth", 20 * 60 * 1000);
                if (_flowCancel) { FlowCancelled(); return; }

                // --- Schritt 3: Windows-Dateien (lesend) ------------------------------
                FlowStep(3, "Die Dateien von Windows werden auf Beschädigungen geprüft");
                var sfc = RunProbe("sfc.exe", "/verifyonly", 30 * 60 * 1000, Encoding.Unicode);
                if (_flowCancel) { FlowCancelled(); return; }

                EvaluateFiles(dism, sfc);
                PublishResult("check");
            }
            catch (Exception ex)
            {
                AppLog.Error("Pruefung fehlgeschlagen", ex);
                _pendingPost = "none";   // nach einem Fehlschlag nichts abschalten
                UiPost(new { type = "flowError",
                             message = "Die Prüfung konnte nicht abgeschlossen werden. Bitte starten Sie den PC neu und versuchen Sie es noch einmal." });
            }
        }

        // ---------------------------------------------------------------- Beheben

        void StartFix()
        {
            if (StartAbgelehnt("Reparatur")) return;
            _flowCancel = false;

            AppLog.Info("Reparatur gestartet.");
            _flowThread = new Thread(FixWorker) { IsBackground = true };
            _flowThread.Start();
        }

        void FixWorker()
        {
            try
            {
                bool space = NeedsAttention("space");
                int total = 3 + (space ? 1 : 0);
                UiPost(new { type = "flowStart", mode = "fix", total = total });

                // Sicherungspunkt zuerst. Rueckgaengig schlaegt Rueckfrage: lieber ein Netz
                // spannen, als den Nutzer mit einer weiteren Sicherheitsfrage aufzuhalten.
                FlowStep(1, "Ein Sicherungspunkt wird angelegt");
                RunProbe("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"" +
                    "try { Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' " +
                    "-Name 'SystemRestorePointCreationFrequency' -Value 0 -EA SilentlyContinue; " +
                    "Checkpoint-Computer -Description 'Vor der Wartung' -RestorePointType MODIFY_SETTINGS -EA Stop } catch { }\"",
                    5 * 60 * 1000);
                if (_flowCancel) { FlowCancelled(); return; }

                FlowStep(2, "Fehlende Windows-Bausteine werden neu geholt");
                var dism = RunProbe("DISM.exe", "/Online /Cleanup-Image /RestoreHealth", 40 * 60 * 1000);
                if (_flowCancel) { FlowCancelled(); return; }

                FlowStep(3, "Beschädigte Windows-Dateien werden ersetzt");
                var sfc = RunProbe("sfc.exe", "/scannow", 40 * 60 * 1000, Encoding.Unicode);
                if (_flowCancel) { FlowCancelled(); return; }

                if (space)
                {
                    FlowStep(4, "Datenmüll wird entfernt");
                    RunProbe("powershell.exe",
                        "-NoProfile -ExecutionPolicy Bypass -Command \"" +
                        "$t=@($env:TEMP,(Join-Path $env:WINDIR 'Temp')); " +
                        "$b=(Get-ChildItem $t -Recurse -File -Force -EA SilentlyContinue | Measure-Object Length -Sum).Sum; " +
                        "Get-ChildItem $t -Force -EA SilentlyContinue | Remove-Item -Recurse -Force -EA SilentlyContinue; " +
                        "$a=(Get-ChildItem $t -Recurse -File -Force -EA SilentlyContinue | Measure-Object Length -Sum).Sum; " +
                        "'Aufgeraeumt: ' + [math]::Round((([double]$b-[double]$a)/1GB),1) + ' GB'\"",
                        15 * 60 * 1000);
                    if (_flowCancel) { FlowCancelled(); return; }
                }

                EvaluateFiles(dism, sfc);

                // Nach dem Eingriff die lesenden Pruefungen wiederholen, damit das Ergebnis
                // den Zustand NACH der Reparatur zeigt und nicht den davor.
                UiPost(new { type = "flowDetail", text = "Das Ergebnis wird zusammengestellt" });
                _lastChecks = Diagnostics.RunAll(null);

                PublishResult("fix");
            }
            catch (Exception ex)
            {
                AppLog.Error("Reparatur fehlgeschlagen", ex);
                _pendingPost = "none";   // nach einem Fehlschlag nichts abschalten
                UiPost(new { type = "flowError",
                             message = "Die Reparatur konnte nicht abgeschlossen werden. Bitte starten Sie den PC neu und versuchen Sie es noch einmal." });
            }
        }

        // ---------------------------------------------------------------- Ablaufhilfen

        void FlowStep(int index, string label)
        {
            UiPost(new { type = "flowStep", index = index, label = label });
        }

        void FlowCancelled()
        {
            AppLog.Info("Ablauf abgebrochen.");
            // Wer abbricht, will erst recht nicht, dass der PC gleich ausgeht.
            _pendingPost = "none";
            UiPost(new { type = "flowCancelled" });
        }

        /// <summary>
        /// Fuehrt aus, was der Nutzer fuer "wenn alles fertig ist" gewaehlt hat.
        ///
        /// Bis 7.2.0 gab es das nur im Werkzeugkasten: Wer den Hauptweg benutzt hat - also
        /// genau die Zielgruppe -, konnte den PC nicht nach der Reparatur abschalten
        /// lassen, obwohl der Lauf zehn Minuten und laenger dauert. Ausgerechnet dort ist
        /// der Wunsch am groessten.
        ///
        /// Der Countdown laeuft im Oberflaechen-Thread an, weil er dem Nutzer ein Banner
        /// zum Abbrechen anzeigt.
        /// </summary>
        void NachlaufStarten()
        {
            if (_pendingPost == "none") return;
            if (_web == null || !_web.IsHandleCreated) return;
            try
            {
                _web.BeginInvoke((Action)delegate
                {
                    ScheduleShutdown();
                    _pendingPost = "none";
                });
            }
            catch (Exception ex) { AppLog.Warn("Nachlauf liess sich nicht starten: " + ex.Message); }
        }

        public void CancelFlow()
        {
            if (!FlowRunning) return;
            _flowCancel = true;
            int pid = _flowPid;
            if (pid > 0) Shell.KillTree(pid);
        }

        /// <summary>
        /// „Abbrechen“ gedrueckt, obwohl gar nichts (mehr) lief. Alle drei Abbrecher
        /// (_runner.Cancel, CancelFlow, CancelScan) steigen in dem Fall still aus - der
        /// Knopf war damit tot, und wer auf dem Ablauf-Bildschirm festhing, kam ohne
        /// Neustart der App nicht mehr weg. Jetzt kommt immer eine Antwort zurueck.
        /// </summary>
        void FlowIdle()
        {
            AppLog.Info("Abbrechen gedrückt, es lief nichts mehr - Oberfläche zurückgesetzt.");
            UiPost(new { type = "flowIdle" });
        }

        volatile int _flowPid;

        /// <summary>
        /// Fuehrt einen Schritt aus und meldet dabei echten Fortschritt. DISM und SFC
        /// schreiben ihre Prozentzahl mit Wagenruecklauf auf EINE Zeile - deshalb wird
        /// zeichenweise gelesen (dieselbe Mechanik wie im CommandRunner).
        /// </summary>
        string RunProbe(string file, string args, int timeoutMs, Encoding enc = null)
        {
            var sb = new StringBuilder();
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.StandardOutputEncoding = enc ?? OemEncoding();
                psi.StandardErrorEncoding = psi.StandardOutputEncoding;

                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    _flowPid = p.Id;
                    p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) sb.AppendLine(e.Data); };
                    p.BeginErrorReadLine();

                    var line = new StringBuilder();
                    int lastPct = -1;
                    int ch;
                    var rdr = p.StandardOutput;
                    while ((ch = rdr.Read()) >= 0)
                    {
                        if (_flowCancel) break;
                        char c = (char)ch;
                        if (c == '\r' || c == '\n')
                        {
                            if (line.Length > 0)
                            {
                                string t = line.ToString();
                                int pct = ParsePct(t);
                                if (pct >= 0)
                                {
                                    if (pct != lastPct) { lastPct = pct; UiPost(new { type = "flowPercent", percent = pct }); }
                                }
                                else if (t.Trim().Length > 0)
                                {
                                    sb.AppendLine(t.Trim());
                                    Log(t.Trim(), LogKind.Normal);   // Rohausgabe fuer den Detailbereich
                                }
                                line.Length = 0;
                            }
                        }
                        else line.Append(c);
                    }

                    if (!p.WaitForExit(timeoutMs))
                    {
                        AppLog.Warn("Zeitlimit im Ablauf: " + file);
                        Shell.KillTree(p.Id);
                    }
                    _flowPid = 0;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Ablaufschritt " + file, ex);
            }
            return sb.ToString();
        }

        static int ParsePct(string line)
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"(\d{1,3})([.,]\d+)?\s*%");
            if (!m.Success) return -1;
            int v;
            if (!int.TryParse(m.Groups[1].Value, out v)) return -1;
            return (v < 0 || v > 100) ? -1 : v;
        }

        /// <summary>
        /// Deutet die Ausgaben von DISM und SFC. Bewusst nur anhand der offiziellen
        /// Meldungstexte (siehe Explain) - unbekannte Ausgaben werden NICHT gedeutet.
        /// </summary>
        void EvaluateFiles(string dismOut, string sfcOut)
        {
            bool good;
            string sfcTxt = Explain.ForOutput("sfc.exe", sfcOut ?? "", out good);
            if (sfcTxt != null)
            {
                if (good)
                {
                    _filesState = Diagnostics.Ok;
                    _filesSummary = (sfcOut ?? "").ToLowerInvariant().Contains("erfolgreich repariert")
                        ? "Beschädigte Windows-Dateien wurden gefunden und repariert."
                        : "Alle geschützten Windows-Dateien sind in Ordnung.";
                    if (_filesSummary.StartsWith("Beschädigte"))
                        _filesAdvice = "Starten Sie den PC einmal neu, damit die Reparatur vollständig abgeschlossen ist.";
                }
                else
                {
                    _filesState = Diagnostics.Bad;
                    _filesSummary = "Es wurden beschädigte Windows-Dateien gefunden, die nicht alle repariert werden konnten.";
                    _filesAdvice = "Starten Sie den PC neu und lassen Sie die Reparatur noch einmal laufen. Bleibt der Befund, hilft eine Windows-Reparaturinstallation.";
                }
                return;
            }

            string dismTxt = Explain.ForOutput("DISM.exe", dismOut ?? "", out good);
            if (dismTxt != null)
            {
                if (good)
                {
                    _filesState = Diagnostics.Ok;
                    _filesSummary = "Die Grundbestandteile von Windows sind unbeschädigt.";
                }
                else
                {
                    _filesState = Diagnostics.Warn;
                    _filesSummary = "An den Grundbestandteilen von Windows wurden Beschädigungen gefunden.";
                    _filesAdvice = "Lassen Sie die Reparatur laufen. Sie holt die fehlenden Bestandteile über das Internet neu.";
                }
                return;
            }

            _filesState = Diagnostics.Unknown;
            _filesSummary = "Der Zustand der Windows-Dateien ließ sich nicht eindeutig bestimmen.";
        }

        bool NeedsAttention(string key)
        {
            var c = _lastChecks.FirstOrDefault(x => x.Key == key);
            return c != null && (c.State == Diagnostics.Bad || c.State == Diagnostics.Warn);
        }

        /// <summary>Stellt das Ergebnis zusammen und schickt es an die Oberflaeche.</summary>
        void PublishResult(string mode)
        {
            var all = new List<Diagnostics.Check>();
            all.Add(new Diagnostics.Check
            {
                Key = "files",
                Title = "Windows-Dateien",
                State = _filesState,
                Summary = _filesSummary ?? "Der Zustand ließ sich nicht bestimmen.",
                Advice = _filesAdvice,
                CanFix = _filesState == Diagnostics.Warn || _filesState == Diagnostics.Bad,
            });
            all.AddRange(_lastChecks);

            string overall = Diagnostics.Overall(all);
            int problems = Diagnostics.CountNotOk(all);
            bool fixable = all.Any(c => c.CanFix && (c.State == Diagnostics.Bad || c.State == Diagnostics.Warn));

            // Braucht der PC einen Neustart, damit das Erledigte wirksam wird?
            bool restart = all.Any(c => c.Advice != null &&
                                        c.Advice.IndexOf("neu", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                        c.Advice.IndexOf("starten", StringComparison.OrdinalIgnoreCase) >= 0);

            History.Add(mode == "fix" ? "Wartung" : "Prüfung",
                        overall == Diagnostics.Ok ? "good" : (overall == Diagnostics.Bad ? "bad" : "warn"),
                        problems == 0 ? "Ohne Befund" : problems + " Punkt(e) gefunden", 0);

            AppLog.Info("Ablauf " + mode + " beendet, Gesamturteil " + overall + ", " + problems + " Befund(e).");

            UiPost(new
            {
                type = "flowResult",
                mode = mode,
                overall = overall,
                problems = problems,
                fixable = fixable,
                restart = restart,
                when = DateTime.Now.ToString("d. MMMM yyyy 'um' HH:mm 'Uhr'",
                            new System.Globalization.CultureInfo("de-DE")),
                checks = all.Select(c => c.ToJson()).ToArray(),
            });

            NachlaufStarten();
        }

        /// <summary>Der aktuelle Befund fuer die Startansicht (ohne neue Pruefung).</summary>
        void SendLastChecks()
        {
            UiPost(new
            {
                type = "lastChecks",
                checks = _lastChecks.Select(c => c.ToJson()).ToArray(),
            });
        }

        /// <summary>
        /// Schneller, rein lesender Erstbefund beim Start. Bewusst OHNE DISM und SFC:
        /// die dauern Minuten. Der Nutzer soll sofort etwas Echtes sehen, statt eine
        /// leere Werkzeugliste - aber ohne den Start auszubremsen.
        /// </summary>
        void StartQuickGlance()
        {
            var t = new Thread(() =>
            {
                try
                {
                    _lastChecks = Diagnostics.RunAll(null);
                    SendLastChecks();
                }
                catch (Exception ex) { AppLog.Error("Erstbefund", ex); }
            })
            { IsBackground = true };
            t.Start();
        }
    }
}
