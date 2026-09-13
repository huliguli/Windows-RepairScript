using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using WartungsToolbox.Kern;

namespace WartungsToolbox
{
    /// <summary>
    /// Wer einen Plan ausfuehrt oder erhoeht misst. Zwei Fassungen: lokal (der Host laeuft
    /// selbst schon erhoeht, der Helfer-Code arbeitet im eigenen Prozess) und ueber die Pipe
    /// (ein zweiter, per runas gestarteter Prozess derselben EXE). Der Aufrufer merkt den
    /// Unterschied nur an Art. Vertrag: docs/M2-ENTWURF.md, Abschnitte 5 und 6.
    ///
    /// Messen liefert ein Systembild oder wirft eine Ausnahme mit Grund; nie null.
    /// Plan liefert immer ein PlanErgebnis (Exit 2 abgelehnt, 3 Ausnahme oder Verbindung
    /// verloren), nie null.
    /// </summary>
    interface IAusfuehrer
    {
        string Art { get; }                                // "lokal" | "pipe"
        bool Lebt { get; }                                 // Pipe offen bzw. lokal immer true
        Systembild Messen(Action<string> fortschritt);
        PlanErgebnis Plan(Kern.Plan plan, Helfer.Ausfuehrungskontext k);   // blockiert; Rueckrufe aus k
        void Abbrechen();
        void Ende();
    }

    /// <summary>
    /// Der Host ist schon erhoeht (EnableLUA=0, eingebauter Administrator, erhoehte Shell):
    /// kein zweiter Prozess, keine Pipe, derselbe Helfer-Code im eigenen Prozess.
    /// </summary>
    class LokalAusfuehrer : IAusfuehrer
    {
        // Der zuletzt uebergebene Kontext: Abbrechen wirkt auf ihn (Flag + Prozessbaum).
        volatile Helfer.Ausfuehrungskontext _aktuell;

        public string Art { get { return "lokal"; } }
        public bool Lebt { get { return true; } }

        public Systembild Messen(Action<string> fortschritt)
        {
            return Helfer.Messung.Erfassen(fortschritt, null);
        }

        public PlanErgebnis Plan(Kern.Plan plan, Helfer.Ausfuehrungskontext k)
        {
            _aktuell = k;
            return Helfer.Ausfuehrung.PlanAusfuehren(plan, k);
        }

        public void Abbrechen()
        {
            Helfer.Ausfuehrungskontext k = _aktuell;
            if (k != null) k.Abbrechen();
        }

        public void Ende() { }
    }

    /// <summary>
    /// Client der Helfer-Pipe. Eine JSON-Zeile je Nachricht (UTF-8, \n-Ende). Anfragen bekommen
    /// laufende Ids (a1, a2, ...); ein Leser-Thread verteilt jede Antwort anhand antwortAuf an
    /// den wartenden Aufruf. Zwischenantworten (fortschritt, zeile) gehen an dessen Rueckruf,
    /// die Abschluss-Antwort (ergebnis|abgelehnt|fehler) weckt ihn. Bricht die Verbindung ab,
    /// werden alle Wartenden mit null geweckt und Lebt wird false.
    /// </summary>
    class PipeAusfuehrer : IAusfuehrer
    {
        public const int VerbindenMs = 60000;
        const int PingMs = 5000;
        const int EndeMs = 3000;

        readonly string _name;
        NamedPipeClientStream _pipe;
        readonly object _schreibSperre = new object();
        readonly Dictionary<string, Wartend> _wartend = new Dictionary<string, Wartend>();
        int _naechsteId;
        volatile bool _lebt;
        volatile bool _beendet;   // Ende() wurde gerufen: der Verbindungsabbruch danach ist gewollt
        volatile string _letzterGrund;   // Ablehnung ohne antwortAuf (SID-Abweichung, unlesbare Zeile)

        class Wartend
        {
            public readonly ManualResetEvent Fertig = new ManualResetEvent(false);
            public Helfer.Antwort Abschluss;      // null = Verbindung verloren
            public Action<Helfer.Antwort> Zwischen;
        }

