using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;

namespace WartungsToolbox
{
    /// <summary>
    /// Der Hauptweg der App in v8: "PC prüfen", "Windows-Dateien tief prüfen", "Beheben".
    ///
    /// Prüfen = Sammler (Systembild, im eigenen Prozess, ohne PowerShell) + Regeln (Befunde).
    /// Das dauert Sekunden, nicht Minuten, und verändert nichts. Die Tiefenprüfung (DISM und
    /// SFC, lesend) ist ein eigener Schritt, den der Nutzer bewusst startet, weil sie Minuten
    /// dauert. "Beheben" gibt es nur noch für das, was ein Befund benennt: in Meilenstein 1
    /// sind das beschädigte Windows-Dateien aus der Tiefenprüfung.
    ///
    /// Zwei Regeln aus v7.3.2 gelten weiter: nie still ablehnen (StartAbgelehnt antwortet
    /// mit Grund), und jeder Lauf protokolliert Anfang und Ende.
    /// </summary>
    public partial class ShellForm
    {
        Thread _flowThread;
        Thread _glanceThread;
        volatile bool _flowCancel;

        // Das zuletzt aufgenommene Systembild mit Befunden. Antworten auf Fragen laufen die
        // Regeln erneut darüber, ohne neu zu messen.
        Systembild _bild;
        List<BereichErgebnis> _ergebnisse = new List<BereichErgebnis>();
        Entscheidungen _entscheidungen;
        Protokoll _protokoll;

        // Befund der Windows-Dateien aus der Tiefenprüfung (DISM/SFC). Getrennt geführt, weil
        // er nicht aus dem Systembild kommt, sondern aus der Ausgabe der beiden Werkzeuge.
        string _filesState = Zustand.Unknown;
        string _filesSummary;
        string _filesAdvice;
        bool _filesGeprueft;
        bool _filesNeustart;     // nur der Zweig "gefunden und repariert" setzt das

        /// <summary>Protokolle aelter als 90 Tage werden beim Start entfernt (ein Lauf, eine Datei).</summary>
        public const int ProtokollTage = 90;

        bool FlowRunning { get { return _flowThread != null && _flowThread.IsAlive; } }

        /// <summary>
        /// Läuft irgendwo schon etwas? Der Hauptweg, die beiden Suchläufe und der
        /// CommandRunner (Werkzeugkasten, Warteschlange, geplante Wartung) greifen auf
        /// denselben PC zu; zwei DISM-Instanzen auf demselben Windows-Abbild wären fatal.
        /// </summary>
        bool EtwasLaeuft
        {
            get { return FlowRunning || ScanRunning || (_runner != null && _runner.Running); }
        }

        /// <summary>
        /// Meldet true, wenn jetzt NICHT gestartet werden darf, und sagt der Oberfläche
        /// dann auch, warum. Ein bloßes return stand hier bis 7.3.2: die Oberfläche hatte
        /// schon umgeschaltet, der Klick verschwand lautlos, der Balken stand für immer.
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

        Entscheidungen Entscheidungen
        {
            get { return _entscheidungen ?? (_entscheidungen = Entscheidungen.Laden()); }
        }

        // ---------------------------------------------------------------- Prüfen

        void StartCheck()
        {
            if (StartAbgelehnt("Prüfung")) return;
            _flowCancel = false;
            AppLog.Info("Pruefung gestartet.");
            _flowThread = new Thread(CheckWorker) { IsBackground = true, Name = "pruefen" };
            _flowThread.Start();
        }

        void CheckWorker()
        {
            try
            {
                UiPost(new { type = "flowStart", mode = "check", total = 2 });
                FlowStep(1, "Ihr PC wird gelesen");
                ErstbefundAbwarten();
                Messen();
                if (_flowCancel) { FlowCancelled(); return; }

                FlowStep(2, "Die Messwerte werden bewertet");
                Bewerten();
                PublishResult("check");
            }
            catch (Exception ex)
            {
                AppLog.Error("Pruefung fehlgeschlagen", ex);
                _pendingPost = "none";
                UiPost(new { type = "flowError",
                             message = "Die Prüfung konnte nicht abgeschlossen werden. Bitte starten Sie den PC neu und versuchen Sie es noch einmal." });
            }
        }

