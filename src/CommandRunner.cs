using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using WartungsToolbox.Kern;

namespace WartungsToolbox
{
    /// <summary>
    /// Fuehrt einen Plan (Kennungen + Parameter, kern/Plan.cs) ueber den Ausfuehrer aus und
    /// meldet Zeilen, Fortschritt, Zustand und Abschluss an die Oberflaeche - dieselben
    /// Rueckrufe wie bis 8.0. Seit 8.1 startet diese Klasse keinen Prozess mehr selbst: das
    /// tut der Helfer (helfer/Werkzeuge.cs), lokal oder in einem erhoehten Zweitprozess;
    /// HelferClient.Holen besorgt ihn und loest dabei den UAC-Dialog aus.
    ///
    /// Alle Rueckrufe kommen per BeginInvoke auf dem Thread der Oberflaeche an; der Lauf
    /// selbst sitzt in einem Hintergrund-Thread, weil Holen bis zu 60 s und ein Plan bis zu
    /// Stunden blockieren darf.
    /// </summary>
    class CommandRunner
    {
        readonly Control _ui;
        readonly Action<string, LogKind> _log;
        readonly Action<bool> _onState;
        readonly Action<string, LogKind, string, double> _onComplete;
        readonly Action<int> _onProgress;
        volatile IAusfuehrer _ausfuehrer;
        volatile bool _cancel;
        volatile bool _running;
        volatile string _title;
        int _abbruchNachgereicht;   // 0/1 je Lauf (Interlocked): der nachgereichte Abbruch geht nur einmal raus

        // Gesetzt, sobald der Hintergrund-Thread mit dem Lauf durch ist (Protokoll und app.log
        // haben ihr Ende, Done ist an die Oberflaeche uebergeben); anfangs gesetzt = nichts laeuft.
        // LaufAbwarten wartet darauf, nicht auf Running: Running faellt erst in Done auf dem
        // Thread der Oberflaeche, und der wartet gerade.
        readonly ManualResetEvent _ende = new ManualResetEvent(true);

        /// <summary>
        /// Optional: (schritt, gesamt, label) je Fortschrittsmeldung des Helfers, auf dem Thread
        /// der Oberflaeche. Das ist der Ablaufbalken des Hauptwegs (CheckFlow.FlowStep), nicht
        /// das Protokoll: aus Fortschritt entsteht hier keine Log-Zeile, die Kopfzeile je Schritt
        /// schreibt der Helfer selbst (helfer/Ausfuehrung.cs).
        /// </summary>
        public Action<int, int, string> OnStep = null;

        /// <summary>
        /// Wahr von RunPlan bis zum Ende von Done auf dem Thread der Oberflaeche. Faellt bewusst
        /// NICHT schon im Hintergrund-Thread: zwischen dem Ende des Laufs dort und der Zustellung
        /// von Done per BeginInvoke verarbeitet die Oberflaeche andere Nachrichten, und ein neuer
        /// RunPlan (Klick, geplante Wartung ueber WM_WW_RUNAUTO) haette gestartet, bevor Done des
        /// alten Laufs dessen Nachlauf, Abschalt-Wunsch und Fertigmeldung verbraucht. Der Runner
        /// gilt auf der Oberflaeche deshalb genau so lange als laufend, bis Done gelaufen ist.
        /// </summary>
        public bool Running { get { return _running; } }

        /// <summary>
        /// Titel des gerade laufenden Auftrags ("Geplante Wartung", ein Werkzeugname, ...).
        /// Der Hauptweg nennt ihn dem Nutzer, wenn er einen Start ablehnen muss: "es laeuft
        /// gerade etwas" ohne zu sagen WAS ist eine Auskunft, mit der niemand etwas anfangen
        /// kann - schon gar nicht bei einem Lauf, den der Zeitplan von selbst gestartet hat.
        /// </summary>
        public string Title { get { return _title; } }

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
            AufUi(delegate { _log(s, k); });
        }

        void Progress(int pct)
        {
            if (_onProgress == null) return;
            AufUi(delegate { _onProgress(pct); });
        }

        // Auf den Thread der Oberflaeche; ohne Fenster (Handle noch nicht da) direkt. Scheitert
        // beides, steht es im app.log: ein stiller Fehler in Done (History.Add) bliebe sonst
        // unsichtbar.
        void AufUi(Action a)
        {
            if (_ui != null && _ui.IsHandleCreated)
            {
                try { _ui.BeginInvoke(a); return; }
                catch (Exception ex) { AppLog.Warn("Rückruf an die Oberfläche (BeginInvoke) fehlgeschlagen: " + ex.Message); }
            }
            try { a(); }
            catch (Exception ex) { AppLog.Warn("Rückruf an die Oberfläche fehlgeschlagen: " + ex.Message); }
        }

