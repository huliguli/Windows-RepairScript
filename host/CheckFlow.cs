using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.AccessControl;
using System.Threading;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;

namespace WartungsToolbox
{
    /// <summary>
    /// Der Hauptweg der App in v8: "PC prüfen", "Windows-Dateien tief prüfen", "Beheben" und
    /// seit 8.1 "Mit Administratorrechten ergänzen".
    ///
    /// Prüfen = Sammler (Systembild, im eigenen Prozess, ohne PowerShell) + Regeln (Befunde).
    /// Das dauert Sekunden, nicht Minuten, und verändert nichts. Die Tiefenprüfung (DISM und
    /// SFC, lesend) ist ein eigener Schritt, den der Nutzer bewusst startet, weil sie Minuten
    /// dauert. "Beheben" gibt es nur noch für das, was ein Befund benennt: in Meilenstein 1
    /// sind das beschädigte Windows-Dateien aus der Tiefenprüfung.
    ///
    /// Seit 8.1 (Rechte-Modell B) startet der Host kein Werkzeug mehr selbst: Tiefenprüfung
    /// und Reparatur gehen als Plan (nur Kennungen) an den Helfer (helfer/Katalog.cs), der
    /// lokal (Host schon erhöht) oder als erhöhter Zweitprozess über die Pipe läuft.
    /// HelferClient.Holen löst dabei den UAC-Dialog aus; die Ablehnung ist ein normaler
    /// Rückweg mit Meldung. Die Messung läuft ohne Rechte im Host; Quellen, die Rechte
    /// brauchen, stehen als "zugriff" in der Fehlerliste und werden gezählt (adminFehlend).
    /// Ist ein Helfer verbunden, misst er sofort vollständig erhöht nach; sonst bietet die
    /// Oberfläche "ergaenzen" an. Vertrag: docs/M2-ENTWURF.md, Abschnitte 6, 9, 12 und 13.
    ///
    /// Zwei Regeln aus v7.3.2 gelten weiter: nie still ablehnen (StartAbgelehnt antwortet
    /// mit Grund), und jeder Lauf protokolliert Anfang und Ende.
    /// </summary>
    public partial class ShellForm
    {
        Thread _flowThread;
        Thread _glanceThread;
        volatile bool _flowCancel;

        // Der Ausführer, bei dem gerade ein Plan des Hauptwegs läuft (für den Abbruch);
        // null, solange keiner läuft. Während des UAC-Dialogs ist er noch null, dann wirkt
        // nur das Flag: der Plan geht dann gar nicht erst zum Helfer.
        volatile IAusfuehrer _flowAusfuehrer;

        // 0/1 je Plan des Hauptwegs (Interlocked): ein Abbruch, der den Helfer vor dem Plan
        // traf und dort verpuffte, wird mit der ersten Zeile des Plans genau einmal nachgereicht
        // (FlowAbbruchNachreichen, dasselbe Muster wie CommandRunner.AbbruchNachreichen).
        int _flowAbbruchNachgereicht;

        // Das zuletzt aufgenommene Systembild mit Befunden. Antworten auf Fragen laufen die
        // Regeln erneut darüber, ohne neu zu messen.
        Systembild _bild;
        List<BereichErgebnis> _ergebnisse = new List<BereichErgebnis>();
        Entscheidungen _entscheidungen;
        Protokoll _protokoll;

        // Anzahl der Quellen, die ohne Administratorrechte nichts geliefert haben (Art
        // "zugriff", je Quellenname einmal gezählt). 0 nach einer erhöhten Messung.
        int _adminFehlend;

        // Befund der Windows-Dateien aus der Tiefenprüfung (DISM/SFC). Getrennt geführt, weil
        // er nicht aus dem Systembild kommt, sondern aus der Ausgabe der beiden Werkzeuge.
        string _filesState = Zustand.Unknown;
        string _filesSummary;
        string _filesAdvice;
        bool _filesGeprueft;
        bool _filesNeustart;     // nur der Zweig "gefunden und repariert" setzt das

        /// <summary>Protokolle älter als 90 Tage werden beim Start entfernt (ein Lauf, eine Datei).</summary>
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
        ///
        /// Läuft noch der Abschalt-Countdown eines vorigen Laufs (_countdownAktiv, ShellForm),
        /// ist das kein Grund zur Absage: der neue Lauf bricht ihn ab, mit Zeile in der
        /// Konsole. Sonst führe Windows bei Sekunde 60 mitten in DISM herunter, während der
        /// Ablaufbildschirm „Bitte lassen Sie den PC dabei eingeschaltet“ verspricht (B18).
        /// Nur aus dem Thread der Oberfläche: CancelShutdown schreibt Konsole und Banner.
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
            else if (GeplanteWartungLaeuft())
                grund = "Die geplante Wartung läuft gerade im Hintergrund.";
            else
            {
                if (_countdownAktiv)
                {
                    AppLog.Info("Abschalt-Countdown abgebrochen, weil " + was + " startet.");
                    CancelShutdown();
                    Log("●  Der Abschalt-Countdown wurde für den neuen Lauf abgebrochen.", LogKind.Warn);
                }
                return false;
            }

            AppLog.Info(was + " nicht gestartet: " + grund);
            UiPost(new { type = "flowBusy", message = grund });
            return true;
        }

