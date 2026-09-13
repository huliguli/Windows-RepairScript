using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using WartungsToolbox.Kern;

namespace WartungsToolbox
{
    /// <summary>
    /// Die beiden Suchläufe, die neben dem Hauptweg stehen:
    ///
    ///   * "Wo steckt der Platz?"  (src/StorageScan.cs)
    ///   * "Einträge, die ins Leere zeigen"  (src/RegistryScan.cs)
    ///
    /// Beide suchen rein lesend, im Host, ohne Administratorrechte. Erst ein
    /// ausdrücklicher zweiter Klick auf einer Auswahl entfernt etwas, und das läuft
    /// seit 8.1 nicht mehr hier, sondern als Plan über den Helfer (docs/M2-ENTWURF.md,
    /// Abschnitte 3, 6 und 13): der Host schickt nur die Kennung der Maßnahme und die
    /// geprüften Kennungen der Auswahl, der Helfer prüft erneut, legt den
    /// Wiederherstellungspunkt an, schreibt die Sicherung und entfernt. Der UAC-Dialog
    /// kommt dabei aus HelferClient.Holen, einmal je Sitzung.
    ///
    /// Grundsatz für beide Löschwege: Die Oberfläche schickt KENNUNGEN, niemals
    /// Pfade oder Schlüsselnamen. Welcher Ordner und welcher Registrierungs-Eintrag
    /// dahintersteckt, entscheidet allein der Quelltext bzw. das gespeicherte Ergebnis
    /// des eigenen Laufs; der Helfer entscheidet es ein zweites Mal für sich.
    ///
    /// Zwei Regeln aus 7.3.2 gelten auch hier: nie still ablehnen, und jeder Lauf hat in
    /// app.log einen Anfang und ein Ende. Am Ende jedes Laufs holt NachlaufPruefen
    /// (ShellForm, Thread der Oberfläche) eine wartende geplante Wartung nach.
    /// </summary>
    public partial class ShellForm
    {
        Thread _scanThread;
        volatile bool _scanCancel;

        // Läuft gerade etwas, das ETWAS ENTFERNT? Dann darf das Fenster nicht einfach
        // zugehen und der Hauptweg nicht dazwischenfunken.
        volatile bool _scanEntfernt;

        // Darf der laufende Lauf abgebrochen werden? Suchen ja, Speicher-Aufräumen ja
        // (StorageScan.Aufraeumen hält zwischen den Kategorien an und meldet abgebrochen=true),
        // Registrierungs-Entfernen nein: dort sind Wiederherstellungspunkt, Sicherungsdatei
        // und Eingriff verzahnt, und die Oberfläche bietet dort auch keinen Knopf an.
        volatile bool _scanAbbrechbar = true;

        // Der Ausführer des gerade laufenden Plans (Aufräumen, Entfernen); null, solange
        // keiner läuft. CancelScan reicht den Abbruch darüber an den Helfer weiter: der
        // Kontext-Rückruf Abgebrochen allein erreicht einen Helfer hinter der Pipe nicht.
        volatile IAusfuehrer _scanAusfuehrer;

        // Ergebnisse des letzten Laufs. Die Oberfläche wählt daraus über Kennung bzw.
        // Position aus; sie bekommt nie die Möglichkeit, einen eigenen Pfad zu nennen.
        // Zuweisung erfolgt immer als ganze neue Liste (Referenzzuweisung ist unteilbar).
        // Nach einem verneinten UAC-Dialog bleiben beide stehen: die Oberfläche zeigt dann
        // weiter das vorhandene Ergebnis (Entwurf 13).
        List<RegistryScan.Fund> _lastFunde = new List<RegistryScan.Fund>();
        StorageScan.Befund _lastSpeicher;

        bool ScanRunning { get { return _scanThread != null && _scanThread.IsAlive; } }

        /// <summary>Wird gerade etwas entfernt? Dann ist der Vorgang nicht mehr anzuhalten.</summary>
        public bool ScanEntferntGerade { get { return _scanEntfernt && ScanRunning; } }