        public PipeAusfuehrer(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Pipe-Name fehlt.", "name");
            _name = name;
        }

        public string Name { get { return _name; } }

        /// <summary>Grund der letzten Ablehnung ohne Anfrage-Id (z. B. „Aufrufer passt nicht zur SID“); null wenn keine.</summary>
        public string LetzterGrund { get { return _letzterGrund; } }
        public string Art { get { return "pipe"; } }
        public bool Lebt { get { return _lebt; } }

        /// <summary>
        /// Verbindet mit dem Server (wirft TimeoutException oder IOException) und startet den
        /// Leser. Der Aufrufer (HelferClient) wiederholt den Versuch, bis der Helfer nach dem
        /// UAC-Dialog seine Pipe angelegt hat.
        ///
        /// Identification statt der Kernel-Vorgabe Impersonation: der Helfer braucht vom Aufrufer
        /// nur die SID (RunAsClient + WindowsIdentity.GetCurrent().User), nicht das Recht, in
        /// seinem Namen zu handeln. Mit dem Live-Versuch vom 13.09.2026 belegt: ping ueber eine
        /// Identification-Verbindung liefert ergebnis, die SID-Pruefung des Servers besteht.
        /// </summary>
        public void Verbinden(int timeoutMs = VerbindenMs)
        {
            if (_lebt) return;
            var pipe = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous,
                                                 TokenImpersonationLevel.Identification);
            try { pipe.Connect(timeoutMs); }
            catch { pipe.Dispose(); throw; }
            _pipe = pipe;
            _lebt = true;
            var t = new Thread(Lesen);
            t.IsBackground = true;
            t.Name = "helfer-pipe-leser";
            t.Start();
        }

        // ---------------------------------------------------------------- Anfragen

        /// <summary>
        /// ping: true bei ergebnis, dann version (Assembly-Version des Helfers) und erhoeht
        /// ("0"/"1") aus der Antwort; beide leer, wenn der Helfer sie nicht mitschickt. Der
        /// Aufrufer entscheidet, was ein nicht erhoehter oder fremdversionierter Helfer bedeutet.
        /// </summary>
        public bool Ping(out string version, out string erhoeht)
        {
            version = null; erhoeht = null;
            Helfer.Antwort a = Aufrufen(Helfer.Anfrage.Neu(NeueId(), Helfer.Anfrage.Ping), null, PingMs);
            if (a == null || a.Typ != Helfer.Antwort.Ergebnis) return false;
            version = a.Wert("version") ?? "";
            erhoeht = a.Wert("erhoeht") ?? "";
            return true;
        }

        public Systembild Messen(Action<string> fortschritt)
        {
            Helfer.Antwort a = Aufrufen(Helfer.Anfrage.Neu(NeueId(), Helfer.Anfrage.Messen), delegate (Helfer.Antwort z)
            {
                if (z.Typ == Helfer.Antwort.Fortschritt && fortschritt != null) fortschritt(z.Wert("text") ?? "");
            }, Timeout.Infinite);

            if (a == null) throw new IOException("Verbindung zum Helfer verloren");
            if (a.Typ != Helfer.Antwort.Ergebnis)
                throw new InvalidOperationException("Der Helfer hat die Messung nicht ausgeführt: " + (a.Wert("grund") ?? a.Typ));
            string json = a.Wert("systembildJson");
            if (string.IsNullOrEmpty(json))
                throw new InvalidOperationException("Der Helfer hat 0 Zeichen Systembild geliefert.");
            return Helfer.Messung.AusJson(json);
        }

