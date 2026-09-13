using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    /// <summary>
    /// Die Named Pipe zwischen dem nicht erhoehten Host und dem erhoehten Helfer
    /// (docs/M2-ENTWURF.md, Abschnitte 5 und 12). Der Host erzeugt den Namen, startet den Helfer
    /// per runas mit Name und eigener SID und verbindet sich dann als Client.
    ///
    /// Sicherheit in zwei Schichten: die Pipe selbst laesst nur das Konto des Hosts und SYSTEM
    /// zu (Sicherheitsbeschreibung, kein "Jeder"), und nach dem ersten gelesenen Block prueft der
    /// Helfer die SID des Aufrufers per Impersonation noch einmal selbst. Wer nicht passt, bekommt
    /// eine Ablehnung und der Helfer beendet sich.
    /// </summary>
    public static class Pipe
    {
        public const string Praefix = "ww-helfer-";

        /// <summary>"ww-helfer-" + 32 Hexziffern; je Helferstart ein neuer Name.</summary>
        public static string NeuerName()
        {
            return Praefix + Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Genau zwei Regeln, beide FullControl: das uebergebene Konto und LocalSystem. Keine
        /// weitere (kein WorldSid, kein AuthenticatedUserSid, kein BuiltinUsersSid), die
        /// Helferprobe prueft das.
        /// </summary>
        public static PipeSecurity PipeSicherheit(string sid)
        {
            if (string.IsNullOrEmpty(sid)) throw new ArgumentException("SID fehlt.", "sid");
            var ps = new PipeSecurity();
            ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid),
                                                PipeAccessRights.FullControl, AccessControlType.Allow));
            ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                                                PipeAccessRights.FullControl, AccessControlType.Allow));
            return ps;
        }

        /// <summary>Server: beide Richtungen, eine Instanz, Bytestrom, ueberlappend, 64 KB je Richtung.</summary>
        public static NamedPipeServerStream Server(string name, string sid)
        {
            return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                                             PipeOptions.Asynchronous, 65536, 65536, PipeSicherheit(sid));
        }

        /// <summary>Zulaessiger Pipe-Name: Buchstaben, Ziffern, Punkt, Bindestrich, Unterstrich; 3 bis 100 Zeichen.</summary>
        public static bool NameGueltig(string name)
        {
            if (name == null || name.Length < 3 || name.Length > 100) return false;
            foreach (char c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                          || c == '.' || c == '-' || c == '_';
                if (!ok) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Bedient genau eine Verbindung: liest Anfragen Zeile fuer Zeile in diesem Thread und laesst
    /// messen/plan in einem zweiten laufen, damit ping, abbrechen und ende waehrend eines Plans
    /// ankommen. Jede Anfrage bekommt genau eine Abschlussantwort (ergebnis, abgelehnt oder
    /// fehler); Zwischenmeldungen (fortschritt, zeile) gehen davor. Schreiben nur unter Sperre,
    /// jede Zeile wird sofort geleert.
    ///
    /// Protokolle (Nachtrag 13.09.2026): die Sitzung selbst schreibt nur nach app.log ("helfer: ..."),
    /// keine lauf-Datei je UAC-Start. Ein Plan ueber die Pipe bekommt sein eigenes
    /// lauf-&lt;planId&gt;.jsonl, genau wie bei --plan.
    ///
    /// Ende: "ende" vom Host (laufender Auftrag wird abgebrochen, bis 15 s Wartezeit), Pipe vom
    /// Host geschlossen (laufender Auftrag wird zu Ende gebracht), oder Helfer.LeerlaufMs ohne
    /// Anfrage bei ruhendem Helfer. Rueckgabe von Bedienen ist der Exit-Code des Prozesses.
    /// </summary>
    public class PipeServer
    {
        const int LeseBlock = 65536;
        const int WarteScheibeMs = 1000;
        const int EndeWartenMs = 15000;
        const int KurzZeichen = 120;
        const string SidGrund = "Aufrufer passt nicht zur SID";

        readonly string _name;
        readonly string _sid;
        readonly SecurityIdentifier _sidObjekt;
        readonly Protokoll _protokoll;      // darf null sein: Sitzungszeilen dann nur nach app.log (Normalfall)
        readonly object _schreibGate = new object();
        readonly object _jobGate = new object();

        NamedPipeServerStream _pipe;
        volatile bool _kaputt;              // Verbindung weg oder Schreibfehler: nichts mehr senden
        volatile bool _ende;                // "ende" empfangen
        bool _beschaeftigt;                 // unter _jobGate: messen oder plan laeuft
        string _jobTyp;                     // unter _jobGate: "messen" oder "plan"
        Thread _job;                        // unter _jobGate
        Ausfuehrungskontext _laufend;       // unter _jobGate: Kontext des Plans, gesetzt VOR dem Start des Threads (fuer abbrechen/ende)
        long _letzteAnfrageTicks;

        /// <summary>
        /// protokoll darf null sein: dann gehen die Sitzungszeilen (gestartet, verbunden, beendet,
        /// Ablehnungen) nur nach app.log. Ein uebergebenes Protokoll bekommt sie zusaetzlich (Probe).
        /// </summary>
        public PipeServer(string name, string sid, Protokoll protokoll)
        {
            if (!Pipe.NameGueltig(name)) throw new ArgumentException("Pipe-Name unzulässig.", "name");
            _sidObjekt = new SecurityIdentifier(sid);   // wirft bei ungueltiger Form
            _name = name;
            _sid = _sidObjekt.Value;
            _protokoll = protokoll;
            Beruehren();
        }

        /// <summary>Blockiert bis ende, Leerlauf oder Verbindungsende. 0 = normal, 4 = Pipe/Sicherheit.</summary>
        public int Bedienen()
        {
            try { _pipe = Pipe.Server(_name, _sid); }
            catch (Exception ex)
            {
                Log(Protokoll.FehlerArt, "Die Verbindung zum Hauptprogramm konnte nicht eingerichtet werden, der Helfer beendet sich",
                    new { pipe = _name, fehler = Fehlertext(ex) });
                return Helfer.ExitPipe;
            }

            using (_pipe)
            {
                // Auf den Host warten, aber nicht ewig: bleibt er aus, endet der Helfer nach der Leerlaufgrenze.
                var warten = _pipe.BeginWaitForConnection(null, null);
                if (!warten.AsyncWaitHandle.WaitOne(Helfer.LeerlaufMs))
                {
                    Log(Protokoll.Ende, "Das Hauptprogramm hat sich " + (Helfer.LeerlaufMs / 60000) + " Minuten lang nicht gemeldet, der Helfer beendet sich",
                        new { pipe = _name });
                    return Helfer.ExitOk;
                }
                try { _pipe.EndWaitForConnection(warten); }
                catch (Exception ex)
                {
                    Log(Protokoll.FehlerArt, "Die Verbindung mit dem Hauptprogramm ist fehlgeschlagen, der Helfer beendet sich",
                        new { pipe = _name, fehler = Fehlertext(ex) });
                    return Helfer.ExitPipe;
                }

                // Die SID-Pruefung (zweite Schicht) steckt in der Leseschleife: Windows laesst den
                // Identitaetswechsel (ImpersonateNamedPipeClient) erst zu, nachdem der Server aus
                // der Pipe gelesen hat (ERROR_CANNOT_IMPERSONATE). Bis dahin wird nichts verarbeitet.
                Beruehren();
                int exit = Schleife();

                // Nach "ende" hoechstens 15 s auf den abgebrochenen Auftrag warten; ist der Host
                // ohne "ende" verschwunden, laeuft der Auftrag zu Ende (kein halbes sfc /scannow).
                JobAbwarten(_ende ? EndeWartenMs : Timeout.Infinite);

                // Ab hier schliesst das using die Pipe. Was der Arbeitsthread danach noch senden
                // will, ist gewollt verloren und kein Fehler im Protokoll.
                _kaputt = true;
                return exit;
            }
        }

        // ------------------------------------------------------------------ Lesen

        /// <summary>Liest Bloecke, prueft beim ersten die SID, zerlegt sie in Zeilen und verarbeitet jede sofort. Rueckgabe = Exit-Code.</summary>
        int Schleife()
        {
            var puffer = new byte[LeseBlock];
            var zeile = new MemoryStream();
            bool zuLang = false;
            bool geprueft = false;

            while (!_ende)
            {
                IAsyncResult lesen;
                try { lesen = _pipe.BeginRead(puffer, 0, puffer.Length, null, null); }
                catch (Exception ex) { VerbindungWeg(IstNormalesEnde(ex) ? null : Fehlertext(ex)); break; }

                // Warten in Scheiben, damit der Leerlauf greift, waehrend der Host still ist.
                while (!lesen.AsyncWaitHandle.WaitOne(WarteScheibeMs))
                {
                    if (IstLeerlauf())
                    {
                        Log(Protokoll.Ende, "Seit " + (Helfer.LeerlaufMs / 60000) + " Minuten kam kein Auftrag vom Hauptprogramm, der Helfer beendet sich",
                            new { pipe = _name });
                        _kaputt = true;
                        return Helfer.ExitOk;
                    }
                }

                // 0 Byte und die drei "Pipe zu"-Fehler sind das normale Ende vom Host; jeder andere
                // Fehler (Handle, Speicher) steht mit Typ und Text im Protokoll.
                int n;
                string fehler = null;
                try { n = _pipe.EndRead(lesen); }
                catch (Exception ex) { n = 0; fehler = IstNormalesEnde(ex) ? null : Fehlertext(ex); }
                if (n <= 0) { VerbindungWeg(fehler); break; }

                if (!geprueft)
                {
                    // Erst jetzt (nach dem ersten Lesen) darf der Server den Aufrufer nachahmen.
                    SecurityIdentifier aufrufer = AufruferSid();
                    if (aufrufer == null || !aufrufer.Equals(_sidObjekt))
                    {
                        Senden(Antwort.Neu(null, Antwort.Abgelehnt, "grund", SidGrund));
                        Log(Protokoll.FehlerArt, "Die Verbindung kam nicht vom erwarteten Konto, der Helfer lehnt sie ab und beendet sich",
                            new { grund = SidGrund, erwartet = _sid, aufrufer = aufrufer == null ? "(unbekannt)" : aufrufer.Value });
                        _kaputt = true;
                        return Helfer.ExitPipe;
                    }
                    geprueft = true;
                    Log(Protokoll.Anfang, "Das Hauptprogramm ist verbunden", new { pipe = _name, sid = _sid });
                }

                for (int i = 0; i < n && !_ende; i++)
                {
                    byte b = puffer[i];
                    if (b == (byte)'\n')
                    {
                        if (zuLang)
                        {
                            Senden(Antwort.Neu(null, Antwort.Abgelehnt, "grund", "Zeile länger als " + Rahmen.MaxZeile + " Byte"));
                            Log(Protokoll.Abgelehnt, "Ein Auftrag war länger als " + Rahmen.MaxZeile + " Byte und wurde abgelehnt", null);
                            zuLang = false;
                        }
                        else Verarbeiten(zeile.ToArray());
                        zeile.SetLength(0);
                    }
                    else if (zuLang) { /* Rest der ueberlangen Zeile verwerfen */ }
                    // Genau MaxZeile Byte sind erlaubt, das naechste Byte darueber macht die Zeile zu lang.
                    else if (zeile.Length + 1 > Rahmen.MaxZeile) { zuLang = true; zeile.SetLength(0); }
                    else zeile.WriteByte(b);
                }
            }
            return Helfer.ExitOk;
        }

        /// <summary>fehler == null: der Host hat sein Ende geschlossen (normal). Sonst der Fehlertext aus dem Leser.</summary>
        void VerbindungWeg(string fehler)
        {
            _kaputt = true;
            bool laeuft;
            lock (_jobGate) laeuft = _beschaeftigt;
            string weiter = laeuft ? "der laufende Auftrag wird noch zu Ende gebracht" : "der Helfer beendet sich";
            if (fehler == null)
                Log(Protokoll.Ende, "Die Verbindung zum Hauptprogramm ist geschlossen, " + weiter, new { pipe = _name });
            else
                Log(Protokoll.FehlerArt, "Die Verbindung zum Hauptprogramm ist mit einem Fehler abgebrochen, " + weiter, new { pipe = _name, fehler });
        }

        /// <summary>ERROR_BROKEN_PIPE 109, ERROR_NO_DATA 232, ERROR_PIPE_NOT_CONNECTED 233: die Gegenseite ist weg, kein Fehler des Helfers.</summary>
        static bool IstNormalesEnde(Exception ex)
        {
            if (!(ex is IOException)) return false;
            if (((ex.HResult >> 16) & 0xFFFF) != 0x8007) return false;
            int code = ex.HResult & 0xFFFF;
            return code == 109 || code == 232 || code == 233;
        }

        // ------------------------------------------------------------------ Anfragen

        void Verarbeiten(byte[] roh)
        {
            Beruehren();
            // GetString wirft nicht (ungueltige Folgen werden zum Ersatzzeichen U+FFFD); die
            // Lesbarkeit entscheidet erst das JSON.
            string text = Encoding.UTF8.GetString(roh);
            if (text.Trim('\r', ' ', '\t', '\uFEFF').Length == 0) return;   // Leerzeile: keine Anfrage, keine Antwort-Id

            Anfrage a;
            string fehler = null;
            try { a = Rahmen.Dekodieren<Anfrage>(text); }
            catch (Exception ex) { a = null; fehler = ex.Message; }
            if (a == null)
            {
                if (fehler == null) fehler = "kein Objekt";
                Senden(Antwort.Neu(null, Antwort.Abgelehnt, "grund", "Anfrage nicht lesbar: " + Kurz(fehler)));
                Log(Protokoll.Abgelehnt, "Ein Auftrag war nicht lesbar und wurde abgelehnt", new { fehler = Kurz(fehler) });
                return;
            }

            // Ohne id gaebe es keine Abschlussantwort, die der Host zuordnen kann; so ein Auftrag
            // darf nichts anstossen. antwortAuf null bleibt fuer unlesbare Zeilen und die SID-Absage.
            string id = a.Id;
            if (string.IsNullOrEmpty(id))
            {
                Senden(Antwort.Neu(null, Antwort.Abgelehnt, "grund", "id fehlt"));
                Log(Protokoll.Abgelehnt, "Ein Auftrag ohne Kennung wurde abgelehnt", new { typ = Kurz(a.Typ), grund = "id fehlt" });
                return;
            }

            // abgeschlossen wird erst NACH dem Senden gesetzt: wirft davor etwas, liefert der
            // catch unten die fehlende Abschlussantwort nach.
            bool abgeschlossen = false;
            try
            {
                switch (a.Typ)
                {
                    case Anfrage.Ping:
                        Senden(Antwort.Neu(id, Antwort.Ergebnis,
                                           "version", Version(),
                                           "erhoeht", Sammler.Quellen.Rechte.Erhoeht() ? "1" : "0"));
                        abgeschlossen = true;
                        break;

                    case Anfrage.Messen:
                        StarteJob(id, a, Messen, false);
                        break;

                    case Anfrage.Plan:
                        StarteJob(id, a, PlanLaufen, true);
                        break;

                    case Anfrage.Abbrechen:
                    {
                        Ausfuehrungskontext k; bool laeuft;
                        lock (_jobGate) { k = _laufend; laeuft = _beschaeftigt; }
                        if (k != null) k.Abbrechen();
                        Log(Protokoll.Rueckweg, laeuft ? (k != null ? "Das Hauptprogramm hat den Abbruch verlangt, der laufende Auftrag wird beendet"
                                                                    : "Das Hauptprogramm hat den Abbruch verlangt, die laufende Messung endet von selbst")
                                                       : "Das Hauptprogramm hat den Abbruch verlangt, aber es läuft nichts", new { id });
                        Senden(Antwort.Neu(id, Antwort.Ergebnis));
                        abgeschlossen = true;
                        break;
                    }

                    case Anfrage.Ende:
                    {
                        Ausfuehrungskontext k; bool laeuft;
                        lock (_jobGate) { k = _laufend; laeuft = _beschaeftigt; }
                        if (k != null) k.Abbrechen();
                        Log(Protokoll.Ende, laeuft ? "Das Hauptprogramm hat das Ende verlangt, der laufende Auftrag wird abgebrochen"
                                                   : "Das Hauptprogramm hat das Ende verlangt, der Helfer beendet sich", new { id });
                        _ende = true;
                        Senden(Antwort.Neu(id, Antwort.Ergebnis));
                        abgeschlossen = true;
                        break;
                    }

                    default:
                        Log(Protokoll.Abgelehnt, "Ein unbekannter Auftrag wurde abgelehnt", new { typ = Kurz(a.Typ), id });
                        Senden(Antwort.Neu(id, Antwort.Abgelehnt, "grund", "Unbekannte Anfrage „" + Kurz(a.Typ) + "“"));
                        abgeschlossen = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                Log(Protokoll.FehlerArt, "Ein Auftrag ist mit einem Fehler abgebrochen", new { typ = Kurz(a.Typ), id }, ex);
                if (!abgeschlossen) Senden(Antwort.Neu(id, Antwort.Fehler, "grund", Kurz(ex.Message)));
            }
        }

        /// <summary>
        /// messen und plan laufen in einem eigenen Thread; nur einer zugleich. Die Arbeit liefert
        /// die Abschlussantwort; erst wird der Helfer wieder frei, dann geht sie raus, so kann
        /// der Host direkt nach dem Ergebnis den naechsten Auftrag schicken.
        ///
        /// Der Kontext eines Plans entsteht hier unter der Sperre, VOR dem Start des Threads:
        /// abbrechen und ende treffen damit auch in dem Augenblick auf einen Kontext, in dem der
        /// Plan noch gelesen wird (bis 1 MB JSON). Protokoll und Rueckrufe traegt PlanLaufen nach.
        /// </summary>
        void StarteJob(string id, Anfrage a, Func<string, Anfrage, Ausfuehrungskontext, Antwort> arbeit, bool mitKontext)
        {
            lock (_jobGate)
            {
                if (_beschaeftigt)
                {
                    Log(Protokoll.Abgelehnt, "Ein Auftrag wurde abgelehnt, weil noch einer läuft", new { typ = a.Typ, id, laeuft = _jobTyp, grund = "beschäftigt" });
                    Senden(Antwort.Neu(id, Antwort.Abgelehnt, "grund", "beschäftigt"));
                    return;
                }
                Ausfuehrungskontext k = mitKontext ? new Ausfuehrungskontext { AufruferSid = _sid } : null;
                _beschaeftigt = true;
                _jobTyp = a.Typ;
                _laufend = k;
                // Die Job-Felder werden nur im Arbeitsthread zurueckgesetzt. Wirft die Anlage
                // oder der Start des Threads (OutOfMemory), laeuft der nie: dann hier zuruecksetzen,
                // sonst bliebe die Verbindung fuer den Rest ihrer Lebensdauer "beschaeftigt"
                // (Nachtrag B3). Die Ausnahme geht weiter an Verarbeiten, das "fehler" antwortet.
                try
                {
                    var t = new Thread(() =>
                    {
                        Antwort abschluss;
                        try { abschluss = arbeit(id, a, k); }
                        catch (Exception ex)
                        {
                            Log(Protokoll.FehlerArt, "Der Auftrag ist mit einem Fehler abgebrochen", new { typ = a.Typ, id }, ex);
                            abschluss = Antwort.Neu(id, Antwort.Fehler, "grund", Kurz(ex.Message));
                        }
                        lock (_jobGate) { _laufend = null; _beschaeftigt = false; _jobTyp = null; _job = null; }
                        Beruehren();
                        Senden(abschluss);
                    });
                    t.IsBackground = true;
                    t.Name = "helfer-" + a.Typ;
                    _job = t;
                    t.Start();
                }
                catch (Exception ex)
                {
                    _laufend = null; _beschaeftigt = false; _jobTyp = null; _job = null;
                    Log(Protokoll.FehlerArt, "Der Arbeitsthread für den Auftrag ließ sich nicht starten; die Verbindung bleibt frei", new { typ = a.Typ, id }, ex);
                    throw;
                }
            }
        }

        Antwort Messen(string id, Anfrage a, Ausfuehrungskontext k)
        {
            Log(Protokoll.Anfang, "Die Messung mit Administratorrechten beginnt", new { id });
            // Kein eigenes Laufprotokoll: das Protokoll der Pruefung fuehrt das Hauptprogramm; die
            // Messzeilen des Sammlers landen nur in einem uebergebenen Probe-Protokoll.
            var bild = Messung.Erfassen(text => Senden(Antwort.Neu(id, Antwort.Fortschritt, "text", text ?? "")), _protokoll);
            string json = Messung.AlsJson(bild);
            Log(Protokoll.Ende, "Die Messung mit Administratorrechten ist fertig", new { id, zeichen = json.Length, fehler = bild.Fehlerliste.Count });
            return Antwort.Neu(id, Antwort.Ergebnis, "systembildJson", json);
        }

        Antwort PlanLaufen(string id, Anfrage a, Ausfuehrungskontext k)
        {
            if (k == null) k = new Ausfuehrungskontext { AufruferSid = _sid };

            string planJson = a.Wert("planJson");
            if (string.IsNullOrEmpty(planJson))
            {
                Log(Protokoll.Abgelehnt, "Der Auftrag enthielt keinen Plan und wurde abgelehnt", new { id, grund = "planJson fehlt" });
                return Antwort.Neu(id, Antwort.Abgelehnt, "grund", "planJson fehlt");
            }
            Plan plan;
            string fehler = null;
            try { plan = Json.Lesen<Plan>(planJson); }
            catch (Exception ex) { plan = null; fehler = ex.Message; }
            if (plan == null)
            {
                if (fehler == null) fehler = "kein Objekt";
                Log(Protokoll.Abgelehnt, "Der Plan war nicht lesbar und wurde abgelehnt", new { id, fehler = Kurz(fehler) });
                return Antwort.Neu(id, Antwort.Abgelehnt, "grund", "planJson nicht lesbar: " + Kurz(fehler));
            }

            // Die Plan-Id wird Dateiname (lauf-<id>.jsonl). Der erhoehte Helfer nimmt keinen Pfad
            // von aussen an: eine ungueltige Id wird abgelehnt, nicht ersetzt (ersetzen tut nur --plan).
            if (!Protokoll.LaufIdGueltig(plan.Id))
            {
                Log(Protokoll.Abgelehnt, "Der Plan trug eine unzulässige Kennung und wurde abgelehnt", new { id, planId = Kurz(plan.Id), grund = "Plan-Id unzulässig" });
                return Antwort.Neu(id, Antwort.Abgelehnt, "grund", "Plan-Id unzulässig");
            }

            // Der Plan bekommt sein eigenes Protokoll lauf-<planId>.jsonl (wie bei --plan).
            // Der Kontext selbst existiert schon seit StarteJob (abbrechen/ende greifen bereits).
            k.Protokoll = new Protokoll(plan.Id);
            k.Zeile = (text, art, prozent) =>
            {
                var z = Antwort.Neu(id, Antwort.Zeile, "text", text ?? "", "art", art ?? "normal");
                if (prozent.HasValue) z.Nutzlast["prozent"] = prozent.Value.ToString(CultureInfo.InvariantCulture);
                Senden(z);
            };
            k.Fortschritt = (schritt, gesamt, text) => Senden(Antwort.Neu(id, Antwort.Fortschritt,
                "schritt", schritt.ToString(CultureInfo.InvariantCulture),
                "gesamt", gesamt.ToString(CultureInfo.InvariantCulture),
                "text", text ?? ""));

            string titel = Kurz(string.IsNullOrEmpty(plan.Titel) ? plan.Id : plan.Titel);
            k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Plan, "pipe",
                "Der Auftrag „" + titel + "“ mit " + plan.Schritte.Count + " Schritten kam vom Hauptprogramm an",
                new { id, planId = plan.Id, schritte = plan.Schritte.Count, aufrufer = _sid });
            AppLog.Info("helfer: Plan " + plan.Id + " (" + plan.Schritte.Count + " Schritte) angenommen, Protokoll " + (k.Protokoll.Pfad ?? "(nur im Speicher)"));

            PlanErgebnis erg = Ausfuehrung.PlanAusfuehren(plan, k);

            // Anfang und Ende des Laufs stehen im Plan-Protokoll (Ausfuehrung); hier nur app.log.
            AppLog.Info("helfer: Plan " + plan.Id + " beendet, Exit " + erg.Exit
                        + (erg.Problem ? ", mit Problem" : "") + (erg.Abgebrochen ? ", abgebrochen" : "")
                        + (string.IsNullOrEmpty(erg.Grund) ? "" : ", Grund: " + Kurz(erg.Grund))
                        + ", " + erg.Sekunden.ToString("0.0", CultureInfo.InvariantCulture) + " s.");
            return Antwort.Neu(id, Antwort.Ergebnis, "ergebnisJson", Json.Schreiben(erg));
        }

        // ------------------------------------------------------------------ Schreiben, Zeit, Hilfen

        /// <summary>Eine Zeile, UTF-8 ohne BOM, "\n", sofort geleert. Nach einem Fehler geht nichts mehr raus (Protokoll bleibt).</summary>
        void Senden(Antwort a)
        {
            if (a == null) return;
            byte[] b = Encoding.UTF8.GetBytes(Rahmen.Kodieren(a) + "\n");
            lock (_schreibGate)
            {
                if (_kaputt || _pipe == null) return;
                try { _pipe.Write(b, 0, b.Length); _pipe.Flush(); }
                catch (Exception ex)
                {
                    _kaputt = true;
                    Log(Protokoll.FehlerArt, "Die Antwort an das Hauptprogramm kam nicht an, die Verbindung gilt als verloren",
                        new { typ = a.Typ, fehler = Fehlertext(ex) });
                }
            }
        }

        SecurityIdentifier AufruferSid()
        {
            SecurityIdentifier sid = null;
            try
            {
                _pipe.RunAsClient(() =>
                {
                    using (var id = WindowsIdentity.GetCurrent()) sid = id.User;
                });
            }
            catch (Exception ex)
            {
                Log(Protokoll.FehlerArt, "Das Konto des Hauptprogramms ließ sich nicht feststellen", new { fehler = Fehlertext(ex) });
                return null;
            }
            return sid;
        }

        void Beruehren()
        {
            Interlocked.Exchange(ref _letzteAnfrageTicks, DateTime.UtcNow.Ticks);
        }

        bool IstLeerlauf()
        {
            lock (_jobGate) { if (_beschaeftigt) return false; }
            long seit = DateTime.UtcNow.Ticks - Interlocked.Read(ref _letzteAnfrageTicks);
            return seit >= (long)Helfer.LeerlaufMs * TimeSpan.TicksPerMillisecond;
        }

        void JobAbwarten(int ms)
        {
            Thread t;
            lock (_jobGate) t = _job;
            if (t == null) return;
            try { t.Join(ms); } catch (Exception) { }
        }

        /// <summary>
        /// Sitzungszeile: immer nach app.log ("helfer: laie (fachmann)"), Stufe nach art; dazu in
        /// das Protokoll, wenn der Konstruktor eins bekommen hat. ex haengt die Ablaufverfolgung an.
        /// </summary>
        void Log(string art, string laie, object fachmann, Exception ex = null)
        {
            string zeile = "helfer: " + laie + FachmannText(fachmann);
            if (ex != null) AppLog.Error(zeile, ex);
            else if (art == Protokoll.FehlerArt) AppLog.Error(zeile);
            else if (art == Protokoll.Abgelehnt) AppLog.Warn(zeile);
            else AppLog.Info(zeile);
            if (_protokoll != null)
            {
                try { _protokoll.Schreibe(Protokoll.Helfer, art, "pipe", laie, fachmann); } catch (Exception) { }
            }
        }

        /// <summary>" (pipe=ww-helfer-…, sid=S-1-5-…)" fuer app.log; leer bei null.</summary>
        static string FachmannText(object fachmann)
        {
            if (fachmann == null) return "";
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, object> kv in Protokoll.AlsWoerterbuch(fachmann))
            {
                sb.Append(sb.Length == 0 ? " (" : ", ").Append(kv.Key).Append('=')
                  .Append(kv.Value == null ? "" : Convert.ToString(kv.Value, CultureInfo.InvariantCulture));
            }
            return sb.Length == 0 ? "" : sb.Append(')').ToString();
        }

        /// <summary>Typ und Text einer Ausnahme, gekuerzt.</summary>
        static string Fehlertext(Exception ex)
        {
            return ex == null ? "" : ex.GetType().Name + ": " + Kurz(ex.Message);
        }

        /// <summary>Fremdtext (Typ, Fehlertext, Kennung, Titel) auf eine Zeile und 120 Zeichen, bevor er in Antwort oder Protokoll geht.</summary>
        static string Kurz(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > KurzZeichen ? s.Substring(0, KurzZeichen) + "…" : s;
        }

        static string Version()
        {
            try { return typeof(PipeServer).Assembly.GetName().Version.ToString(); }
            catch (Exception) { return "0.0.0.0"; }
        }
    }
}