        /// <summary>
        /// Stoppt einen laufenden Suchlauf oder das Speicher-Aufräumen. Wird vom Befehl
        /// "cancel" mitbedient, den die Oberfläche auch für den Hauptweg schickt.
        ///
        /// Das Entfernen aus der Registrierung wird ausdrücklich NICHT gestoppt
        /// (_scanAbbrechbar): dort sind Sicherung und Eingriff verzahnt, und die Oberfläche
        /// sagt dem Nutzer auch, dass es sich nicht mehr anhalten lässt. Bis 8.0 stoppte
        /// jedes "cancel" auch ein Aufräumen, selbst wenn der Nutzer nur den Prüf-Ablauf
        /// gemeint hat; in der Phase 2 von 8.1 kehrte es beim Speicher-Aufräumen still
        /// zurück, obwohl die Oberfläche schon "Abbrechen" gezeigt und weggeschaltet hatte.
        ///
        /// Läuft der Plan schon beim Ausführer, bekommt auch der den Abbruch: lokal setzt
        /// das den Kontext, über die Pipe geht die Anfrage "abbrechen" an den Helfer.
        /// Während des UAC-Dialogs gibt es noch keinen Ausführer; dann greift das Flag,
        /// das ScanPlan vor der Übergabe des Plans prüft.
        /// </summary>
        public void CancelScan()
        {
            if (!ScanRunning) return;
            if (!_scanAbbrechbar)
            {
                AppLog.Info("Abbrechen während des Entfernens aus der Registrierung: der Vorgang läuft zu Ende, wie angekündigt.");
                return;
            }
            _scanCancel = true;
            IAusfuehrer a = _scanAusfuehrer;
            if (a == null) return;
            try { a.Abbrechen(); }
            catch (Exception ex) { AppLog.Warn("Abbruch des Suchlaufs beim Helfer: " + ex.Message); }
        }

        void ScanProgress(string scan, string text)
        {
            UiPost(new { type = "scanProgress", scan = scan, text = text });
        }

        // abgelehnt = true nur, wenn der Nutzer den UAC-Dialog verneint hat: die Oberfläche
        // zeigt dann einen Hinweis und behält das vorhandene Ergebnis, statt es zu leeren
        // (Entwurf 13). Der Host hält _lastSpeicher und _lastFunde ohnehin weiter.
        void ScanFehler(string scan, string message, bool abgelehnt = false)
        {
            UiPost(new { type = "scanError", scan = scan, message = message, abgelehnt = abgelehnt });
        }

        /// <summary>
        /// Läuft gerade eine Wartung, neben der nicht gelöscht werden darf? Hauptweg, Runner
        /// (Werkzeugkasten, Warteschlange, in der App nachgeholte geplante Wartung) und der
        /// --auto-Prozess im Hintergrund (Mutex, GeplanteWartungLaeuft in CheckFlow). Bis 8.1
        /// fehlte der Hintergrundlauf: ohne verbundenen Helfer gibt die App den Termin an
        /// --auto zurück, der Host war frei, und „Aufräumen“ mit „temp“ oder „updates“ lief
        /// neben DISM RestoreHealth (B14). grund ist der Satz für scanError; null, wenn frei.
        /// </summary>
        bool WartungLaeuft(string was, out string grund)
        {
            if (FlowRunning || (_runner != null && _runner.Running))
                grund = "Es läuft gerade eine Wartung. Bitte warten Sie, bis sie fertig ist.";
            else if (GeplanteWartungLaeuft())
                grund = "Die geplante Wartung läuft gerade im Hintergrund.";
            else
            {
                grund = null;
                return false;
            }
            AppLog.Info(was + " nicht gestartet: " + grund);
            return true;
        }

        /// <summary>
        /// Startet einen Hintergrund-Lauf. Zwei Läufe gleichzeitig gibt es nicht: sie
        /// würden sich beim Messen gegenseitig verfälschen. entfernt sperrt das Schließen
        /// des Fensters (OnClosingWhileBusy), abbrechbar entscheidet, ob "cancel" greift.
        /// </summary>
        bool StarteScan(string scan, ThreadStart arbeit, bool entfernt = false, bool abbrechbar = true)
        {
            if (ScanRunning)
            {
                ScanFehler(scan, "Es läuft bereits eine Suche. Bitte warten Sie, bis sie fertig ist.");
                return false;
            }
            _scanCancel = false;
            _scanEntfernt = entfernt;
            _scanAbbrechbar = abbrechbar;
            _scanThread = new Thread(delegate ()
            {
                try { arbeit(); }
                finally
                {
                    _scanEntfernt = false;
                    _scanAbbrechbar = true;
                    _scanAusfuehrer = null;
                    // Erst den eigenen Eintrag räumen, dann den Nachlauf anstoßen: NachlaufPruefen
                    // fragt EtwasLaeuft, und dieser Thread lebt noch, solange sein finally läuft.
                    // Nur der eigene Eintrag: ein schon gestarteter Nachfolger bleibt unberührt.
                    Interlocked.CompareExchange(ref _scanThread, null, Thread.CurrentThread);
                    NachlaufAnstossen();
                }
            })
            { IsBackground = true, Name = "suchlauf-" + scan };
            _scanThread.Start();
            return true;
        }