        /// <summary>
        /// Der Erstbefund beim Start und ein Klick auf „PC jetzt prüfen“ in denselben Sekunden
        /// würden sonst gleichzeitig in _bild, _ergebnisse und _protokoll schreiben. Der Lauf
        /// wartet den Erstbefund ab (höchstens zwei Minuten) und misst dann frisch.
        /// </summary>
        void ErstbefundAbwarten()
        {
            var g = _glanceThread;
            if (g == null || !g.IsAlive) return;
            UiPost(new { type = "flowDetail", text = "Der erste Blick wird abgeschlossen" });
            g.Join(120 * 1000);
        }

        /// <summary>Systembild aufnehmen. Läuft im Hintergrund-Thread.</summary>
        void Messen()
        {
            _protokoll = new Protokoll(Protokoll.NeueLaufId());
            // Konnte ProgramData nicht beschrieben werden, liegen Entscheidungen und Protokoll im
            // Nutzerprofil (nicht maschinenweit). Das steht im Protokoll, nicht nur im Ordnernamen.
            if (Ablage.IstAusweichordner())
                _protokoll.Schreibe(Protokoll.Host, Protokoll.FehlerArt, "ablage",
                    "Ablage im Nutzerprofil statt in ProgramData (kein Schreibrecht); Entscheidungen gelten nur für dieses Konto",
                    new { ordner = Ablage.Maschinenweit() });
            if (Entscheidungen.LadeFehler != null)
                _protokoll.Schreibe(Protokoll.Host, Protokoll.FehlerArt, "entscheidungen",
                    "Die gespeicherten Antworten ließen sich nicht lesen; es gilt ein leerer Speicher", new { grund = Entscheidungen.LadeFehler });
            _protokoll.Schreibe(Protokoll.Host, Protokoll.Anfang, "pruefen", "Prüfung gestartet",
                new { version = typeof(ShellForm).Assembly.GetName().Version.ToString(3), erhoeht = Sammler.Quellen.Rechte.Erhoeht() });
            var sammler = new Sammler.Sammler(was => UiPost(new { type = "flowDetail", text = was + " wird gelesen" }), _protokoll);
            _bild = sammler.Erfassen();
            foreach (string z in sammler.Zeiten) Log(z, LogKind.Dim);
            foreach (var f in _bild.Fehlerliste)
                Log("Quelle " + f.Quelle + ": " + f.Art + (f.Text != null ? " (" + f.Text + ")" : ""), LogKind.Dim);
        }

        /// <summary>Regeln über das Systembild laufen lassen und protokollieren.</summary>
        void Bewerten()
        {
            _ergebnisse = Alle.Pruefen(_bild, Entscheidungen);
            foreach (var b in Alle.AlleBefunde(_ergebnisse))
            {
                if (b.Zustand == Zustand.Ok && b.Frage == null) continue;
                _protokoll.Schreibe(Protokoll.Regeln, b.Frage != null ? Protokoll.FrageArt : Protokoll.BefundArt, b.Schluessel,
                    b.Titel + ": " + b.Satz,
                    new { zustand = b.Zustand, quelle = b.Quelle, messwert = b.Messwert == null ? null : b.Messwert.Wert + " " + b.Messwert.Einheit, schwelle = b.Messwert == null ? null : b.Messwert.Schwelle });
            }
        }

        // ---------------------------------------------------------------- Antworten

        /// <summary>
        /// Der Nutzer hat eine Frage beantwortet ("so lassen" oder "reparieren"). Die Antwort
        /// wird maschinenweit gemerkt, die Regeln laufen erneut über das vorhandene Systembild.
        /// </summary>
        void Antwort(string frageId, string wert)
        {
            // Nie still verwerfen: die Oberfläche hat die Knöpfe schon gesperrt und wartet.
            if (string.IsNullOrEmpty(frageId) || _bild == null
                || (wert != Entscheidungen.Absicht && wert != Entscheidungen.Reparieren))
            {
                AppLog.Warn("Antwort verworfen: " + frageId + " = " + wert + (_bild == null ? " (kein Systembild)" : ""));
                UiPost(new { type = "flowError", message = "Die Antwort ließ sich keiner Prüfung zuordnen. Bitte prüfen Sie den PC noch einmal und antworten Sie dann." });
                return;
            }
            if (StartAbgelehnt("Antwort")) return;

            try
            {
                Entscheidungen.Setze(frageId, wert, Zeit.Utc(DateTime.UtcNow));
                Entscheidungen.Speichern();
                if (_protokoll != null)
                    _protokoll.Schreibe(Protokoll.Host, Protokoll.AntwortArt, frageId,
                        wert == Entscheidungen.Absicht ? "Sie haben bestätigt: so gewollt" : "Sie möchten das reparieren lassen", new { wert });
                AppLog.Info("Antwort gemerkt: " + frageId + " = " + wert);
                _ergebnisse = Alle.Pruefen(_bild, Entscheidungen);
                PublishResult("answer");
            }
            catch (Exception ex)
            {
                AppLog.Error("Antwort speichern", ex);
                UiPost(new { type = "flowError", message = "Ihre Antwort ließ sich nicht speichern. Bitte versuchen Sie es noch einmal." });
            }
        }