        /// <summary>
        /// Läuft der --auto-Prozess gerade (eigener, erhöhter Prozess ohne Fenster)? Seit 8.1
        /// gibt die offene App ohne verbundenen Helfer die geplante Wartung an ihn zurück; er
        /// hält dann den Mutex AutoRunner.MutexName, solange DISM und SFC laufen. Ein Klick
        /// auf „Tiefenprüfung“ würde sonst per UAC einen zweiten DISM-Lauf starten.
        /// Öffnen genügt als Nachweis: die Standard-DACL des Mutex erlaubt dem eigenen Konto
        /// das Öffnen auch aus dem nicht erhöhten Host (live gemessen am 13.09.2026, Halter
        /// erhöht, Prüfer über schtasks /RL LIMITED). „Vorhanden, aber nicht öffenbar“
        /// (anderes Konto, andere DACL) zählt trotzdem als „läuft“: der Mutex existiert nur,
        /// solange der Lauf lebt.
        /// </summary>
        static bool GeplanteWartungLaeuft()
        {
            Mutex m = null;
            try
            {
                if (!Mutex.TryOpenExisting(AutoRunner.MutexName, MutexRights.Synchronize, out m)) return false;
                return true;
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return true; }
            catch (Exception ex)
            {
                AppLog.Warn("Der Stand der geplanten Wartung ließ sich nicht prüfen: " + ex.Message);
                return false;
            }
            finally { if (m != null) m.Dispose(); }
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
            AppLog.Info("Prüfung gestartet.");
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
                AppLog.Error("Prüfung fehlgeschlagen", ex);
                FlowFehler("pruefen", "Die Prüfung konnte nicht abgeschlossen werden. Bitte starten Sie den PC neu und versuchen Sie es noch einmal.", ex.Message);
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

        /// <summary>
        /// Systembild aufnehmen. Läuft im Hintergrund-Thread. Der Sammler misst im Host ohne
        /// Rechte; was Rechte braucht, steht als "zugriff" in der Fehlerliste und wird gezählt.
        /// Ist schon ein Helfer verbunden (nach einer Maßnahme, oder der Host läuft erhöht),
        /// misst er sofort vollständig erhöht nach, ohne Dialog.
        ///
        /// neuesProtokoll: eine Prüfung ist ein eigener Lauf mit eigener Datei (Anfang „pruefen“).
        /// Tiefenprüfung und Reparatur messen am Ende nur nach und geben false: ihr Anfang
        /// steht schon im laufenden Protokoll, und das Ende soll in derselben Datei stehen
        /// (Entwurf 13), nicht in einer zweiten mit einem fremden Anfang.
        /// </summary>
        void Messen(bool neuesProtokoll = true)
        {
            if (neuesProtokoll || _protokoll == null)
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
            }
            else
            {
                _protokoll.Schreibe(Protokoll.Host, Protokoll.Messung, "pruefen", "Messwerte nach der Maßnahme neu aufgenommen",
                    new { erhoeht = Sammler.Quellen.Rechte.Erhoeht(), verbunden = HelferClient.Verbunden });
            }
            var sammler = new Sammler.Sammler(was => UiPost(new { type = "flowDetail", text = was + " wird gelesen" }), _protokoll);
            _bild = sammler.Erfassen();
            foreach (string z in sammler.Zeiten) FlowLog(z, LogKind.Dim);
            FehlerlisteLoggen(_bild);
            _adminFehlend = AdminFehlend(_bild);

            // Nach einer erhöhten Messung zählt AdminFehlend 0, auch wenn die Fehlerliste
            // zugriff-Einträge trägt (Richtlinie, siehe dort); dann bleibt es bei einer Messung.
            // Sonst nur, wenn der Helfer schon da ist: kein Dialog aus "Prüfen".
            if (_adminFehlend > 0) ErgaenzungOhneDialog();
        }