        /// <summary>
        /// Am Ende jedes Laufs (Entwurf 13): eine geplante Wartung, die während des Laufs
        /// angeklopft hat (_autoRunPending), holt NachlaufPruefen auf dem Thread der
        /// Oberfläche nach. Aus dem Hintergrund-Thread, deshalb BeginInvoke; ohne Fenster
        /// (Handle weg, Fenster schließt gerade) gibt es nichts nachzuholen.
        /// </summary>
        void NachlaufAnstossen()
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)NachlaufPruefen);
            }
            catch (Exception ex) { AppLog.Warn("Nachlauf nach dem Suchlauf ließ sich nicht anstoßen: " + ex.Message); }
        }

        // ================================================================ Plan über den Helfer

        /// <summary>
        /// Führt einen Plan über den Helfer aus: lokal, wenn dieser Prozess schon erhöht
        /// läuft, sonst über die Pipe zu einem per runas gestarteten Zweitprozess. Liefert
        /// null, wenn kein Helfer zu bekommen war; die Meldung an die Oberfläche ist dann
        /// schon unterwegs (scanError), nie eine stille Absage.
        ///
        /// ungewiss ist null, solange das Ergebnis verlässlich ist. Kam der Plan mit Exit 3
        /// und ohne einen einzigen Wert zurück (Verbindung verloren, Ausnahme im Ablauf),
        /// steht dort der Satz für den Nutzer: nach Entwurf 12 bringt der Helfer einen
        /// laufenden Auftrag auch ohne Host zu Ende, Dateien oder Einträge können also weg
        /// sein. Der Aufrufer zeigt den Satz und misst danach neu, statt "nicht ausgeführt"
        /// zu behaupten.
        ///
        /// Nur aus dem Scan-Thread rufen: HelferClient.Holen blockiert bis zu 60 s plus den
        /// UAC-Dialog. Zeilen der Art dim/normal sind Fortschrittstexte und gehen als
        /// scanProgress an die Oberfläche; alles andere (good/warn/bad) steht im app.log
        /// und im Protokoll des Helfers (lauf-&lt;planId&gt;.jsonl), das Ergebnis trägt es
        /// in Exit, Grund und Werten.
        /// </summary>
        PlanErgebnis ScanPlan(string scan, Plan plan, string ohneRechte, out string ungewiss)
        {
            ungewiss = null;

            // Der Hinweis nur, wenn der Dialog wirklich kommt: verbunden oder erhöht heißt
            // kein Dialog, und auf dem Abnahmeweg (--pipe) läuft der Helfer schon von außen.
            if (!HelferClient.Verbunden && !HelferClient.Abnahmeweg)
                ScanProgress(scan, "Windows fragt gleich nach Administratorrechten");

            string grund;
            IAusfuehrer a = HelferClient.Holen(true, out grund);
            if (a == null)
            {
                if (grund == Protokoll.Abgelehnt)
                {
                    // Der Nutzer hat im UAC-Dialog "Nein" gesagt: normaler Rückweg, kein Fehler.
                    AppLog.Info(plan.Titel + ": der UAC-Dialog wurde abgelehnt, nichts wurde verändert.");
                    ScanFehler(scan, ohneRechte, true);
                }
                else
                {
                    // Rechte erteilt, aber der Helfer kam technisch nicht zustande (keine Pipe
                    // binnen 60 s, Abnahme-Pipe stumm): das ist ein Fehler, keine Ablehnung.
                    string g = OhneSchlusspunkt(grund) ?? "ohne Angabe";
                    AppLog.Warn(plan.Titel + ": der Helfer ließ sich nicht starten: " + g + ".");
                    ScanFehler(scan, "Der Helfer ließ sich nicht starten: " + g + ".");
                }
                return null;
            }

            var k = new Helfer.Ausfuehrungskontext();
            bool abbruchNachgereicht = false;
            k.Zeile = delegate (string text, string art, int? prozent)
            {
                // Ein "cancel", das genau zwischen der Prüfung unten und dem Senden des Plans
                // ankam, hat den Helfer noch ohne laufenden Auftrag getroffen: mit der ersten
                // Zeile des Plans wird es nachgereicht (lokal reicht der Rückruf Abgebrochen).
                if (_scanCancel && !abbruchNachgereicht)
                {
                    abbruchNachgereicht = true;
                    try { a.Abbrechen(); } catch (Exception ex) { AppLog.Warn("Abbruch nachreichen: " + ex.Message); }
                }
                string t = (text ?? "").Trim();
                if (t.Length == 0) return;
                if (art == "dim" || art == "normal") ScanProgress(scan, t);
                else AppLog.Info("Helfer (" + plan.Titel + ", " + (art ?? "?") + "): " + t);
            };
            k.Abgebrochen = delegate { return _scanCancel; };

            // Zuerst den Ausführer bekannt machen, dann das Flag lesen: ein "cancel" zwischen
            // beiden erreicht so entweder den Ausführer (a.Abbrechen) oder die Prüfung hier.
            _scanAusfuehrer = a;
            try
            {
                // Ein Abbruch während des UAC-Dialogs (Entwurf 13): der Plan geht gar nicht erst
                // zum Helfer. Über die Pipe liest der Helfer den Kontext-Rückruf nicht, lokal
                // würde er den Plan mit Exit 4 zurückgeben; hier ist es auf beiden Wegen gleich.
                if (_scanCancel)
                {
                    AppLog.Info(plan.Titel + " abgebrochen, bevor der Plan an den Helfer ging; nichts wurde verändert.");
                    return new PlanErgebnis { PlanId = plan.Id, Exit = PlanErgebnis.AbgebrochenExit, Abgebrochen = true };
                }

                PlanErgebnis erg = a.Plan(plan, k);
                if (erg == null)
                    erg = new PlanErgebnis { PlanId = plan.Id, Exit = PlanErgebnis.Ausnahme, Grund = "Der Ausführer lieferte kein Ergebnis" };
                if (erg.Exit == PlanErgebnis.Ausnahme && erg.Werte.Count == 0)
                    ungewiss = UngewissSatz(erg, a, plan.Id);
                return erg;
            }
            catch (Exception ex)
            {
                AppLog.Error("Ausführer " + a.Art + " (" + plan.Titel + ")", ex);
                return new PlanErgebnis { PlanId = plan.Id, Exit = PlanErgebnis.Ausnahme, Grund = ex.Message };
            }
            finally { _scanAusfuehrer = null; }
        }

        /// <summary>
        /// Exit 3 ohne Werte: der Helfer hat kein Ergebnis geliefert, ob er etwas verändert hat,
        /// weiß nur sein Protokoll. Ist die Pipe dabei gestorben, heißt das Verbindung verloren;
        /// sonst (lokal, oder die Pipe lebt noch) nennt der Satz den Grund des Helfers.
        /// </summary>
        static string UngewissSatz(PlanErgebnis erg, IAusfuehrer a, string planId)
        {
            string protokoll = "lauf-" + (string.IsNullOrEmpty(erg.PlanId) ? planId : erg.PlanId) + ".jsonl";
            if (a.Art == "pipe" && !a.Lebt)
                return "Die Verbindung zum Helfer ging verloren; ob aufgeräumt wurde, steht im Protokoll " + protokoll + ".";
            return "Der Helfer lieferte kein Ergebnis (" + (OhneSchlusspunkt(erg.Grund) ?? "ohne Angabe")
                   + "); ob aufgeräumt wurde, steht im Protokoll " + protokoll + ".";
        }

        /// <summary>
        /// Warum ein Plan nicht bis zum Ergebnis kam, als Satzrest ohne Schlusspunkt:
        /// Grund des Helfers (Ablehnung, Ausnahme), sonst der Exit-Code.
        /// </summary>
        static string PlanGrund(PlanErgebnis erg)
        {
            if (erg.Abgebrochen || erg.Exit == PlanErgebnis.AbgebrochenExit) return "der Lauf wurde abgebrochen";
            return OhneSchlusspunkt(erg.Grund) ?? ("der Helfer meldete Exit " + erg.Exit + " ohne Ergebnis");
        }

        /// <summary>
        /// Grund des Helfers als Satzrest hinter einem Doppelpunkt: ohne Schlusspunkt (der Satz
        /// hier setzt seinen eigenen) und mit kleinem Anfangsbuchstaben, wenn das erste Wort
        /// kein Hauptwort und kein Name ist. Leer bleibt null.
        /// </summary>
        static string OhneSchlusspunkt(string grund)
        {
            if (string.IsNullOrEmpty(grund)) return null;
            string g = grund.TrimEnd('.', ' ');
            if (g.Length == 0) return null;
            return KleinNachDoppelpunkt(g);
        }

        // Wörter, mit denen ein Grund des Helfers beginnen kann und die hinter einem Doppelpunkt
        // klein geschrieben werden: Artikel, Fürwörter, Eigenschaftswörter, Verhältniswörter.
        // Alles andere behält seinen Großbuchstaben, denn Hauptwörter ("Verbindung zum Helfer
        // verloren", "Zugriff verweigert") und Namen (Windows, DISM) bleiben groß; ein Wort mit
        // weiterem Großbuchstaben (DISM, HKCU, PowerShell) ohnehin.
        static readonly HashSet<string> KleinAmSatzanfang = new HashSet<string>(StringComparer.Ordinal)
        {
            "der", "die", "das", "den", "dem", "des", "ein", "eine", "einen", "einem", "einer", "eines",
            "es", "kein", "keine", "keinen", "keinem", "keiner", "keines",
            "dieser", "diese", "dieses", "diesen", "diesem", "jeder", "jede", "jedes",
            "im", "in", "bei", "beim", "nach", "vor", "zu", "zum", "zur", "für", "ohne", "mit", "auf", "aus",
            "bitte", "nicht", "nichts", "mindestens", "weder", "mehr", "weniger",
            "unbekannt", "unbekannte", "unbekannter", "unbekanntes",
            "ungültig", "ungültige", "ungültiger", "ungültiges",
            "fehlend", "fehlende", "fehlender", "fehlendes", "leer", "leere", "leerer", "leeres",
        };

        static string KleinNachDoppelpunkt(string satz)
        {
            if (string.IsNullOrEmpty(satz) || !char.IsUpper(satz[0])) return satz;
            int ende = 0;
            while (ende < satz.Length && char.IsLetter(satz[ende])) ende++;
            string wort = satz.Substring(0, ende);
            for (int i = 1; i < wort.Length; i++)
                if (char.IsUpper(wort[i])) return satz;   // DISM, HKCU, PowerShell: Name oder Abkürzung
            if (!KleinAmSatzanfang.Contains(wort.ToLowerInvariant())) return satz;
            return char.ToLowerInvariant(satz[0]) + satz.Substring(1);
        }

        static int WertZahl(PlanErgebnis erg, string name, int sonst)
        {
            int z;
            string v = erg.Wert(name);
            return !string.IsNullOrEmpty(v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out z) ? z : sonst;
        }

        // ================================================================ Speicherplatz

        void StartStorageScan()
        {
            StarteScan("storage", delegate ()
            {
                try
                {
                    StorageScan.Befund b = StorageScan.Run(
                        t => ScanProgress("storage", t),
                        () => _scanCancel);

                    // Ein abgebrochener Lauf wird NICHT angezeigt und nicht gemerkt. Er
                    // sieht auf dem Bildschirm aus wie ein vollständiges Ergebnis, ist
                    // aber nur ein Bruchstück, und eine Auswahl darauf wäre blind.
                    if (_scanCancel) { _lastSpeicher = null; return; }

                    _lastSpeicher = b;
                    UiPost(b.ToJson());
                }
                catch (Exception ex)
                {
                    AppLog.Error("Speichersuche", ex);
                    ScanFehler("storage", "Die Suche nach dem belegten Platz ist fehlgeschlagen.");
                }
            });
        }

        /// <summary>
        /// Räumt die gewählten Kategorien weg, als Plan "speicher.aufraeumen" über den
        /// Helfer. Angenommen werden nur Kennungen, die im zuletzt angezeigten Ergebnis auch
        /// wirklich vorkamen; so kann die Oberfläche nichts auslösen, das der Nutzer nie
        /// gesehen hat. Der Helfer prüft die Kennungen gegen StorageScan.BekannteSchluessel
        /// ein zweites Mal und liefert den Bericht als JSON-Text (Wert speicher.bericht);
        /// der Host macht daraus wieder ein Objekt und postet es wie bisher (storageCleaned).
        /// Abbrechbar: StorageScan.Aufraeumen hält zwischen den Kategorien an.
        /// </summary>
        void StorageClean(Dictionary<string, object> m)
        {
            StorageScan.Befund letzter = _lastSpeicher;
            if (letzter == null)
            {
                ScanFehler("storage", "Bitte suchen Sie zuerst nach dem belegten Platz.");
                return;
            }

            var erlaubt = new HashSet<string>(
                letzter.Aufraeumbar.Where(p => p.Aufraeumbar).Select(p => p.Schluessel),
                StringComparer.OrdinalIgnoreCase);

            var gewaehlt = new List<string>();
            object arr;
            if (m.TryGetValue("keys", out arr) && arr is object[])
            {
                foreach (object o in (object[])arr)
                {
                    string k = o == null ? "" : o.ToString();
                    if (erlaubt.Contains(k) && !gewaehlt.Contains(k)) gewaehlt.Add(k);
                }
            }
            if (gewaehlt.Count == 0)
            {
                ScanFehler("storage", "Es war nichts ausgewählt, das sich aufräumen lässt.");
                return;
            }

            // Während DISM oder SFC laufen, wird nicht zusätzlich gelöscht: „temp“ enthält
            // Windows\Temp (DISM-Scratch), „updates“ SoftwareDistribution\Download (Quelle für
            // RestoreHealth). Das gilt auch für die geplante Wartung im Hintergrund (--auto).
            string wartung;
            if (WartungLaeuft("Aufräumen", out wartung))
            {
                ScanFehler("storage", wartung);
                return;
            }

            StarteScan("storage", delegate ()
            {
                try
                {
                    Plan plan = Plan.Neu("Aufräumen").Mit("speicher.aufraeumen", "schluessel", string.Join(";", gewaehlt.ToArray()));
                    // Die Anfangszeile des Hosts nennt die Plan-Id: so findet der Leser von app.log
                    // das passende lauf-<id>.jsonl des Helfers.
                    AppLog.Info("Aufräumen gestartet (Plan " + plan.Id + "): " + string.Join(", ", gewaehlt.ToArray()));
                    string ungewiss;
                    PlanErgebnis erg = ScanPlan("storage", plan,
                        "Ohne Administratorrechte kann nicht aufgeräumt werden. Sie können es jederzeit erneut versuchen.", out ungewiss);
                    if (erg == null) return;

                    // Der Bericht ist die Wahrheit über den Lauf: auch ein abgebrochener Lauf
                    // hat einen (abgebrochen=true) und wird angezeigt. Fehlt er, lief entweder
                    // nichts (Grund) oder das Ergebnis ist ungewiss (dann trotzdem neu messen).
                    string berichtJson = erg.Wert("speicher.bericht");
                    if (string.IsNullOrEmpty(berichtJson))
                    {
                        if (_scanCancel && (erg.Abgebrochen || erg.Exit == PlanErgebnis.AbgebrochenExit))
                        {
                            // Vom Nutzer abgebrochen, bevor der Helfer die erste Kategorie anfasste
                            // (UAC-Dialog, runas-Start, Warten auf die Pipe): nichts wurde verändert.
                            // Anders als beim Suchlauf bleibt die Oberfläche beim Aufräumen auf dem
                            // Wartebildschirm („Wird angehalten …“) und wartet auf storageCleaned
                            // oder scanError; ein stilles return ließ sie dort stehen (B23). Also
                            // derselbe Bericht wie vom Helfer, nur leer, danach die Neumessung
                            // wie im Normalfall. Ein fremder Abbruch fällt unten in den Grund.
                            AppLog.Info("Aufräumen beendet, ohne dass etwas gelöscht wurde (abgebrochen, bevor der Plan beim Helfer war).");
                            UiPost(new
                            {
                                type = "storageCleaned",
                                abgebrochen = true,
                                gesamtBytes = 0L,
                                gesamt = StorageScan.Menschlich(0),
                                posten = new object[0],
                            });
                        }
                        else if (ungewiss == null)
                        {
                            string g = PlanGrund(erg);
                            AppLog.Warn("Aufräumen ohne Bericht beendet (Exit " + erg.Exit + "): " + g + ".");
                            ScanFehler("storage", "Das Aufräumen wurde nicht ausgeführt: " + g + ".");
                            return;
                        }
                        else
                        {
                            AppLog.Warn("Aufräumen ohne Bericht beendet (Exit " + erg.Exit + "): " + ungewiss);
                            ScanFehler("storage", ungewiss);
                        }
                    }
                    else
                    {
                        object bericht = new JavaScriptSerializer().DeserializeObject(berichtJson);
                        AppLog.Info("Aufräumen beendet (Exit " + erg.Exit + ", " + erg.Sekunden.ToString("0.0", CultureInfo.InvariantCulture) + " s).");
                        UiPost(bericht);
                    }

                    // Danach neu messen, damit die Anzeige den Zustand NACH dem Aufräumen
                    // zeigt und nicht den davor. Bewusst ohne Abbruchprüfung: eine halbe
                    // Messung wäre schlechter als gar keine, und sie würde als
                    // vollständiges Ergebnis angezeigt. Rein lesend, deshalb auch im
                    // ungewissen Fall.
                    StorageScan.Befund b = StorageScan.Run(null, null);
                    _lastSpeicher = b;
                    UiPost(b.ToJson());
                }
                catch (Exception ex)
                {
                    AppLog.Error("Aufräumen", ex);
                    ScanFehler("storage", ex.Message);
                }
            }, true, true);
        }

        /// <summary>
        /// Zeigt einen der großen Brocken im Explorer. Die Oberfläche nennt nur seine
        /// Position in der zuletzt angezeigten Liste, nie einen Pfad.
        /// </summary>
        void OpenBrocken(int index)
        {
            StorageScan.Befund letzter = _lastSpeicher;
            if (letzter == null || index < 0 || index >= letzter.Brocken.Count) return;
            string pfad = letzter.Brocken[index].Pfad;
            if (string.IsNullOrEmpty(pfad)) return;
            // /select zeigt den Eintrag im übergeordneten Ordner markiert an, statt ihn zu
            // öffnen. Bei einer 40-GB-Datei ist das der freundlichere Weg.
            // Der Start läuft über die Oberfläche des Nutzers, damit der Explorer NICHT
            // die Administratorrechte dieser App erbt.
            Shell.OeffneImNutzerkontext("explorer.exe", "/select,\"" + pfad + "\"");
        }

        // ================================================================ Registrierung

        void StartRegistryScan()
        {
            StarteScan("registry", delegate ()
            {
                try
                {
                    List<RegistryScan.Fund> funde = RegistryScan.Run(
                        t => ScanProgress("registry", t),
                        () => _scanCancel);

                    // Wie beim Speicher: ein Bruchstück wird weder gezeigt noch gemerkt.
                    if (_scanCancel) { _lastFunde = new List<RegistryScan.Fund>(); return; }

                    _lastFunde = funde;
                    UiPost(new
                    {
                        type = "registryResult",
                        abgebrochen = false,
                        hinweise = RegistryScan.KategorieHinweise,
                        funde = funde.Select(f => f.ToJson()).ToArray(),
                    });
                }
                catch (Exception ex)
                {
                    AppLog.Error("Registrierungs-Prüfung", ex);
                    ScanFehler("registry", "Die Prüfung der Registrierung ist fehlgeschlagen.");
                }
            });
        }

        /// <summary>
        /// Entfernt die ausgewählten Einträge, als Plan "registrierung.entfernen" über
        /// den Helfer. Ablauf, in dieser Reihenfolge:
        ///
        ///   1. Auswahl gegen das gespeicherte Ergebnis prüfen (Kennungen, keine Pfade): hier
        ///   2. der Helfer sucht selbst erneut und nimmt nur Funde mit einer dieser Kennungen
        ///   3. Wiederherstellungspunkt anlegen (Parameter sicherung=1): im Helfer
        ///   4. .reg-Sicherung schreiben; schlägt sie fehl, wird nichts angefasst
        ///      (das erledigt RegistryScan.Entferne selbst, im Helfer)
        ///   5. entfernen und melden, wo die Sicherung liegt (Werte registrierung.*, sicherung.*)
        ///
        /// Nicht abbrechbar (abbrechbar=false): Punkt, Sicherung und Eingriff gehören zusammen.
        /// </summary>
        void RegistryClean(Dictionary<string, object> m)
        {
            List<RegistryScan.Fund> letzte = _lastFunde;
            if (letzte == null || letzte.Count == 0)
            {
                ScanFehler("registry", "Bitte lassen Sie zuerst die Registrierung prüfen.");
                return;
            }
            string wartung;
            if (WartungLaeuft("Registrierung aufräumen", out wartung))
            {
                ScanFehler("registry", wartung);
                return;
            }

            // Funde ohne Kennung können nicht ausgewählt worden sein. Sie hier still zu
            // übergehen ist billiger als ein Absturz mitten im Nachrichten-Empfang.
            var nachId = new Dictionary<string, RegistryScan.Fund>(StringComparer.Ordinal);
            foreach (RegistryScan.Fund f in letzte)
                if (f != null && !string.IsNullOrEmpty(f.Id)) nachId[f.Id] = f;

            var ids = new List<string>();
            object arr;
            if (m.TryGetValue("ids", out arr) && arr is object[])
            {
                foreach (object o in (object[])arr)
                {
                    string id = o == null ? "" : o.ToString();
                    if (!nachId.ContainsKey(id)) continue;
                    if (!ids.Contains(id)) ids.Add(id);
                }
            }
            if (ids.Count == 0)
            {
                ScanFehler("registry", "Es war nichts ausgewählt.");
                return;
            }

            StarteScan("registry", delegate ()
            {
                try
                {
                    Plan plan = Plan.Neu("Registrierung aufräumen")
                        .Mit("registrierung.entfernen", "kennungen", string.Join(";", ids.ToArray()), "sicherung", "1");
                    AppLog.Info("Registrierung aufräumen gestartet (Plan " + plan.Id + "): " + ids.Count + " Einträge gewählt.");
                    string ungewiss;
                    PlanErgebnis erg = ScanPlan("registry", plan,
                        "Ohne Administratorrechte kann nicht aufgeräumt werden. Sie können es jederzeit erneut versuchen.", out ungewiss);
                    if (erg == null) return;

                    // Ohne den Wert registrierung.entfernt hat die Maßnahme nie begonnen
                    // (abgelehnt, Ausnahme davor, fremder Abbruch): dann gibt es auch keine
                    // Abschlussnachricht, sondern den Grund. Dieser Weg ist nicht abbrechbar,
                    // die Oberfläche zeigt keinen Knopf: darum immer eine Meldung, nie nur eine
                    // Zeile in app.log. Ausnahme: das ungewisse Ergebnis (Verbindung verloren),
                    // dann Satz und trotzdem neu prüfen.
                    if (erg.Wert("registrierung.entfernt") == null)
                    {
                        if (ungewiss == null)
                        {
                            string g = PlanGrund(erg);
                            AppLog.Warn("Registrierung aufräumen ohne Ergebnis beendet (Exit " + erg.Exit + "): " + g + ".");
                            ScanFehler("registry", "Die Einträge wurden nicht entfernt: " + g + ".");
                            return;
                        }
                        AppLog.Warn("Registrierung aufräumen ohne Ergebnis beendet (Exit " + erg.Exit + "): " + ungewiss);
                        ScanFehler("registry", ungewiss);
                    }
                    else
                    {
                        int gewaehlt = WertZahl(erg, "registrierung.gewaehlt", ids.Count);
                        int entfernt = WertZahl(erg, "registrierung.entfernt", 0);
                        int fehlgeschlagen = WertZahl(erg, "registrierung.fehlgeschlagen", 0);
                        string sicherung = erg.Wert("registrierung.sicherung");
                        if (string.IsNullOrEmpty(sicherung)) sicherung = null;   // null = keine .reg, nichts verändert

                        // Der Punkt gilt als angelegt, wenn die Folgenummer nachher höher ist als
                        // vorher (Nachweis des Helfers); der Satz dazu kommt wörtlich mit.
                        int vorher = WertZahl(erg, "sicherung.vorher", -1);
                        int nachher = WertZahl(erg, "sicherung.nachher", -1);
                        bool punkt = vorher >= 0 && nachher > vorher;
                        string punktSatz = erg.Wert("sicherung.satz") ?? "";

                        AppLog.Info("Registrierung aufräumen beendet (Exit " + erg.Exit + "): " + entfernt + " von " + gewaehlt
                                    + " entfernt, " + fehlgeschlagen + " übersprungen"
                                    + (sicherung == null ? ", keine Sicherung" : ", Sicherung " + sicherung)
                                    + (punktSatz.Length > 0 ? "; " + punktSatz : "") + ".");
                        History.Add("Registrierung aufräumen",
                                    fehlgeschlagen > 0 || erg.Problem ? "warn" : "good",
                                    entfernt + " Eintrag/Einträge entfernt", erg.Sekunden);

                        UiPost(new
                        {
                            type = "registryCleaned",
                            gewaehlt = gewaehlt,
                            entfernt = entfernt,
                            fehlgeschlagen = fehlgeschlagen,
                            sicherung = sicherung,
                            sicherungspunkt = punkt,
                            sicherungspunktSatz = punktSatz,
                        });
                    }

                    // Neu prüfen, damit die Liste den Zustand danach zeigt. Ohne
                    // Abbruchprüfung: ein halbes Ergebnis wäre hier schlimmer als keines,
                    // weil es wie eine vollständige Liste aussieht. Rein lesend, deshalb
                    // auch im ungewissen Fall.
                    List<RegistryScan.Fund> funde = RegistryScan.Run(null, null);
                    _lastFunde = funde;
                    UiPost(new
                    {
                        type = "registryResult",
                        abgebrochen = false,
                        hinweise = RegistryScan.KategorieHinweise,
                        funde = funde.Select(f => f.ToJson()).ToArray(),
                    });
                }
                catch (Exception ex)
                {
                    AppLog.Error("Registrierung aufräumen", ex);
                    ScanFehler("registry", ex.Message);
                }
            }, true, false);
        }

        /// <summary>Öffnet den Ordner mit den .reg-Sicherungen im Explorer.</summary>
        void OpenRegBackup()
        {
            try
            {
                string ordner = RegistryScan.Sicherungsordner();
                if (ordner == null) { AppLog.Warn("Sicherungsordner liegt hinter einer Abzweigung und wird nicht geöffnet."); return; }
                Directory.CreateDirectory(ordner);
                Shell.OeffneImNutzerkontext("explorer.exe", "\"" + ordner + "\"");
            }
            catch (Exception ex) { AppLog.Warn("Sicherungsordner ließ sich nicht öffnen: " + ex.Message); }
        }
    }
}