        // ---------------------------------------------------------------- Tiefenprüfung

        void StartDeepCheck()
        {
            if (StartAbgelehnt("Tiefenprüfung")) return;
            _flowCancel = false;
            _filesState = Zustand.Unknown;
            _filesSummary = null;
            _filesAdvice = null;
            AppLog.Info("Tiefenpruefung gestartet.");
            _flowThread = new Thread(DeepWorker) { IsBackground = true, Name = "tiefenpruefung" };
            _flowThread.Start();
        }

        void DeepWorker()
        {
            try
            {
                if (_protokoll == null) _protokoll = new Protokoll(Protokoll.NeueLaufId());
                _protokoll.Schreibe(Protokoll.Host, Protokoll.Anfang, "tiefenpruefung", "Tiefenprüfung der Windows-Dateien gestartet");
                UiPost(new { type = "flowStart", mode = "deep", total = 2 });

                FlowStep(1, "Die Grundbestandteile von Windows werden geprüft");
                var dism = RunProbe("DISM.exe", "/Online /Cleanup-Image /ScanHealth", 20 * 60 * 1000);
                if (_flowCancel) { FlowCancelled(); return; }

                FlowStep(2, "Die Dateien von Windows werden auf Beschädigungen geprüft");
                var sfc = RunProbe("sfc.exe", "/verifyonly", 30 * 60 * 1000, Encoding.Unicode);
                if (_flowCancel) { FlowCancelled(); return; }

                EvaluateFiles(dism, sfc);
                _filesGeprueft = true;
                _protokoll.Schreibe(Protokoll.Regeln, Protokoll.BefundArt, "windows.dateien", _filesSummary, new { zustand = _filesState });
                ErstbefundAbwarten();
                if (_bild == null) { Messen(); }
                Bewerten();
                PublishResult("deep");
            }
            catch (Exception ex)
            {
                AppLog.Error("Tiefenpruefung fehlgeschlagen", ex);
                _pendingPost = "none";
                UiPost(new { type = "flowError",
                             message = "Die Tiefenprüfung konnte nicht abgeschlossen werden. Bitte starten Sie den PC neu und versuchen Sie es noch einmal." });
            }
        }

        // ---------------------------------------------------------------- Beheben

        void StartFix()
        {
            if (StartAbgelehnt("Reparatur")) return;
            // Grundsatz 4: keine Reparatur ohne Befund. Beheben gibt es nur, wenn die
            // Tiefenprüfung beschädigte Windows-Dateien gefunden hat.
            if (!(_filesState == Zustand.Bad || _filesState == Zustand.Warn))
            {
                AppLog.Info("Reparatur nicht gestartet: kein Dateibefund (Zustand " + _filesState + ", geprüft: " + _filesGeprueft + ").");
                UiPost(new { type = "flowNichts", message = _filesGeprueft
                    ? "Die Tiefenprüfung hat keine beschädigten Windows-Dateien gefunden. Eine Reparatur würde nichts verändern."
                    : "Die Windows-Dateien wurden noch nicht tief geprüft. Erst die Tiefenprüfung sagt, ob es etwas zu beheben gibt." });
                return;
            }
            _flowCancel = false;
            _filesSummary = null;
            _filesAdvice = null;
            _filesNeustart = false;
            AppLog.Info("Reparatur gestartet.");
            _flowThread = new Thread(FixWorker) { IsBackground = true, Name = "beheben" };
            _flowThread.Start();
        }