        public PlanErgebnis Plan(Kern.Plan plan, Helfer.Ausfuehrungskontext k)
        {
            string planId = plan != null ? plan.Id : null;
            string planJson = Json.Schreiben(plan);
            Helfer.Antwort a = Aufrufen(Helfer.Anfrage.Neu(NeueId(), Helfer.Anfrage.Plan, "planJson", planJson), delegate (Helfer.Antwort z)
            {
                if (k == null) return;
                if (z.Typ == Helfer.Antwort.Zeile)
                {
                    int p;
                    int? prozent = int.TryParse(z.Wert("prozent"), out p) ? p : (int?)null;
                    k.Schreibe(z.Wert("text") ?? "", z.Wert("art") ?? "normal", prozent);
                }
                else if (z.Typ == Helfer.Antwort.Fortschritt)
                {
                    int schritt, gesamt;
                    int.TryParse(z.Wert("schritt"), out schritt);
                    int.TryParse(z.Wert("gesamt"), out gesamt);
                    k.Melde(schritt, gesamt, z.Wert("text") ?? "");
                }
            }, Timeout.Infinite);

            if (a == null)
                return new PlanErgebnis { PlanId = planId, Exit = PlanErgebnis.Ausnahme, Grund = "Verbindung zum Helfer verloren" };
            if (a.Typ == Helfer.Antwort.Abgelehnt)
                return new PlanErgebnis { PlanId = planId, Exit = PlanErgebnis.Abgelehnt, Grund = a.Wert("grund") ?? "abgelehnt" };
            if (a.Typ == Helfer.Antwort.Fehler)
                return new PlanErgebnis { PlanId = planId, Exit = PlanErgebnis.Ausnahme, Grund = a.Wert("grund") ?? "Fehler im Helfer" };

            string ergebnisJson = a.Wert("ergebnisJson");
            try
            {
                if (!string.IsNullOrEmpty(ergebnisJson))
                {
                    PlanErgebnis e = Json.Lesen<PlanErgebnis>(ergebnisJson);
                    if (e != null) return e;
                }
            }
            catch (Exception ex) { AppLog.Warn("Ergebnis des Helfers nicht lesbar: " + ex.Message); }
            return new PlanErgebnis { PlanId = planId, Exit = PlanErgebnis.Ausnahme, Grund = "Das Ergebnis des Helfers war nicht lesbar" };
        }

        /// <summary>Abbruch anfordern; die Antwort wird nicht abgewartet (der laufende Plan endet mit abgebrochen).</summary>
        public void Abbrechen()
        {
            if (!_lebt) return;
            var w = new Wartend();                          // damit die Antwort einen Empfaenger hat
            string id = NeueId();
            lock (_wartend) _wartend[id] = w;
            if (!Senden(Helfer.Anfrage.Neu(id, Helfer.Anfrage.Abbrechen)))
                lock (_wartend) _wartend.Remove(id);
        }

        /// <summary>ende senden (kurz auf die Antwort warten) und die Pipe schliessen.</summary>
        public void Ende()
        {
            _beendet = true;
            if (_lebt)
            {
                try { Aufrufen(Helfer.Anfrage.Neu(NeueId(), Helfer.Anfrage.Ende), null, EndeMs); } catch (Exception) { }
            }
            Schliessen();
        }

        // ---------------------------------------------------------------- Innen

        string NeueId()
        {
            return "a" + Interlocked.Increment(ref _naechsteId);
        }

        /// <summary>
        /// Anfrage senden und auf die Abschluss-Antwort warten. null bei Verbindungsabbruch oder
        /// abgelaufener Wartezeit; Zwischenantworten gehen waehrenddessen an zwischen.
        /// </summary>
        Helfer.Antwort Aufrufen(Helfer.Anfrage anfrage, Action<Helfer.Antwort> zwischen, int wartenMs)
        {
            var w = new Wartend { Zwischen = zwischen };
            lock (_wartend) _wartend[anfrage.Id] = w;
            if (!Senden(anfrage))
            {
                lock (_wartend) _wartend.Remove(anfrage.Id);
                return null;
            }
            if (!w.Fertig.WaitOne(wartenMs))
            {
                lock (_wartend) _wartend.Remove(anfrage.Id);
                AppLog.Warn("Helfer: keine Antwort auf " + anfrage.Typ + " binnen " + wartenMs + " ms.");
                return null;
            }
            return w.Abschluss;
        }