        /// <summary>Laufenden Plan abbrechen: Flag setzen, Ausfuehrer benachrichtigen (Pipe: Anfrage abbrechen).</summary>
        public void Cancel()
        {
            if (!_running) return;
            _cancel = true;
            IAusfuehrer a = _ausfuehrer;
            if (a != null)
            {
                try { a.Abbrechen(); }
                catch (Exception ex) { AppLog.Warn("Abbruch: " + ex.Message); }
            }
        }

        /// <summary>
        /// Plan ausfuehren. Laeuft schon etwas, passiert nichts (der Aufrufer prueft Running
        /// vorher und nennt dem Nutzer den Title). Der UAC-Dialog kommt aus Holen, wenn noch
        /// kein Helfer verbunden ist; die Ablehnung ist ein normaler Rueckweg mit Meldung.
        /// </summary>
        public void RunPlan(string titel, Plan plan)
        {
            if (_running) return;
            _running = true;
            _title = titel;
            _cancel = false;
            _ausfuehrer = null;
            _abbruchNachgereicht = 0;
            _ende.Reset();
            // Mit der Plan-Id laesst sich das lauf-<planId>.jsonl des Helfers im app.log
            // zuordnen; das Ende schreibt Lauf ("Lauf beendet") im Hintergrund-Thread.
            int n = plan == null ? 0 : plan.Schritte.Count;
            AppLog.Info(titel + " gestartet (Plan " + (plan == null || plan.Id == null ? "?" : plan.Id) + ", "
                        + (n == 1 ? "1 Schritt" : n + " Schritte") + ")");
            _onState(true);

            var t = new Thread(delegate () { Lauf(titel, plan); });
            t.IsBackground = true;
            t.Name = "plan-lauf";
            t.Start();
        }