        void FixWorker()
        {
            try
            {
                if (_protokoll == null) _protokoll = new Protokoll(Protokoll.NeueLaufId());
                _protokoll.Schreibe(Protokoll.Host, Protokoll.Anfang, "beheben", "Reparatur der Windows-Dateien gestartet");
                UiPost(new { type = "flowStart", mode = "fix", total = 4 });

                // Sicherungspunkt zuerst (Grundsatz 2). Erfolg wird an der Folgenummer
                // gemessen, nicht am Rückgabewert: CreateRestorePoint meldet S_OK auch dann,
                // wenn die 24-Stunden-Drossel nichts anlegt (Widerlegungsrunde).
                FlowStep(1, "Ein Sicherungspunkt wird angelegt");
                int? vorher = LetzteSicherungsnummer();
                RunProbe("powershell.exe",
                    "-NoProfile -Command \"" +
                    "try { Checkpoint-Computer -Description 'Vor der Wartung' -RestorePointType MODIFY_SETTINGS -EA Stop } catch { }\"",
                    5 * 60 * 1000);
                int? nachher = LetzteSicherungsnummer();
                bool punktAngelegt = vorher.HasValue && nachher.HasValue && nachher.Value > vorher.Value;
                // Drei Faelle, drei Saetze: neuer Punkt; kein neuer, aber ein vorhandener (Drossel);
                // gar keiner (Systemschutz aus oder nicht lesbar). Der dritte darf nicht wie der zweite klingen.
                bool einerVorhanden = nachher.HasValue && nachher.Value > 0;
                string sicherungSatz = punktAngelegt ? "Wiederherstellungspunkt Nr. " + nachher + " angelegt"
                    : einerVorhanden ? "Kein neuer Wiederherstellungspunkt (Windows legt höchstens einen je 24 Stunden an); der vorhandene Nr. " + nachher + " gilt"
                    : "Kein Wiederherstellungspunkt vorhanden (Systemschutz aus oder nicht lesbar); die Reparatur läuft ohne Rückweg über einen Punkt";
                _protokoll.Schreibe(Protokoll.Helfer, Protokoll.Sicherung, "wiederherstellungspunkt", sicherungSatz, new { vorher, nachher });
                Log(sicherungSatz + ".", punktAngelegt ? LogKind.Good : LogKind.Warn);
                if (!einerVorhanden) UiPost(new { type = "flowDetail", text = "Kein Wiederherstellungspunkt möglich, Systemschutz prüfen" });
                if (_flowCancel) { FlowCancelled(); return; }

                FlowStep(2, "Fehlende Windows-Bausteine werden neu geholt");
                var dism = RunProbe("DISM.exe", "/Online /Cleanup-Image /RestoreHealth", 40 * 60 * 1000);
                if (_flowCancel) { FlowCancelled(); return; }

                FlowStep(3, "Beschädigte Windows-Dateien werden ersetzt");
                var sfc = RunProbe("sfc.exe", "/scannow", 40 * 60 * 1000, Encoding.Unicode);
                if (_flowCancel) { FlowCancelled(); return; }

                EvaluateFiles(dism, sfc);
                _protokoll.Schreibe(Protokoll.Helfer, Protokoll.Nachweis, "windows.dateien", _filesSummary, new { zustand = _filesState });

                // Nach dem Eingriff neu messen, damit das Ergebnis den Zustand DANACH zeigt.
                FlowStep(4, "Das Ergebnis wird zusammengestellt");
                Messen();
                Bewerten();
                PublishResult("fix");
            }
            catch (Exception ex)
            {
                AppLog.Error("Reparatur fehlgeschlagen", ex);
                _pendingPost = "none";
                UiPost(new { type = "flowError",
                             message = "Die Reparatur konnte nicht abgeschlossen werden. Bitte starten Sie den PC neu und versuchen Sie es noch einmal." });
            }
        }

        /// <summary>Höchste Folgenummer der Wiederherstellungspunkte (nur erhöht lesbar), sonst null.</summary>
        static int? LetzteSicherungsnummer()
        {
            try
            {
                int max = -1;
                foreach (var mo in Sammler.Quellen.Wmi.Abfrage(@"root\default", "SELECT SequenceNumber FROM SystemRestore", 10000))
                {
                    var n = Sammler.Quellen.Wmi.Ganz(mo, "SequenceNumber");
                    if (n.HasValue && n.Value > max) max = n.Value;
                }
                return max < 0 ? 0 : max;
            }
            catch (Exception) { return null; }
        }

        // ---------------------------------------------------------------- Ablaufhilfen