        bool Senden(Helfer.Anfrage anfrage)
        {
            NamedPipeClientStream pipe = _pipe;
            if (!_lebt || pipe == null) return false;
            try
            {
                byte[] b = Encoding.UTF8.GetBytes(Helfer.Rahmen.Kodieren(anfrage) + "\n");
                lock (_schreibSperre)
                {
                    pipe.Write(b, 0, b.Length);
                    pipe.Flush();
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Helfer: Senden fehlgeschlagen (" + anfrage.Typ + "): " + ex.Message);
                VerbindungVerloren();
                return false;
            }
        }

        // Leser-Thread: liest Zeile fuer Zeile, bis der Server schliesst oder die Pipe bricht.
        void Lesen()
        {
            NamedPipeClientStream pipe = _pipe;
            if (pipe == null) { VerbindungVerloren(); return; }
            try
            {
                using (var leser = new StreamReader(pipe, new UTF8Encoding(false), false, 65536, true))
                {
                    string zeile;
                    while ((zeile = leser.ReadLine()) != null)
                    {
                        if (zeile.Length == 0) continue;
                        Verteilen(zeile);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_beendet) AppLog.Warn("Helfer: Leser beendet: " + ex.Message);
            }
            finally { VerbindungVerloren(); }
        }

        void Verteilen(string zeile)
        {
            Helfer.Antwort a;
            try { a = Helfer.Rahmen.Dekodieren<Helfer.Antwort>(zeile); }
            catch (Exception ex) { AppLog.Warn("Helfer: Antwort nicht lesbar: " + ex.Message); return; }
            if (a == null) { AppLog.Warn("Helfer: leere Antwort."); return; }
            if (string.IsNullOrEmpty(a.AntwortAuf))
            {
                // Der Server lehnt ohne Id ab, wenn die Verbindung selbst nicht passt (SID) oder die
                // Zeile unlesbar war. Den Grund merken: HelferClient nennt ihn im Fehlertext.
                string g = a.Wert("grund");
                if (!string.IsNullOrEmpty(g)) _letzterGrund = g;
                AppLog.Warn("Helfer: " + (a.Typ ?? "?") + " ohne antwortAuf" + (g != null ? ": " + g : "."));
                return;
            }

            Wartend w;
            lock (_wartend)
            {
                _wartend.TryGetValue(a.AntwortAuf, out w);
                if (w != null && a.IstAbschluss) _wartend.Remove(a.AntwortAuf);
            }
            if (w == null) { AppLog.Warn("Helfer: Antwort auf unbekannte Anfrage " + a.AntwortAuf + " (" + a.Typ + ")."); return; }

            if (a.IstAbschluss)
            {
                w.Abschluss = a;
                w.Fertig.Set();
            }
            else if (w.Zwischen != null)
            {
                try { w.Zwischen(a); } catch (Exception ex) { AppLog.Warn("Helfer: Rueckruf fehlgeschlagen: " + ex.Message); }
            }
        }

        // Alle Wartenden mit null wecken; danach nimmt dieser Ausfuehrer nichts mehr an.
        void VerbindungVerloren()
        {
            bool war = _lebt;
            _lebt = false;
            List<Wartend> offen;
            lock (_wartend)
            {
                offen = new List<Wartend>(_wartend.Values);
                _wartend.Clear();
            }
            foreach (Wartend w in offen) { w.Abschluss = null; w.Fertig.Set(); }
            if (war && !_beendet) AppLog.Warn("Helfer: Verbindung verloren (" + offen.Count + " offene Anfragen).");
            Schliessen();
        }

        void Schliessen()
        {
            _lebt = false;
            NamedPipeClientStream pipe = _pipe;
            _pipe = null;
            if (pipe != null) { try { pipe.Dispose(); } catch (Exception) { } }
        }
    }
}