        void Lauf(string titel, Plan plan)
        {
            var sw = Stopwatch.StartNew();
            LogKind fk = LogKind.Bad;
            string fmsg = "Abgebrochen (Fehler)";
            try
            {
                if (plan == null || plan.Schritte.Count == 0)
                {
                    Log("✖  Der Plan „" + titel + "“ enthält 0 Schritte, es gibt nichts auszuführen.", LogKind.Bad);
                    fmsg = "Abgebrochen (leerer Plan)";
                }
                else
                {
                    // Der Hinweis nur, wenn der Dialog wirklich kommt: schon verbunden oder
                    // erhoeht heisst kein Dialog, und auf dem Abnahmeweg (--pipe) laeuft der
                    // Helfer schon von aussen.
                    if (!HelferClient.Verbunden && !HelferClient.Abnahmeweg)
                        Log("Windows fragt gleich nach Administratorrechten.", LogKind.Dim);

                    string grund;
                    IAusfuehrer a = HelferClient.Holen(true, out grund);
                    if (_cancel)
                    {
                        // "Abbrechen" waehrend des UAC-Dialogs: der Helfer kennt das Flag nicht,
                        // also darf der Plan gar nicht erst zu ihm. Ein gestarteter Helfer bleibt
                        // fuer den naechsten Auftrag verbunden (ein Dialog je Sitzung).
                        Log("✖  Abgebrochen.", LogKind.Bad);
                        fk = LogKind.Bad; fmsg = "Abgebrochen";
                    }
                    else if (a == null)
                    {
                        if (grund == Protokoll.Abgelehnt)
                        {
                            // Der Nutzer hat im UAC-Dialog "Nein" gesagt: normaler Rueckweg.
                            Log("✖  Ohne Administratorrechte kann dieser Schritt nicht laufen. Sie können es jederzeit erneut versuchen.", LogKind.Bad);
                            fk = LogKind.Bad; fmsg = "Abgebrochen (keine Administratorrechte)";
                        }
                        else
                        {
                            // Rechte erteilt, aber der Helfer kam technisch nicht zustande (Exit 7,
                            // keine Pipe binnen 60 s, Abnahme-Pipe stumm): das ist ein Fehler,
                            // keine Ablehnung, und so steht es auch im Verlauf.
                            string g = string.IsNullOrEmpty(grund) ? "ohne Angabe" : grund.TrimEnd('.', ' ');
                            Log("✖  Der Helfer ließ sich nicht starten: " + g + ".", LogKind.Bad);
                            fk = LogKind.Bad; fmsg = "Abgebrochen (Fehler)";
                        }
                    }
                    else
                    {
                        _ausfuehrer = a;
                        PlanErgebnis erg = Ausfuehren(a, plan);
                        sw.Stop();
                        Abschluss(erg, sw.Elapsed.TotalSeconds, out fk, out fmsg);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Plan " + titel, ex);
                Log("   Fehler: " + ex.Message, LogKind.Bad);
                fk = LogKind.Bad; fmsg = "Abgebrochen (Fehler)";
            }
            if (sw.IsRunning) sw.Stop();
            double fsec = sw.Elapsed.TotalSeconds;

            // Das Ende des Laufs steht im app.log, BEVOR es an die Oberflaeche geht: wird das
            // Fenster gerade geschlossen (ShellForm wartet mit LaufAbwarten), verwirft das
            // zerstoerte Fenster anstehende BeginInvokes, und die Zeile kaeme sonst nie. Der
            // Verlaufseintrag entsteht weiter in Done (ShellForm), das nach LaufAbwarten noch
            // zugestellt wird. Running/Title fallen erst dort (siehe Running).
            string ftitle = titel;
            AppLog.Info("Lauf beendet: " + ftitle + " - " + fmsg + " (" + fsec.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s).");
            AufUi(delegate
            {
                _running = false;
                _title = null;
                _ausfuehrer = null;
                _onState(false);
                if (_onComplete != null) _onComplete(ftitle, fk, fmsg, fsec);
            });
            _ende.Set();
        }

        /// <summary>
        /// Wartet hoechstens ms Millisekunden, bis der Hintergrund-Thread des laufenden Plans
        /// durch ist (Protokoll und app.log haben ihr Ende, Done ist an die Oberflaeche
        /// uebergeben). true = fertig oder es lief nichts; false = die Zeit ist um (der Helfer
        /// haengt noch im UAC-Dialog oder in einem Werkzeug). Fuer das Schliessen des Fensters:
        /// erst Cancel, dann LaufAbwarten, dann HelferClient.Beenden; sonst schliesst die Pipe
        /// unter dem Lauf weg und der Lauf endet als "Verbindung verloren" ohne Ende im Verlauf.
        /// Blockiert den Thread der Oberflaeche, deshalb kurz halten (5 s); Done selbst laeuft
        /// erst, wenn die Oberflaeche wieder Nachrichten verarbeitet.
        /// </summary>
        public bool LaufAbwarten(int ms)
        {
            if (!_running) return true;
            try { return _ende.WaitOne(ms < 0 ? 0 : ms); }
            catch (Exception ex)
            {
                AppLog.Warn("Auf das Ende des Laufs warten: " + ex.Message);
                return false;
            }
        }

        // Kontext bauen (Rueckrufe -> Oberflaeche) und den Plan beim Ausfuehrer laufen lassen.
        PlanErgebnis Ausfuehren(IAusfuehrer a, Plan plan)
        {
            var k = new Helfer.Ausfuehrungskontext();
            k.Zeile = delegate (string text, string art, int? prozent)
            {
                if (_cancel) AbbruchNachreichen(a);
                if (prozent.HasValue)
                {
                    Progress(prozent.Value);
                    if (string.IsNullOrEmpty(text)) return;   // reine Fortschrittszeile: kein Protokoll-Spam
                }
                Log(text ?? "", Art(art));
            };
            // Fortschritt ist der Ablaufbalken, nicht das Protokoll: keine Zeile, nur der
            // optionale Rueckruf. Die Kopfzeile "▶  Titel" je Schritt schreibt der Helfer.
            k.Fortschritt = delegate (int schritt, int gesamt, string label)
            {
                if (_cancel) AbbruchNachreichen(a);
                Action<int, int, string> h = OnStep;
                if (h == null) return;
                AufUi(delegate { h(schritt, gesamt, label ?? ""); });
            };
            k.Abgebrochen = delegate { return _cancel; };

            PlanErgebnis erg;
            try { erg = a.Plan(plan, k); }
            catch (Exception ex)
            {
                AppLog.Error("Ausführer " + a.Art, ex);
                erg = new PlanErgebnis { PlanId = plan.Id, Exit = PlanErgebnis.Ausnahme, Grund = ex.Message };
            }
            if (erg == null)
                erg = new PlanErgebnis { PlanId = plan.Id, Exit = PlanErgebnis.Ausnahme, Grund = "Der Ausführer lieferte kein Ergebnis" };
            return erg;
        }

        // Ein Abbruch, der zwischen der Pruefung nach Holen und dem Eintreffen des Plans beim
        // Helfer kam, hat dort nichts vorgefunden ("es läuft nichts", helfer/Pipe.cs) und ist
        // verpufft; lokal wirkt k.Abgebrochen, ueber die Pipe kennt der Helfer das Flag nicht.
        // Deshalb geht er mit der naechsten Zeile oder Fortschrittsmeldung des Helfers noch
        // einmal raus, hoechstens einmal je Lauf (Interlocked). Ein doppelter Abbruch ist fuer
        // den Helfer harmlos, ein verlorener kostet bis zu 45 Minuten.
        void AbbruchNachreichen(IAusfuehrer a)
        {
            if (Interlocked.Exchange(ref _abbruchNachgereicht, 1) != 0) return;
            try { a.Abbrechen(); }
            catch (Exception ex) { AppLog.Warn("Abbruch nachreichen: " + ex.Message); }
        }

        // Dieselben Fertigmeldungen wie bis 8.0: Abgebrochen / mit Hinweisen / Fertig in Xs.
        // Neu: abgelehnt (Exit 2) und Ausnahme (Exit 3) mit Grund, Neustart-Hinweis aus den Werten.
        void Abschluss(PlanErgebnis erg, double sekunden, out LogKind fk, out string fmsg)
        {
            if (erg.Wert("neustart") == "1")
                Log("●  Ein Neustart von Windows steht noch aus, erst danach ist die Änderung abgeschlossen.", LogKind.Warn);

            if (erg.Abgebrochen || erg.Exit == PlanErgebnis.AbgebrochenExit)
            {
                Log("✖  Abgebrochen.", LogKind.Bad);
                fk = LogKind.Bad; fmsg = "Abgebrochen";
            }
            else if (erg.Exit == PlanErgebnis.Abgelehnt)
            {
                // Eine Ablehnung aus der Ausfuehrung steht schon als bad-Zeile des Helfers im
                // Protokoll ("✖  Abgelehnt: ..."). Nur die Ablehnungen der Pipe selbst kommen
                // ohne solche Zeile an; deren Grund gehoert deshalb hierher.
                string g = GrundOhnePunkt(erg.Grund);
                bool ohneHelferzeile = g != null && (g.StartsWith("beschäftigt", StringComparison.Ordinal)
                                                  || g.StartsWith("planJson", StringComparison.Ordinal)
                                                  || g.StartsWith("Verbindung", StringComparison.Ordinal));
                Log("✖  Nicht ausgeführt." + (ohneHelferzeile ? " Grund: " + g + "." : ""), LogKind.Bad);
                fk = LogKind.Bad; fmsg = "Abgelehnt";
            }
            else if (erg.Exit == PlanErgebnis.Ausnahme)
            {
                Log("✖  Der Lauf ist mit einem Fehler abgebrochen: " + (GrundOhnePunkt(erg.Grund) ?? "ohne Angabe") + ".", LogKind.Bad);
                fk = LogKind.Bad; fmsg = "Abgebrochen (Fehler)";
            }
            else if (erg.Problem || erg.Exit != PlanErgebnis.Ok)
            {
                Log(string.Format("●  Fertig, aber nicht alles hat geklappt ({0:0.0}s). Die gelben Hinweise oben erklären Ursache und Lösung.", sekunden), LogKind.Warn);
                fk = LogKind.Warn; fmsg = "Mit Hinweisen abgeschlossen";
            }
            else
            {
                Log(string.Format("✔  Fertig in {0:0.0}s", sekunden), LogKind.Good);
                fk = LogKind.Good; fmsg = string.Format("Erfolgreich in {0:0.0}s", sekunden);
            }
            Log("", LogKind.Normal);
        }

        // Gruende des Helfers enden schon mit "."; der Satz hier setzt seinen eigenen. null bleibt null.
        static string GrundOhnePunkt(string grund)
        {
            if (string.IsNullOrEmpty(grund)) return null;
            string g = grund.TrimEnd('.', ' ');
            return g.Length == 0 ? null : g;
        }

        // Zeilenart des Helfers -> LogKind der Oberflaeche. Unbekanntes wird Normal, nie verworfen.
        static LogKind Art(string art)
        {
            switch (art)
            {
                case "header": return LogKind.Header;
                case "good": return LogKind.Good;
                case "bad": return LogKind.Bad;
                case "dim": return LogKind.Dim;
                case "warn": return LogKind.Warn;
                default: return LogKind.Normal;
            }
        }
    }
}