        void FlowStep(int index, string label)
        {
            UiPost(new { type = "flowStep", index = index, label = label });
        }

        void FlowCancelled()
        {
            AppLog.Info("Ablauf abgebrochen.");
            if (_protokoll != null) _protokoll.Schreibe(Protokoll.Host, Protokoll.Ende, "abgebrochen", "Abgebrochen. Ihrem PC ist nichts passiert.");
            // Wer abbricht, will erst recht nicht, dass der PC gleich ausgeht.
            _pendingPost = "none";
            UiPost(new { type = "flowCancelled" });
        }

        /// <summary>
        /// Führt aus, was der Nutzer für "wenn alles fertig ist" gewählt hat. Der Countdown
        /// läuft im Oberflächen-Thread an, weil er dem Nutzer ein Banner zum Abbrechen zeigt.
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

        /// <summary>„Abbrechen“ gedrückt, obwohl nichts (mehr) lief: immer eine Antwort.</summary>
        void FlowIdle()
        {
            AppLog.Info("Abbrechen gedrückt, es lief nichts mehr - Oberfläche zurückgesetzt.");
            UiPost(new { type = "flowIdle" });
        }

        volatile int _flowPid;

        /// <summary>
        /// Führt ein Windows-Werkzeug (DISM, SFC) aus und meldet dabei echten Fortschritt.
        /// Beide schreiben ihre Prozentzahl mit Wagenrücklauf auf EINE Zeile, deshalb wird
        /// zeichenweise gelesen. Direkter Prozessstart, keine Shell dazwischen.
        /// </summary>
        string RunProbe(string file, string args, int timeoutMs, Encoding enc = null)
        {
            var sb = new StringBuilder();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int exit = -1;
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
                    // Wachhund: das zeichenweise Read() unten blockiert VOR dem WaitForExit und
                    // sieht dessen Zeitgrenze nie (Lehre aus 7.3.2, DESKTOP-APPS/GOTCHAS). Am
                    // 12.09.2026 stand sfc /verifyonly 35 Minuten hinter einem beschaeftigten
                    // TrustedInstaller, und die Tiefenpruefung endete nie. Der Timer toetet den Baum.
                    int pidFuerWachhund = p.Id;
                    bool zeitAbgelaufen = false;
                    using (var wachhund = new System.Threading.Timer(_ =>
                    {
                        zeitAbgelaufen = true;
                        AppLog.Warn("Zeitlimit im Ablauf (" + (timeoutMs / 60000) + " min): " + file + " wird beendet.");
                        Shell.KillTree(pidFuerWachhund);
                    }, null, timeoutMs, System.Threading.Timeout.Infinite))
                    {
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
                                    Log(t.Trim(), LogKind.Normal);   // Rohausgabe für den Detailbereich
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
                    }   // Wachhund
                    try { exit = p.ExitCode; } catch (Exception) { }
                    if (zeitAbgelaufen) { sb.AppendLine("Zeitlimit: " + file + " wurde nach " + (timeoutMs / 60000) + " Minuten beendet."); exit = -2; }
                    _flowPid = 0;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Ablaufschritt " + file, ex);
            }
            if (_protokoll != null)
                _protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, file.ToLowerInvariant(),
                    file + " " + args + " ist beendet", new { exit, sekunden = (int)sw.Elapsed.TotalSeconds });
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
        /// Deutet die Ausgaben von DISM und SFC anhand der offiziellen Meldungstexte (Explain).
        /// Unbekannte Ausgaben werden NICHT gedeutet. Das bleibt die einzige Stelle im
        /// Hauptweg, die Text liest; sie wird in Meilenstein 4 durch DISM-API und CBS.log ersetzt.
        /// </summary>
        void EvaluateFiles(string dismOut, string sfcOut)
        {
            _filesSummary = null;
            _filesAdvice = null;
            _filesNeustart = false;

            // Beide Deutungen getrennt, dann gilt die schlechtere. Bis 12.09.2026 gewann bei
            // unbekannter SFC-Ausgabe (abgebrochen, unbekannte Meldung) die DISM-Deutung, und
            // ein Lauf ohne SFC-Ergebnis stand als "unbeschädigt" da.
            bool sfcGut;
            string sfcTxt = Explain.ForOutput("sfc.exe", sfcOut ?? "", out sfcGut);
            bool dismGut;
            string dismTxt = Explain.ForOutput("DISM.exe", dismOut ?? "", out dismGut);

            string sfcZustand = sfcTxt == null ? Zustand.Unknown : (sfcGut ? Zustand.Ok : Zustand.Bad);
            string dismZustand = dismTxt == null ? Zustand.Unknown : (dismGut ? Zustand.Ok : Zustand.Warn);
            // Unbekannt zaehlt hier wie ein eigener Rang zwischen ok und warn: ohne SFC-Ergebnis
            // gibt es kein "in Ordnung", aber auch keinen Befund.
            string zustand;
            if (sfcZustand == Zustand.Bad) zustand = Zustand.Bad;
            else if (dismZustand == Zustand.Warn) zustand = Zustand.Warn;
            else if (sfcZustand == Zustand.Unknown || dismZustand == Zustand.Unknown) zustand = Zustand.Unknown;
            else zustand = Zustand.Ok;
            _filesState = zustand;

            if (zustand == Zustand.Bad)
            {
                _filesSummary = "Es wurden beschädigte Windows-Dateien gefunden, die nicht alle repariert werden konnten.";
                _filesAdvice = "Lassen Sie die Reparatur laufen. Bleibt der Befund, hilft eine Windows-Reparaturinstallation.";
                return;
            }
            if (zustand == Zustand.Warn)
            {
                _filesSummary = "An den Grundbestandteilen von Windows wurden Beschädigungen gefunden.";
                _filesAdvice = "Lassen Sie die Reparatur laufen. Sie holt die fehlenden Bestandteile über das Internet neu.";
                return;
            }
            if (zustand == Zustand.Unknown)
            {
                _filesSummary = sfcTxt == null && dismTxt == null
                    ? "Der Zustand der Windows-Dateien ließ sich nicht eindeutig bestimmen: beide Prüfwerkzeuge lieferten kein auswertbares Ergebnis."
                    : (sfcTxt == null ? "Die Grundbestandteile sind geprüft, die Dateiprüfung (SFC) lieferte aber kein auswertbares Ergebnis."
                                      : "Die Dateien sind geprüft, die Prüfung der Grundbestandteile (DISM) lieferte aber kein auswertbares Ergebnis.");
                return;
            }
            bool repariert = sfcTxt.IndexOf("repariert", StringComparison.OrdinalIgnoreCase) >= 0;
            _filesSummary = repariert
                ? "Beschädigte Windows-Dateien wurden gefunden und repariert."
                : "Alle geschützten Windows-Dateien und die Grundbestandteile von Windows sind in Ordnung.";
            _filesNeustart = repariert;
            if (repariert)
                _filesAdvice = "Starten Sie den PC einmal neu, damit die Reparatur vollständig abgeschlossen ist.";
        }