        /// <summary>
        /// Zahl der Quellen (verschiedene Quellennamen, nicht Einträge), die wegen fehlender
        /// Rechte nichts geliefert haben. Das ist die Zahl im Satz „N Werte brauchen einmal
        /// Administratorrechte“ der Oberfläche.
        ///
        /// Nach einer erhöhten Messung immer 0: zugriff-Einträge gibt es auch dann noch
        /// (Sicherheitsquelle in NurErhoeht, Richtlinie HideExclusionsFromLocalAdmins oder
        /// Sentinel „N/A“), aber mehr Rechte holen sie nicht herein. Sie zu zählen hieße,
        /// die Rechte-Karte nach jeder Ergänzung erneut zu zeigen, endlos (B20).
        /// </summary>
        static int AdminFehlend(Systembild bild)
        {
            if (bild == null || bild.Erhoeht) return 0;
            var quellen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in bild.Fehlerliste)
                if (f != null && f.Art == Fehler.Zugriff) quellen.Add(f.Quelle ?? "");
            return quellen.Count;
        }

        void FehlerlisteLoggen(Systembild bild)
        {
            if (bild == null) return;
            foreach (var f in bild.Fehlerliste)
                FlowLog("Quelle " + f.Quelle + ": " + f.Art + (f.Text != null ? " (" + f.Text + ")" : ""), LogKind.Dim);
        }

        /// <summary>
        /// Erhöht nachmessen, wenn das ohne UAC-Dialog geht (Helfer verbunden oder Host
        /// erhöht). Kein Dialog aus einer bloßen Prüfung heraus: den löst nur „ergaenzen“
        /// oder eine Maßnahme aus. Scheitert die Nachmessung, gilt das Bild ohne Rechte weiter.
        /// </summary>
        void ErgaenzungOhneDialog()
        {
            if (!HelferClient.Verbunden) return;
            string grund;
            IAusfuehrer a = HelferClient.Holen(false, out grund);
            if (a == null)
            {
                AppLog.Info("Erhöhte Nachmessung entfällt: " + (grund ?? "kein Ausführer") + ".");
                return;
            }
            ErhoehtMessen(a);
        }

        /// <summary>
        /// Misst über den Ausführer vollständig erhöht (Entwurf 0.4: das erhöhte Bild ersetzt
        /// das Bild ohne Rechte ganz, kein Zusammenführen). true, wenn _bild ersetzt wurde;
        /// false mit Protokollzeile, wenn die Messung scheiterte (das alte Bild bleibt).
        /// </summary>
        bool ErhoehtMessen(IAusfuehrer a)
        {
            int vorher = _adminFehlend;
            try
            {
                Systembild bild = a.Messen(was => UiPost(new { type = "flowDetail", text = was + " wird gelesen" }));
                if (bild == null) throw new InvalidOperationException("Der Ausführer lieferte kein Systembild.");
                _bild = bild;
                // Ehrlich zählen statt pauschal 0: ein Helfer ohne Rechte (Abnahmeweg --pipe
                // mit nicht erhöhter Shell) liefert weiter zugriff-Einträge.
                _adminFehlend = AdminFehlend(bild);
                FehlerlisteLoggen(bild);
                if (_protokoll != null)
                    _protokoll.Schreibe(Protokoll.Host, Protokoll.Messung, "ergaenzung", "Messwerte mit Administratorrechten ergänzt",
                        new { ausfuehrer = a.Art, erhoeht = bild.Erhoeht, vorher, nachher = _adminFehlend });
                AppLog.Info("Messwerte mit Administratorrechten ergänzt (" + a.Art + "): vorher " + vorher + ", jetzt " + _adminFehlend + " Quelle(n) ohne Rechte.");
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Erhöhte Messung fehlgeschlagen: " + ex.Message);
                if (_protokoll != null)
                    _protokoll.Schreibe(Protokoll.Host, Protokoll.FehlerArt, "ergaenzung",
                        "Die Messung mit Administratorrechten scheiterte; es gilt das Bild ohne Rechte", new { ausfuehrer = a.Art, grund = ex.Message });
                FlowLog("Messung mit Administratorrechten fehlgeschlagen: " + ex.Message, LogKind.Warn);
                return false;
            }
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

        // ---------------------------------------------------------------- Ergänzen

        /// <summary>
        /// Befehl „ergaenzen“ (UI → Host): die Quellen, die ohne Rechte nichts lieferten, mit
        /// Administratorrechten nachholen. Ein UAC-Dialog, dann misst der Helfer vollständig
        /// erhöht (10 s), die Regeln laufen erneut, Ergebnis mit mode "ergaenzt".
        /// ShellForm hängt den Befehl ein; hier steht nur der Handler.
        /// </summary>
        internal void Ergaenzen()
        {
            if (StartAbgelehnt("Ergänzung")) return;
            _flowCancel = false;
            AppLog.Info("Ergänzung mit Administratorrechten gestartet.");
            _flowThread = new Thread(ErgaenzenWorker) { IsBackground = true, Name = "ergaenzen" };
            _flowThread.Start();
        }

        void ErgaenzenWorker()
        {
            try
            {
                if (_protokoll == null) _protokoll = new Protokoll(Protokoll.NeueLaufId());
                _protokoll.Schreibe(Protokoll.Host, Protokoll.Anfang, "ergaenzen", "Ergänzung der Messwerte mit Administratorrechten gestartet",
                    new { adminFehlend = _adminFehlend, verbunden = HelferClient.Verbunden });
                UiPost(new { type = "flowStart", mode = "ergaenzen", total = 2 });
                ErstbefundAbwarten();

                // Der Dialog kommt nur, wenn noch kein Helfer da ist; sonst wäre die Zeile eine Lüge.
                bool dialog = !HelferClient.Verbunden && !HelferClient.Abnahmeweg;
                FlowStep(1, dialog ? "Windows fragt nach Administratorrechten" : "Ihr PC wird mit Administratorrechten gelesen");
                IAusfuehrer a = AusfuehrerHolen("die Ergänzung der Messwerte", "ergaenzen");
                if (a == null) return;
                if (_flowCancel) { FlowCancelled(); return; }

                if (dialog) UiPost(new { type = "flowDetail", text = "Ihr PC wird mit Administratorrechten gelesen" });
                if (!ErhoehtMessen(a))
                {
                    FlowFehler("ergaenzen", "Die Messung mit Administratorrechten ist fehlgeschlagen. Das Ergebnis ohne Administratorrechte bleibt gültig; Sie können es jederzeit erneut versuchen.", "Messen über " + a.Art + " gescheitert");
                    return;
                }
                if (_flowCancel) { FlowCancelled(); return; }

                FlowStep(2, "Die Messwerte werden bewertet");
                Bewerten();
                PublishResult("ergaenzt");
            }
            catch (Exception ex)
            {
                AppLog.Error("Ergänzung fehlgeschlagen", ex);
                FlowFehler("ergaenzen", "Die Ergänzung konnte nicht abgeschlossen werden. Das Ergebnis ohne Administratorrechte bleibt gültig; Sie können es jederzeit erneut versuchen.", ex.Message);
            }
        }

        // ---------------------------------------------------------------- Antworten

        /// <summary>
        /// Der Nutzer hat eine Frage beantwortet ("so lassen" oder "reparieren"). Die Antwort
        /// wird maschinenweit gemerkt, die Regeln laufen erneut über das vorhandene Systembild.
        ///
        /// Gesperrt ist das nur, solange der Hauptweg selbst läuft (oder der erste Blick beim
        /// Start): nur die schreiben _bild, _ergebnisse und _protokoll. Suchläufe, Werkzeuge
        /// und die geplante Wartung fassen nichts davon an; sie über StartAbgelehnt als Grund
        /// zu nehmen, ließ die Antwort bis zur nächsten Prüfung unbeantwortbar (B12).
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
            Thread g = _glanceThread;
            if (FlowRunning || (g != null && g.IsAlive))
            {
                string grund = FlowRunning ? "Es läuft bereits eine Prüfung oder Reparatur." : "Der erste Blick auf den PC läuft noch.";
                AppLog.Info("Antwort nicht gespeichert: " + grund);
                UiPost(new { type = "flowBusy", message = grund });
                return;
            }

            // Die Antwort bleibt im Speicher nur, wenn sie auch auf der Platte steht: sonst
            // gälte die Frage bis zum nächsten Start als beantwortet, der Knopf wäre weg, und
            // „nach dem nächsten Schritt mit Administratorrechten klappt es“ hätte keinen Weg.
            WartungsToolbox.Kern.Antwort vorher;
            Entscheidungen.Antworten.TryGetValue(frageId, out vorher);
            bool gespeichert = false;
            try
            {
                Entscheidungen.Setze(frageId, wert, Zeit.Utc(DateTime.UtcNow));
                Entscheidungen.Speichern();
                gespeichert = true;
                if (_protokoll != null)
                    _protokoll.Schreibe(Protokoll.Host, Protokoll.AntwortArt, frageId,
                        wert == Entscheidungen.Absicht ? "Sie haben bestätigt: so gewollt" : "Sie möchten das reparieren lassen", new { wert });
                AppLog.Info("Antwort gemerkt: " + frageId + " = " + wert);
                _ergebnisse = Alle.Pruefen(_bild, Entscheidungen);
                PublishResult("answer");
            }
            catch (Exception ex)
            {
                if (!gespeichert)
                {
                    if (vorher != null) Entscheidungen.Antworten[frageId] = vorher;
                    else Entscheidungen.Vergiss(frageId);
                }
                AppLog.Error("Antwort speichern", ex);
                if (_protokoll != null)
                    _protokoll.Schreibe(Protokoll.Host, Protokoll.FehlerArt, "entscheidungen",
                        "Die Antwort ließ sich nicht speichern", new { frageId, wert, grund = ex.Message });
                UiPost(new { type = "flowError", message = AntwortSpeicherfehler(ex) });
            }
        }

        /// <summary>
        /// Der Satz, wenn entscheidungen.json nicht zu schreiben war. „Bitte noch einmal“
        /// stand hier bis 8.1 für jeden Grund, und bei „Zugriff verweigert“ half noch einmal
        /// nie: nach dem ZIP-Update von 8.0 gehört die Datei den Administratoren, Users
        /// dürfen sie nicht ersetzen, bis der Helfer beim nächsten erhöhten Schritt die Rechte
        /// des Ordners setzt (Ablage.RechteSichern; B34, B57). Andere Gründe (Datei gesperrt,
        /// Platte voll) nennen den Grund und laden zum Wiederholen ein.
        /// </summary>
        static string AntwortSpeicherfehler(Exception ex)
        {
            if (ex is UnauthorizedAccessException)
                return "Ihre Antwort ließ sich nicht speichern. Die Antworten gehören noch der vorigen Fassung; "
                       + "nach dem nächsten Schritt mit Administratorrechten klappt es.";
            string grund = (ex.Message ?? "").Trim().TrimEnd('.', ' ');
            return "Ihre Antwort ließ sich nicht speichern"
                   + (grund.Length > 0 ? ": " + grund : "") + ". Bitte versuchen Sie es noch einmal.";
        }

        // ---------------------------------------------------------------- Tiefenprüfung

        void StartDeepCheck()
        {
            if (StartAbgelehnt("Tiefenprüfung")) return;
            _flowCancel = false;
            // Der alte Dateibefund bleibt bis zum Start des Plans stehen: sagt der Nutzer im
            // UAC-Dialog „Nein“, gilt das letzte Ergebnis weiter (DeepWorker räumt erst danach).
            AppLog.Info("Tiefenprüfung gestartet.");
            _flowThread = new Thread(DeepWorker) { IsBackground = true, Name = "tiefenpruefung" };
            _flowThread.Start();
        }

        /// <summary>
        /// Plan „tiefenpruefung“ (DISM ScanHealth, SFC verifyonly) über den Helfer. Die beiden
        /// Schritte des Balkens kommen als Fortschritt aus dem Helfer; die Deutung der Ausgabe
        /// (EvaluateFiles) bleibt im Host.
        /// </summary>
        void DeepWorker()
        {
            try
            {
                if (_protokoll == null) _protokoll = new Protokoll(Protokoll.NeueLaufId());
                _protokoll.Schreibe(Protokoll.Host, Protokoll.Anfang, "tiefenpruefung", "Tiefenprüfung der Windows-Dateien gestartet");
                UiPost(new { type = "flowStart", mode = "deep", total = 2 });

                IAusfuehrer a = AusfuehrerHolen("die Tiefenprüfung", "tiefenpruefung");
                if (a == null) return;
                if (_flowCancel) { FlowCancelled(); return; }

                // Erst jetzt, mit Ausführer in der Hand, den alten Befund räumen: bis hierher
                // konnte der Lauf noch ohne Wirkung enden (Ablehnung, Abbruch im Dialog), und
                // dann darf das Ergebnis nicht „Problem“ mit „Noch nicht geprüft“ zeigen.
                _filesState = Zustand.Unknown;
                _filesSummary = null;
                _filesAdvice = null;
                _filesGeprueft = false;

                PlanErgebnis erg = PlanAusfuehren(a, Plan.Neu("Tiefenprüfung").Mit("tiefenpruefung"), "tiefenpruefung");
                if (!PlanErgebnisPruefen(erg, "tiefenpruefung", "die Tiefenprüfung",
                        "Die Tiefenprüfung konnte nicht abgeschlossen werden. Bitte starten Sie den PC neu und versuchen Sie es noch einmal."))
                    return;

                EvaluateFiles(erg.Wert("dism.ausgabe"), erg.Wert("sfc.ausgabe"));
                _filesGeprueft = true;
                _protokoll.Schreibe(Protokoll.Regeln, Protokoll.BefundArt, "windows.dateien", _filesSummary,
                    new { zustand = _filesState, dismExit = erg.Wert("dism.exit"), sfcExit = erg.Wert("sfc.exit") });
                ErstbefundAbwarten();
                // Nachmessung im laufenden Protokoll (kein neuer Lauf): Anfang „tiefenpruefung“
                // und Ende „deep“ stehen in derselben Datei.
                if (_bild == null) Messen(false);
                // Der Helfer ist jetzt verbunden: fehlten Werte ohne Rechte, holt er sie ohne
                // zweiten Dialog nach (10 s nach 10 Minuten Tiefenprüfung).
                else if (_adminFehlend > 0) ErgaenzungOhneDialog();
                Bewerten();
                AbbruchNachPruefungMelden();
                PublishResult("deep");
            }
            catch (Exception ex)
            {
                AppLog.Error("Tiefenprüfung fehlgeschlagen", ex);
                FlowFehler("tiefenpruefung", "Die Tiefenprüfung konnte nicht abgeschlossen werden. Bitte starten Sie den PC neu und versuchen Sie es noch einmal.", ex.Message);
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
            // Befund, Satz und Rat bleiben stehen, bis der Plan wirklich läuft: nach „Nein“ im
            // UAC-Dialog gilt weiter, was die Tiefenprüfung fand. EvaluateFiles räumt selbst.
            AppLog.Info("Reparatur gestartet.");
            _flowThread = new Thread(FixWorker) { IsBackground = true, Name = "beheben" };
            _flowThread.Start();
        }

        /// <summary>
        /// Plan „dateien.reparieren“ über den Helfer: Wiederherstellungspunkt (Grundsatz 2,
        /// Nachweis über die Folgenummer vorher/nachher), DISM RestoreHealth, SFC scannow; die
        /// drei Schritte des Balkens kommen aus dem Helfer, Schritt 4 (Messung danach) aus dem Host.
        /// </summary>
        void FixWorker()
        {
            try
            {
                if (_protokoll == null) _protokoll = new Protokoll(Protokoll.NeueLaufId());
                _protokoll.Schreibe(Protokoll.Host, Protokoll.Anfang, "beheben", "Reparatur der Windows-Dateien gestartet");
                UiPost(new { type = "flowStart", mode = "fix", total = 4 });

                IAusfuehrer a = AusfuehrerHolen("die Reparatur", "beheben");
                if (a == null) return;
                if (_flowCancel) { FlowCancelled(); return; }

                PlanErgebnis erg = PlanAusfuehren(a, Plan.Neu("Reparatur").Mit("dateien.reparieren"), "beheben");
                if (!PlanErgebnisPruefen(erg, "beheben", "die Reparatur",
                        "Die Reparatur konnte nicht abgeschlossen werden. Bitte starten Sie den PC neu und versuchen Sie es noch einmal."))
                    return;

                // Der Satz zum Wiederherstellungspunkt (drei Fälle, auch „keiner vorhanden,
                // Systemschutz aus“) steht schon als Zeile des Helfers in Konsole und Bericht;
                // hier zählt nur die Folgenummer für den Nachweis.
                int vorher = Ganz(erg.Wert("sicherung.vorher"));
                int nachher = Ganz(erg.Wert("sicherung.nachher"));
                bool punktAngelegt = vorher >= 0 && nachher > vorher;

                EvaluateFiles(erg.Wert("dism.ausgabe"), erg.Wert("sfc.ausgabe"));
                // Ein Schritt mit 3010 heißt Neustart, auch wenn die SFC-Deutung ihn nicht nennt.
                if (erg.Wert("neustart") == "1") _filesNeustart = true;
                _protokoll.Schreibe(Protokoll.Host, Protokoll.Nachweis, "windows.dateien", _filesSummary,
                    new { zustand = _filesState, punktAngelegt, sicherungVorher = erg.Wert("sicherung.vorher"), sicherungNachher = erg.Wert("sicherung.nachher"),
                          sicherungSatz = erg.Wert("sicherung.satz"), dismExit = erg.Wert("dism.exit"), sfcExit = erg.Wert("sfc.exit"), neustart = erg.Wert("neustart") });

                // Nach dem Eingriff neu messen, damit das Ergebnis den Zustand DANACH zeigt.
                // Der Helfer ist verbunden, also misst Messen gleich erhöht nach; im laufenden
                // Protokoll, damit Anfang „beheben“ und Ende „fix“ in derselben Datei stehen.
                FlowStep(4, "Das Ergebnis wird zusammengestellt");
                Messen(false);
                Bewerten();
                AbbruchNachPruefungMelden();
                PublishResult("fix");
            }
            catch (Exception ex)
            {
                AppLog.Error("Reparatur fehlgeschlagen", ex);
                FlowFehler("beheben", "Die Reparatur konnte nicht abgeschlossen werden. Bitte starten Sie den PC neu und versuchen Sie es noch einmal.", ex.Message);
            }
        }

        /// <summary>Ganze Zahl aus einem Helfer-Wert; -1, wenn leer oder unlesbar.</summary>
        static int Ganz(string s)
        {
            int v;
            return !string.IsNullOrEmpty(s) && int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : -1;
        }

        // ---------------------------------------------------------------- Helfer im Hauptweg

        /// <summary>
        /// Besorgt den Ausführer (UAC-Dialog, wenn noch kein Helfer verbunden ist). Liefert
        /// null, wenn es nicht ging; dann ist die Meldung schon draußen (flowError, AppLog,
        /// Protokoll-Ende), der Aufrufer kehrt nur noch zurück. Blockiert bis zu 60 s plus
        /// Dialog: nur aus dem Hintergrund-Thread des Hauptwegs.
        /// </summary>
        IAusfuehrer AusfuehrerHolen(string was, string kennung)
        {
            // Der Hinweis nur, wenn der Dialog wirklich kommt: verbunden oder erhöht heißt kein
            // Dialog, und auf dem Abnahmeweg (--pipe) läuft der Helfer schon von außen.
            // Ohne Schlusspunkt: die Oberfläche hängt an jedes flowDetail selbst „ …“ an.
            if (!HelferClient.Verbunden && !HelferClient.Abnahmeweg)
                UiPost(new { type = "flowDetail", text = "Windows fragt gleich nach Administratorrechten" });

            string grund;
            IAusfuehrer a = HelferClient.Holen(true, out grund);
            if (a != null)
            {
                AppLog.Info("Ausführer für " + was + ": " + a.Art + ".");
                return a;
            }
            if (grund == Protokoll.Abgelehnt)
            {
                // Der Nutzer hat im UAC-Dialog „Nein“ gesagt: normaler Rückweg, kein Fehler.
                // Als Feld gemeldet (abgelehnt), nicht am Satzanfang erkannt (Entwurf 13).
                AppLog.Info("Der UAC-Dialog wurde abgelehnt, " + was + " läuft nicht.");
                FlowFehler(kennung, "Ohne Administratorrechte kann " + was + " nicht laufen. Sie können es jederzeit erneut versuchen.", Protokoll.Abgelehnt, true);
            }
            else
            {
                // Rechte erteilt, aber der Helfer kam technisch nicht zustande (Exit 7, keine
                // Pipe binnen 60 s, Abnahme-Pipe stumm): ein Fehler, keine Ablehnung.
                string g = string.IsNullOrEmpty(grund) ? "ohne Angabe" : grund.TrimEnd('.', ' ');
                AppLog.Warn("Der Helfer ließ sich nicht starten (" + was + "): " + g + ".");
                FlowFehler(kennung, "Der Helfer ließ sich nicht starten: " + g + ".", grund);
            }
            return null;
        }

        /// <summary>
        /// Plan beim Ausführer laufen lassen; die Rückrufe des Kontexts zeigen auf Konsole
        /// (Zeile), Ablaufbalken (Prozent, Fortschritt) und Protokoll des Hosts. Liefert
        /// immer ein Ergebnis, nie null.
        /// </summary>
        PlanErgebnis PlanAusfuehren(IAusfuehrer a, Plan plan, string kennung)
        {
            _flowAbbruchNachgereicht = 0;
            var k = new Helfer.Ausfuehrungskontext();
            k.Zeile = delegate (string text, string art, int? prozent)
            {
                if (_flowCancel) FlowAbbruchNachreichen(a);
                if (prozent.HasValue)
                {
                    UiPost(new { type = "flowPercent", percent = prozent.Value });
                    if (string.IsNullOrEmpty(text)) return;   // reine Fortschrittszeile: keine Konsolenzeile
                }
                FlowLog(text ?? "", ZeilenArt(art));
            };
            // Der Balken zählt die Schritte der Maßnahme (Tiefenprüfung 2, Reparatur 4); ein
            // Fortschritt mit gesamt 1 wäre nur die Planzeile und ersetzt keinen Schritt.
            k.Fortschritt = delegate (int schritt, int gesamt, string label)
            {
                if (_flowCancel) FlowAbbruchNachreichen(a);
                if (gesamt > 1) FlowStep(schritt, label ?? "");
            };
            k.Abgebrochen = delegate { return _flowCancel; };
            // Lokal schreibt der Helfer-Code in dieses Protokoll und trägt dessen Lauf-Id als
            // Plan-Id (Ausfuehrung.PlanAusfuehren); die Übergabezeile nennt deshalb schon sie,
            // sonst zeigte sie auf eine Datei, die es nie gibt. Über die Pipe bleibt die Id aus
            // Plan.Neu: der Helfer führt sein eigenes lauf-<planId>.jsonl (Entwurf 12), hier
            // bleibt dann der Verweis darauf.
            k.Protokoll = _protokoll;
            if (a.Art == "lokal") plan.Id = _protokoll.LaufId;
            _protokoll.Schreibe(Protokoll.Host, Protokoll.Plan, kennung, plan.Titel + " an den Ausführer übergeben",
                new { planId = plan.Id, ausfuehrer = a.Art, schritte = plan.Schritte.Count });

            PlanErgebnis erg;
            _flowAusfuehrer = a;
            try
            {
                // „Abbrechen“ während des UAC-Dialogs traf noch keinen Ausführer, nur das Flag.
                // Erst der Ausführer ins Feld, dann das Flag lesen: so sieht CancelFlow entweder
                // den Ausführer (und bricht dort ab) oder wir sehen sein Flag. Über die Pipe
                // kennt der Helfer das Host-Flag nicht; ohne diese Prüfung liefe DISM 10 Minuten.
                if (_flowCancel)
                    erg = new PlanErgebnis { PlanId = plan.Id, Exit = PlanErgebnis.AbgebrochenExit, Abgebrochen = true, Grund = "Vor dem Start abgebrochen" };
                else
                    erg = a.Plan(plan, k);
            }
            catch (Exception ex)
            {
                AppLog.Error("Ausführer " + a.Art + " (" + kennung + ")", ex);
                erg = new PlanErgebnis { PlanId = plan.Id, Exit = PlanErgebnis.Ausnahme, Grund = ex.Message };
            }
            finally { _flowAusfuehrer = null; }
            if (erg == null)
                erg = new PlanErgebnis { PlanId = plan.Id, Exit = PlanErgebnis.Ausnahme, Grund = "Der Ausführer lieferte kein Ergebnis" };

            _protokoll.Schreibe(Protokoll.Host, Protokoll.Schritt, kennung, plan.Titel + " ist beim Ausführer beendet (Exit " + erg.Exit + ")",
                new { planId = erg.PlanId, ausfuehrer = a.Art, exit = erg.Exit, sekunden = (int)erg.Sekunden, problem = erg.Problem, abgebrochen = erg.Abgebrochen, grund = erg.Grund });
            return erg;
        }

        /// <summary>
        /// Ein „Abbrechen“, das zwischen dem Lesen von _flowCancel oben und dem Eintreffen des
        /// Plans beim Helfer kam, hat dort nichts vorgefunden („es läuft nichts“, helfer/Pipe.cs)
        /// und ist verpufft: CancelFlow sieht den Ausführer schon, der Helfer aber noch keinen
        /// Auftrag. Lokal wirkt k.Abgebrochen, über die Pipe kennt der Helfer das Flag nicht.
        /// Deshalb geht der Abbruch mit der ersten Zeile oder Fortschrittsmeldung des Plans
        /// noch einmal raus, höchstens einmal je Plan (Interlocked); ein doppelter Abbruch ist
        /// für den Helfer harmlos, ein verlorener kostet 10 bis 20 Minuten DISM und SFC (B17).
        /// Runner (CommandRunner.AbbruchNachreichen) und Suchläufe (ScanPlan) tun dasselbe.
        /// </summary>
        void FlowAbbruchNachreichen(IAusfuehrer a)
        {
            if (Interlocked.Exchange(ref _flowAbbruchNachgereicht, 1) != 0) return;
            AppLog.Info("Abbruch des Hauptwegs beim Ausführer " + a.Art + " noch einmal angemeldet (erste Zeile des Plans nach dem Abbrechen).");
            try { a.Abbrechen(); }
            catch (Exception ex) { AppLog.Warn("Abbruch nachreichen: " + ex.Message); }
        }

        /// <summary>
        /// Ergebnis des Plans in den Hauptweg übersetzen: Exit 4 → abgebrochen, Exit 2 →
        /// Meldung mit Grund (Absage des Helfers), Exit 3 → die bisherige Fehlermeldung.
        /// true nur, wenn die Werte ausgewertet werden dürfen (Exit 0 oder 1).
        /// </summary>
        bool PlanErgebnisPruefen(PlanErgebnis erg, string kennung, string was, string fehlerSatz)
        {
            if (_flowCancel || erg.Abgebrochen || erg.Exit == PlanErgebnis.AbgebrochenExit)
            {
                FlowCancelled();
                return false;
            }
            string grund = string.IsNullOrEmpty(erg.Grund) ? null : erg.Grund.TrimEnd('.', ' ');
            if (erg.Exit == PlanErgebnis.Abgelehnt)
            {
                AppLog.Warn(was + " abgelehnt: " + (grund ?? "ohne Angabe"));
                FlowFehler(kennung, "Der Helfer hat " + was + " abgelehnt: " + (grund ?? "ohne Angabe") + ".", erg.Grund);
                return false;
            }
            if (erg.Exit == PlanErgebnis.Ausnahme)
            {
                AppLog.Warn(was + " mit Fehler beendet: " + (grund ?? "ohne Angabe"));
                FlowFehler(kennung, fehlerSatz, erg.Grund);
                return false;
            }
            return true;
        }

        // Zeilenart des Helfers -> LogKind der Oberfläche. Unbekanntes wird Normal, nie verworfen.
        static LogKind ZeilenArt(string art)
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

        // ---------------------------------------------------------------- Ablaufhilfen

        void FlowStep(int index, string label)
        {
            UiPost(new { type = "flowStep", index = index, label = label });
        }

        /// <summary>
        /// Konsolenzeile aus dem Hintergrund-Thread: ganz auf den Thread der Oberfläche, dort
        /// schreibt Log (ShellForm) in den Berichtspuffer und an WebView2. Der Puffer ist ein
        /// StringBuilder, den der UI-Thread selbst beschreibt und liest (Bericht); ein Append
        /// aus dem Pipe-Leser oder dem Hauptweg daneben wäre ein Wettlauf. WebView2 nimmt
        /// Nachrichten ohnehin nur von seinem eigenen Thread an.
        /// </summary>
        void FlowLog(string text, LogKind k)
        {
            if (_web == null || !_web.IsHandleCreated) return;
            try { _web.BeginInvoke((Action)delegate { Log(text, k); }); }
            catch (Exception ex) { AppLog.Warn("Konsolenzeile nicht zustellbar: " + ex.Message); }
        }

        void FlowCancelled()
        {
            AppLog.Info("Ablauf abgebrochen.");
            if (_protokoll != null) _protokoll.Schreibe(Protokoll.Host, Protokoll.Ende, "abgebrochen", "Abgebrochen. Ihrem PC ist nichts passiert.");
            // Wer abbricht, will erst recht nicht, dass der PC gleich ausgeht.
            _pendingPost = "none";
            UiPost(new { type = "flowCancelled" });
            NachlaufPruefenAnstossen();
        }

        /// <summary>
        /// Ein Lauf endet mit Fehler oder Absage: Protokoll-Ende (jeder Lauf hat eins), kein
        /// Nachlauf (der PC darf nach einem Fehler nicht ausgehen), Meldung an die Oberfläche.
        /// abgelehnt = true heißt „Nein“ im UAC-Dialog: die Oberfläche zeigt dann nur einen
        /// Hinweis am Rand und behält das vorhandene Ergebnis (Entwurf 13).
        /// </summary>
        void FlowFehler(string kennung, string meldung, string grund, bool abgelehnt = false)
        {
            if (_protokoll != null)
                _protokoll.Schreibe(Protokoll.Host, Protokoll.Ende, kennung, meldung, new { grund, abgelehnt });
            _pendingPost = "none";
            UiPost(new { type = "flowError", message = meldung, abgelehnt });
            NachlaufPruefenAnstossen();
        }

        /// <summary>
        /// Hat die geplante Wartung während des Laufs angeklopft (WM_WW_RUNAUTO, der --auto-
        /// Prozess hat übergeben und ist weg), holt NachlaufPruefen (ShellForm, UI-Thread) sie
        /// jetzt nach. Bis 8.1 kannte nur Done im Runner das Flag; nach dem Hauptweg fiel der
        /// Termin still aus, obwohl der Nutzer „wartet, bis die laufende Aktion abgeschlossen
        /// ist“ gelesen hatte. Jeder Ausgang ruft das: Ergebnis, Fehler, Abbruch.
        /// </summary>
        void NachlaufPruefenAnstossen()
        {
            if (_web == null || !_web.IsHandleCreated) return;
            try { _web.BeginInvoke((Action)delegate { HauptwegAuslaufenLassen(); NachlaufPruefen(); }); }
            catch (Exception ex) { AppLog.Warn("Nachholen der geplanten Wartung ließ sich nicht anstoßen: " + ex.Message); }
        }

        /// <summary>
        /// Nur im UI-Thread, vor NachlaufPruefen. Der Anstoß kommt aus dem Hauptweg-Thread
        /// selbst, dessen letzte Tat er ist; bis die Nachricht hier ankommt, kann der Thread
        /// noch als lebend gelten (FlowRunning true). NachlaufPruefen soll einen beendeten
        /// Hauptweg sehen, keinen auslaufenden: sonst hielte eine Prüfung auf „läuft noch“
        /// den Termin erneut zurück, und niemand holte ihn danach nach. Höchstens 2 s; der
        /// Thread hat nach dem Anstoß nichts Blockierendes mehr vor sich.
        /// </summary>
        void HauptwegAuslaufenLassen()
        {
            Thread t = _flowThread;
            if (t == null || t == Thread.CurrentThread || !t.IsAlive) return;
            if (!t.Join(2000)) AppLog.Warn("Der Hauptweg-Thread lief nach seinem Ende noch 2 s weiter.");
        }

        /// <summary>
        /// Ende eines fertigen Laufs im Oberflächen-Thread: erst die wartende geplante Wartung
        /// nachholen (NachlaufPruefen), dann das, was der Nutzer für "wenn alles fertig ist"
        /// gewählt hat. Der Countdown zeigt ein Banner zum Abbrechen, deshalb im UI-Thread.
        /// Läuft jetzt die nachgeholte Wartung, bleibt der Wunsch gemerkt und greift nach ihr
        /// (Done im Runner): mitten in DISM darf der PC nicht ausgehen.
        /// </summary>
        void NachlaufStarten()
        {
            if (_web == null || !_web.IsHandleCreated) return;
            try
            {
                _web.BeginInvoke((Action)delegate
                {
                    HauptwegAuslaufenLassen();
                    NachlaufPruefen();
                    if (_pendingPost == "none") return;
                    // Läuft der Runner jetzt, hat NachlaufPruefen die Wartung eben gestartet;
                    // Done des Runners holt den Countdown nach (_pendingPost bleibt gesetzt).
                    if (_runner != null && _runner.Running)
                    {
                        AppLog.Info("Abschalt-Countdown zurückgestellt: die geplante Wartung läuft jetzt in der App.");
                        Log("●  Der Abschalt-Countdown startet erst nach der geplanten Wartung.", LogKind.Warn);
                        return;
                    }
                    ScheduleShutdown();
                    _pendingPost = "none";
                });
            }
            catch (Exception ex) { AppLog.Warn("Nachlauf ließ sich nicht starten: " + ex.Message); }
        }

        /// <summary>
        /// „Abbrechen“ im Hauptweg: Flag setzen und den Ausführer benachrichtigen (lokal:
        /// Prozessbaum beenden; Pipe: Anfrage abbrechen, der Helfer beendet den Baum). Während
        /// des UAC-Dialogs gibt es noch keinen Ausführer, dann greift nur das Flag.
        /// </summary>
        public void CancelFlow()
        {
            if (!FlowRunning) return;
            _flowCancel = true;
            IAusfuehrer a = _flowAusfuehrer;
            if (a != null)
            {
                try { a.Abbrechen(); }
                catch (Exception ex) { AppLog.Warn("Abbruch im Hauptweg: " + ex.Message); }
            }
        }

        /// <summary>„Abbrechen“ gedrückt, obwohl nichts (mehr) lief: immer eine Antwort.</summary>
        void FlowIdle()
        {
            AppLog.Info("Abbrechen gedrückt, es lief nichts mehr; Oberfläche zurückgesetzt.");
            UiPost(new { type = "flowIdle" });
        }

        /// <summary>
        /// „Abbrechen“ kam erst während der Nachmessung (Sekunden), als DISM und SFC schon
        /// fertig waren: das Ergebnis liegt vor und wird gezeigt. Der Nutzer hat aber gerade
        /// „Vorgang abbrechen?“ bestätigt, deshalb steht der Grund in der Konsole statt einer
        /// stillen Ergebnisseite. Und wie in FlowCancelled entfällt der Abschalt-Countdown:
        /// wer abbricht, hat „Ihrem PC passiert dabei nichts“ gelesen und darf nicht daneben
        /// „Der PC wird in 60 s heruntergefahren“ vorfinden (B13).
        /// </summary>
        void AbbruchNachPruefungMelden()
        {
            if (!_flowCancel) return;
            AppLog.Info("Abbruch kam nach der Prüfung; das Ergebnis wird gezeigt.");
            FlowLog("●  Der Abbruch kam erst nach der Prüfung; das fertige Ergebnis wird gezeigt.", LogKind.Warn);
            if (_pendingPost != "none")
            {
                AppLog.Info("Abschalt-Countdown entfällt wegen des Abbruchs.");
                FlowLog("●  Der Abschalt-Countdown entfällt wegen des Abbruchs.", LogKind.Warn);
            }
            _pendingPost = "none";
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
            string sfcTxt = Deutung("sfc", sfcOut, out sfcGut);
            bool dismGut;
            string dismTxt = Deutung("dism", dismOut, out dismGut);

            string sfcZustand = sfcTxt == null ? Zustand.Unknown : (sfcGut ? Zustand.Ok : Zustand.Bad);
            string dismZustand = dismTxt == null ? Zustand.Unknown : (dismGut ? Zustand.Ok : Zustand.Warn);
            // Unbekannt zählt hier wie ein eigener Rang zwischen ok und warn: ohne SFC-Ergebnis
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

        /// <summary>
        /// Explain.ForOutput wählt die Deutung am Dateinamen des Werkzeugs. Der Host führt seit
        /// 8.1 keinen Werkzeugnamen mehr als Literal (er startet keins; Test 8.3 greppt danach),
        /// deshalb entsteht der Name aus dem Werte-Präfix des Helfers ("dism", "sfc") plus Endung.
        /// </summary>
        static string Deutung(string praefix, string ausgabe, out bool gut)
        {
            return Explain.ForOutput(praefix + ".exe", ausgabe ?? "", out gut);
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
                History.Add(mode == "fix" ? "Wartung" : (mode == "deep" ? "Tiefenprüfung" : (mode == "ergaenzt" ? "Prüfung (ergänzt)" : "Prüfung")),
                            overall == Zustand.Ok ? "good" : (overall == Zustand.Bad ? "bad" : "warn"),
                            problems == 0 ? "Ohne Befund" : problems + " Punkt(e) gefunden", 0);
                AppLog.Info("Ablauf " + mode + " beendet, Gesamturteil " + overall + ", " + problems + " Befund(e), " + fragen + " Frage(n), " + _adminFehlend + " Quelle(n) ohne Rechte.");
                if (_protokoll != null)
                    _protokoll.Schreibe(Protokoll.Host, Protokoll.Ende, mode,
                        "Fertig: " + (problems == 0 ? "kein Problem gefunden" : problems + " Punkt(e) gefunden") + (fragen > 0 ? ", " + fragen + " Frage(n) offen" : "")
                        + (_adminFehlend > 0 ? ", " + _adminFehlend + " Quelle(n) brauchen Administratorrechte" : ""),
                        new { overall, problems, fragen, adminFehlend = _adminFehlend });
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
                adminFehlend = _adminFehlend,
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
            UiPost(new { type = "lastChecks", checks = zeilen.ToArray(), fragen = Alle.OffeneFragen(_ergebnisse).Count(), adminFehlend = _adminFehlend });
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
                    // Auch der erste Blick ist ein Lauf mit Anfang und Ende (Regel aus 7.3.2).
                    if (_protokoll != null)
                        _protokoll.Schreibe(Protokoll.Host, Protokoll.Ende, "erstbefund",
                            "Erster Blick fertig: " + Alle.Probleme(_ergebnisse) + " Punkt(e), " + _adminFehlend + " Quelle(n) brauchen Administratorrechte",
                            new { probleme = Alle.Probleme(_ergebnisse), adminFehlend = _adminFehlend });
                }
                catch (Exception ex)
                {
                    // Nie still: die Startseite zeigt sonst für immer "wird geladen".
                    AppLog.Error("Erstbefund", ex);
                    if (_protokoll != null)
                        _protokoll.Schreibe(Protokoll.Host, Protokoll.Ende, "erstbefund", "Erster Blick abgebrochen: " + ex.Message);
                    UiPost(new { type = "lastChecks", checks = new object[0], fragen = 0, adminFehlend = 0,
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
            sb.AppendLine("Windows-Wartung " + typeof(ShellForm).Assembly.GetName().Version.ToString(3) + ", Bericht vom " + DateTime.Now.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture));
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
                    foreach (var fe in _bild.Fehlerliste)
                        sb.AppendLine("  " + fe.Quelle + " [" + fe.Art + "] " + fe.Text
                                      + (fe.Art == Fehler.Zugriff ? " (mit Administratorrechten ergänzbar)" : ""));
                }
            }
            if (_protokoll != null && _protokoll.Zeilen.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("PROTOKOLL (Datei: " + (_protokoll.Pfad ?? "nur im Speicher") + ")");
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