        // ---------------------------------------------------------------- Ergebnis

        /// <summary>Eine Bereichszeile für die Oberfläche (Startseite und Ergebnis).</summary>
        object Bereichszeile(BereichErgebnis b)
        {
            var probleme = b.Befunde.Where(x => x.IstProblem).ToList();
            var fragen = b.Befunde.Where(x => x.Frage != null).ToList();
            string summary;
            if (probleme.Count > 0)
                summary = probleme.Count == 1 ? probleme[0].Satz : probleme.Count + " Punkte: " + string.Join(" · ", probleme.Select(p => p.Titel));
            else if (fragen.Count > 0)
                summary = fragen.Count == 1 ? "Eine Frage an Sie: " + fragen[0].Titel : fragen.Count + " Fragen an Sie";
            else if (!b.DatenVorhanden)
                summary = b.Fehlend.Count > 0 ? "Ließ sich nicht prüfen: " + string.Join(", ", b.Fehlend) : "Ließ sich nicht prüfen.";
            else
            {
                var info = b.Befunde.FirstOrDefault(x => x.Zustand == Zustand.Ok || x.Zustand == Zustand.Absicht);
                summary = info != null ? info.Satz : "Nichts Auffälliges gefunden.";
            }
            string advice = probleme.Select(p => p.Rat).FirstOrDefault(r => !string.IsNullOrEmpty(r));
            // Eine offene Frage ist kein Problem, aber auch nicht „in Ordnung“: die Plakette
            // sagt „Ihre Antwort“, solange der Nutzer nicht geantwortet hat.
            string state = (probleme.Count == 0 && fragen.Count > 0) ? "frage" : b.Zustand;
            return new
            {
                key = b.Bereich,
                title = Bereich.Titel(b.Bereich),
                state = state,
                summary = summary,
                advice = advice,
                detail = string.Join("\n", b.Befunde.Where(x => x.Zustand != Zustand.Ok || x.Frage != null).Select(x => x.Titel + ": " + x.Satz).Concat(b.Fehlend.Select(f => "Nicht geprüft: " + f))),
                canFix = b.Bereich == Bereich.Speicherplatz && b.Zustand != Zustand.Ok && b.Zustand != Zustand.Unknown,
                probleme = probleme.Count,
                fragen = fragen.Count,
                fehlend = b.Fehlend.ToArray(),
            };
        }

        object BefundZeile(Befund f)
        {
            return new
            {
                bereich = f.Bereich,
                bereichTitel = Bereich.Titel(f.Bereich),
                key = f.Schluessel,
                state = f.Zustand,
                title = f.Titel,
                summary = f.Satz,
                advice = f.Rat,
                messwert = f.Messwert == null ? null : new { wert = f.Messwert.Wert, einheit = f.Messwert.Einheit, schwelle = f.Messwert.Schwelle },
                quelle = f.Quelle,
                detail = f.Detail.ToArray(),
                massnahmen = f.Massnahmen.ToArray(),
                frage = f.Frage == null ? null : new { id = f.Frage.Id, text = f.Frage.Text, ja = f.Frage.Ja, nein = f.Frage.Nein },
            };
        }

        object DateiZeile()
        {
            return new
            {
                key = "files",
                title = "Windows-Dateien",
                state = _filesState,
                summary = _filesSummary ?? "Noch nicht geprüft. Die Tiefenprüfung dauert 5 bis 10 Minuten und verändert nichts.",
                advice = _filesAdvice,
                detail = (string)null,
                canFix = _filesState == Zustand.Warn || _filesState == Zustand.Bad,
                probleme = (_filesState == Zustand.Warn || _filesState == Zustand.Bad) ? 1 : 0,
                fragen = 0,
                fehlend = new string[0],
            };
        }

        /// <summary>Stellt das Ergebnis zusammen und schickt es an die Oberfläche.</summary>
        void PublishResult(string mode)
        {
            var zeilen = new List<object> { DateiZeile() };
            zeilen.AddRange(_ergebnisse.Select(Bereichszeile));

            var befunde = Alle.AlleBefunde(_ergebnisse).ToList();
            string overall = Zustand.Schlechtester(_ergebnisse.Select(x => x.Zustand).Concat(new[] { _filesGeprueft ? _filesState : Zustand.Unknown }));
            int problems = Alle.Probleme(_ergebnisse) + ((_filesState == Zustand.Warn || _filesState == Zustand.Bad) ? 1 : 0);
            int fragen = Alle.OffeneFragen(_ergebnisse).Count();
            bool fixable = _filesState == Zustand.Warn || _filesState == Zustand.Bad;

            // Braucht der PC einen Neustart? Nur aus Befunden, die es ausdrücklich sagen.
            bool restart = befunde.Any(b => b.IstProblem && b.Schluessel != null && b.Schluessel.StartsWith("update.neustart"))
                           || _filesNeustart;

            if (mode != "answer")
            {
                History.Add(mode == "fix" ? "Wartung" : (mode == "deep" ? "Tiefenprüfung" : "Prüfung"),
                            overall == Zustand.Ok ? "good" : (overall == Zustand.Bad ? "bad" : "warn"),
                            problems == 0 ? "Ohne Befund" : problems + " Punkt(e) gefunden", 0);
                AppLog.Info("Ablauf " + mode + " beendet, Gesamturteil " + overall + ", " + problems + " Befund(e), " + fragen + " Frage(n).");
                if (_protokoll != null)
                    _protokoll.Schreibe(Protokoll.Host, Protokoll.Ende, mode,
                        "Fertig: " + (problems == 0 ? "kein Problem gefunden" : problems + " Punkt(e) gefunden") + (fragen > 0 ? ", " + fragen + " Frage(n) offen" : ""),
                        new { overall, problems, fragen });
            }

            UiPost(new
            {
                type = "flowResult",
                mode = mode,
                overall = overall,
                problems = problems,
                fragen = fragen,
                fixable = fixable,
                restart = restart,
                erhoeht = _bild != null && _bild.Erhoeht,
                filesGeprueft = _filesGeprueft,
                when = DateTime.Now.ToString("d. MMMM yyyy 'um' HH:mm 'Uhr'", new System.Globalization.CultureInfo("de-DE")),
                checks = zeilen.ToArray(),
                befunde = befunde.Select(BefundZeile).ToArray(),
            });

            if (mode != "answer") NachlaufStarten();
        }

        /// <summary>Der aktuelle Befund für die Startansicht (ohne neue Prüfung).</summary>
        void SendLastChecks()
        {
            var zeilen = new List<object>();
            zeilen.AddRange(_ergebnisse.Select(Bereichszeile));
            UiPost(new { type = "lastChecks", checks = zeilen.ToArray(), fragen = Alle.OffeneFragen(_ergebnisse).Count() });
        }

        /// <summary>
        /// Schneller, rein lesender Erstbefund beim Start (Sammler + Regeln, Sekunden).
        /// Der Nutzer soll sofort etwas Echtes sehen statt einer leeren Werkzeugliste.
        /// </summary>
        void StartQuickGlance()
        {
            if (FlowRunning || (_glanceThread != null && _glanceThread.IsAlive)) return;
            Protokoll.Aufraeumen(ProtokollTage);
            var t = new Thread(() =>
            {
                try
                {
                    Messen();
                    Bewerten();
                    SendLastChecks();
                }
                catch (Exception ex)
                {
                    // Nie still: die Startseite zeigt sonst fuer immer "wird geladen".
                    AppLog.Error("Erstbefund", ex);
                    UiPost(new { type = "lastChecks", checks = new object[0], fragen = 0,
                                 fehler = "Der erste Blick ließ sich nicht erstellen. Sie können den PC trotzdem prüfen." });
                }
            })
            { IsBackground = true, Name = "erstbefund" };
            _glanceThread = t;
            t.Start();
        }

        /// <summary>Der Bericht für zwei Leser: erst Alltagssprache, dann das Fachliche, dann die Rohausgabe.</summary>
        string BerichtText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Windows-Wartung " + typeof(ShellForm).Assembly.GetName().Version.ToString(3) + " - Bericht vom " + DateTime.Now.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine();
            if (_ergebnisse.Count > 0)
            {
                sb.AppendLine("WAS GEFUNDEN WURDE");
                foreach (var b in _ergebnisse)
                {
                    sb.AppendLine();
                    sb.AppendLine("[" + b.Zustand + "] " + Bereich.Titel(b.Bereich));
                    foreach (var f in b.Befunde.Where(x => x.Zustand != Zustand.Ok || x.Frage != null))
                    {
                        sb.AppendLine("  " + f.Titel + ": " + f.Satz);
                        if (!string.IsNullOrEmpty(f.Rat)) sb.AppendLine("    Rat: " + f.Rat);
                    }
                    foreach (string fe in b.Fehlend) sb.AppendLine("  Nicht geprüft: " + fe);
                }
                if (_filesGeprueft) { sb.AppendLine(); sb.AppendLine("[" + _filesState + "] Windows-Dateien: " + _filesSummary); }
                sb.AppendLine();
                sb.AppendLine("FÜR DEN TECHNIKER");
                foreach (var f in Alle.AlleBefunde(_ergebnisse).Where(x => x.Zustand != Zustand.Ok || x.Frage != null))
                {
                    sb.AppendLine("  " + f.Schluessel + "  [" + f.Zustand + "]  Quelle: " + f.Quelle
                                  + (f.Messwert != null ? "  Messwert: " + f.Messwert.Wert + " " + f.Messwert.Einheit + (f.Messwert.Schwelle != null ? " (Schwelle " + f.Messwert.Schwelle + ")" : "") : ""));
                    foreach (string d in f.Detail) sb.AppendLine("      " + d);
                }
                if (_bild != null && _bild.Fehlerliste.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("QUELLEN OHNE DATEN");
                    foreach (var fe in _bild.Fehlerliste) sb.AppendLine("  " + fe.Quelle + " [" + fe.Art + "] " + fe.Text);
                }
            }
            if (_protokoll != null && _protokoll.Zeilen.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("PROTOKOLL (Datei: " + _protokoll.Pfad + ")");
                sb.Append(Protokoll.AlsText(_protokoll.Zeilen, true));
            }
            if (_log.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("ROHAUSGABE DER WERKZEUGE");
                sb.Append(_log.ToString());
            }
            return sb.ToString();
        }
    }
}
