using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    /// Seit 8.2 (Meilenstein 3) laufen hier auch die Maßnahmen des Kern-Katalogs (Abschnitt
    /// „Maßnahmen“ unten): eine Karte trägt eine Kennung, die Oberfläche fragt die Vorschau ab,
    /// der Plan geht mit den Parametern des Befunds an den Helfer, danach Nachmessung, Regeln,
    /// Nachweis aus dem Kern und das Ergebnis mit Nachweiskarte; der Rückweg spielt eine
    /// Sicherungsdatei zurück. Vertrag: docs/M3-ENTWURF.md, Abschnitte 4, 8.9, 8.10 und 9.
    ///
    /// Seit 8.3 (Meilenstein 4) kommen die Maßnahmen der Stufe 3 dazu (docs/M4-ENTWURF.md,
    /// Abschnitte 4 und 10.3): die Vorschau trägt den Satz, was verloren geht (verlust), und
    /// massnahmeAusfuehren läuft für Stufe 3 nur mit bestaetigt = true aus dem Häkchen der
    /// Vorschau. Die Nachweise lesen alle Werte des Helfers mit Punkt im Namen aus dem
    /// Wörterbuch p; das Dateiurteil (EvaluateFiles) kommt aus dism.zustand und sfc.zustand
    /// statt aus Konsolentext; die Techniker-Ansicht (technikerAnsicht, technikerText) zeigt
    /// alle Befunde mit Messwert, Schwelle und Quelle, ohne etwas zu ändern.
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
        /// Die drei Wege, die „cancel“ anhält: der Hauptweg, die beiden Suchläufe und der
        /// CommandRunner (Werkzeugkasten, Warteschlange, geplante Wartung). Ohne das Update:
        /// dessen Leiste hat keinen Abbrechen-Knopf, und ein „Abbrechen“ auf einem stehen
        /// gebliebenen Ablaufbildschirm soll die Oberfläche auch während eines Updates zurücksetzen.
        /// </summary>
        bool AbbrechbarerLauf
        {
            get { return FlowRunning || ScanRunning || (_runner != null && _runner.Running); }
        }

        /// <summary>
        /// Läuft irgendwo schon etwas? Die drei abbrechbaren Wege greifen auf denselben PC zu;
        /// zwei DISM-Instanzen auf demselben Windows-Abbild wären fatal. Seit 8.3 zählt das
        /// Update dazu (M4-Entwurf 4): sein Dateitausch läuft als Plan update.anwenden auf
        /// derselben Helfer-Pipe. Klopfte die geplante Wartung in diesen Sekunden an
        /// (OnScheduledRunRequested), startete sie sonst sofort, der Helfer meldete
        /// „beschäftigt“, der --auto-Prozess hatte schon übergeben, und der Termin fiel aus;
        /// mit dem Flag wird sie vorgemerkt, und UpdateFreigeben holt sie nach.
        /// </summary>
        bool EtwasLaeuft
        {
            get { return AbbrechbarerLauf || UpdateLaeuft; }
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
            // Seit 8.3 läuft der Dateitausch des Updates als Plan über denselben Ausführer und
            // dasselbe Protokoll (ShellForm.FinishUpdate); ein zweiter Plan daneben bekäme vom
            // Helfer „beschäftigt“, und die Nachmessung schriebe in den laufenden Update-Lauf.
            else if (UpdateLaeuft)
                grund = "Das Update läuft gerade; bis es fertig ist, startet kein anderer Lauf.";
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

                EvaluateFiles(erg);
                _filesGeprueft = true;
                _protokoll.Schreibe(Protokoll.Regeln, Protokoll.BefundArt, "windows.dateien", _filesSummary,
                    new { zustand = _filesState, dismZustand = erg.Wert(WertDismZustand), dismHresult = erg.Wert(WertDismHresult),
                          sfcZustand = erg.Wert(WertSfcZustand), sfcExit = erg.Wert(WertSfcExit) });
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

                // Das Urteil liest neustart selbst (3010 oder „pending“ in CBS.log heißt Neustart,
                // auch wenn sfc.zustand ihn nicht nennt).
                EvaluateFiles(erg);
                _protokoll.Schreibe(Protokoll.Host, Protokoll.Nachweis, "windows.dateien", _filesSummary,
                    new { zustand = _filesState, punktAngelegt, sicherungVorher = erg.Wert("sicherung.vorher"), sicherungNachher = erg.Wert("sicherung.nachher"),
                          sicherungSatz = erg.Wert("sicherung.satz"), dismZustand = erg.Wert(WertDismZustand), dismHresult = erg.Wert(WertDismHresult),
                          dismRepariert = erg.Wert(WertDismRepariert), sfcZustand = erg.Wert(WertSfcZustand), sfcExit = erg.Wert(WertSfcExit), neustart = erg.Wert(WertNeustart) });

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

        // ---------------------------------------------------------------- Maßnahmen (Meilenstein 3)

        // Kennungen mit fester Sonderbehandlung im Host (docs/M3-ENTWURF.md, Abschnitte 4 und 9.2).
        const string KennungRueckweg = "rueckweg";
        const string KennungGeraetAktivieren = "geraet.aktivieren";
        const string KennungUpdateEinstellungen = "update.einstellungen.oeffnen";

        // Werte-Schlüssel des Helfers, die in das Wörterbuch des Nachweises wandern (Entwurf 8.10):
        // unverändert, unter demselben Namen, der Kern liest sie dort. Seit 8.3 wandern ALLE
        // Werte mit Punkt im Namen generisch hinein (WerteMitPunktUebernehmen, M4-Entwurf 10.3:
        // sicherung.vorher, nachweis.bestanden, nachweis.satz, rueckweg.kennung,
        // rueckweg.parameter.<name>, rueckweg.moeglich, dism.*, sfc.*, update.*); die Konstanten
        // bleiben für die Stellen, die einen Wert einzeln lesen, und für neustart (ohne Punkt,
        // wird eigens übernommen).
        const string WertSicherungDatei = "sicherung.datei";
        const string WertNeustart = "neustart";
        const string WertNachweisSatz = "nachweis.satz";
        const string WertRueckwegKennung = "rueckweg.kennung";
        const string WertRueckwegMoeglich = "rueckweg.moeglich";

        // Werte des Helfers zur Tiefenprüfung und Reparatur (helfer/Massnahmen/System.cs, M4-Entwurf
        // 3.4): das Dateiurteil (EvaluateFiles) liest sie, nicht mehr den Konsolentext.
        const string WertDismZustand = "dism.zustand";      // healthy | repairable | nonrepairable | fehler
        const string WertDismHresult = "dism.hresult";      // 0x + 8 Hex, nur bei fehler von Belang
        const string WertDismRepariert = "dism.repariert";  // "1" nach gelungenem RestoreHealth (nur dateien.reparieren)
        const string WertSfcZustand = "sfc.zustand";        // ohneBefund | repariert | nichtRepariert | fehler
        const string WertSfcExit = "sfc.exit";

        // Schlüssel unter „parameter“ einer Sicherungsdatei, der das Profil nennt, aus dem ein
        // HKCU-Vorwert stammt (helfer/Massnahmen/Leistung.cs): der Verlauf zeigt „Rückgängig“ nur
        // für das eigene Profil (M4-Entwurf 4, 10.3), der Helfer lehnt fremde Profile ohnehin ab.
        const string SicherungProfilSchluessel = Helfer.LeistungMassnahmen.ProfilSchluessel;

        // Die letzte bad-Zeile des zuletzt über PlanAusfuehren gelaufenen Plans (getrimmt, null
        // ohne bad-Zeile): das Update nennt sie bei Exit 1 als Grund (M4-Entwurf 10.3), weil
        // PlanErgebnis.Grund dort nur „Problem“ sagt. Geschrieben im Rückruf des Ausführers,
        // gelesen nach dem Plan auf demselben Thread.
        volatile string _planLetzteBadZeile;

        // Dateiname einer Sicherung, wie ihn die Kennung rueckweg annimmt (Vertrag Abschnitt 1):
        // Buchstaben, Ziffern, Punkt, Unterstrich, Bindestrich, Endung .json, keine Pfadzeichen.
        // Der Helfer prüft Ort und Herkunft der Datei beim Rückweg selbst noch einmal.
        static readonly Regex SicherungNameRx = new Regex(@"^[A-Za-z0-9._-]{1,200}\.json$", RegexOptions.Compiled);
        // Aus dem Namen lässt sich die Hin-Kennung lesen: yyyyMMdd-HHmmss-<kennung>-<4hex>.json
        // (helfer/Massnahmen/Gemeinsam.cs). Nur für Titel und Knopf; der Nachweis nach dem Rückweg
        // nimmt rueckweg.kennung aus den Werten des Helfers.
        static readonly Regex SicherungKennungRx = new Regex(@"^\d{8}-\d{6}-(?<k>[a-z0-9.]+)-[0-9a-f]{4}\.json$", RegexOptions.Compiled);

        /// <summary>Der Befund mit diesem Schlüssel im letzten Ergebnis; null, wenn keiner da ist.</summary>
        Befund BefundFinden(string schluessel)
        {
            if (string.IsNullOrEmpty(schluessel)) return null;
            var liste = _ergebnisse;
            if (liste == null) return null;
            return Alle.AlleBefunde(liste).FirstOrDefault(b => b != null && b.Schluessel == schluessel);
        }

        /// <summary>
        /// Prüft eine Maßnahme aus der Oberfläche gegen Katalog, Befund und Antworten (Entwurf
        /// 9.1 und 9.2): die Kennung muss am gefundenen Befund hängen (nie eine freie Kennung aus
        /// der Oberfläche), die Art muss ausfuehren sein, jeder Pflichtparameter aus
        /// Massnahme.Parameter muss am Befund stehen, und nur diese Namen wandern in den Plan;
        /// geraet.aktivieren braucht dazu die Antwort „reparieren“ auf die Frage des Geräts
        /// (Frage-Kennung geraet:&lt;instanzId&gt;:22, kern/Regeln/Hardware.cs). Liefert null,
        /// wenn alles passt, sonst den Satz für die Oberfläche. Nur lesend; „gerade läuft etwas“
        /// prüft der Aufrufer (Vorschau über LaufendesWas, Start über StartAbgelehnt).
        /// </summary>
        string MassnahmePruefen(string kennung, string schluessel, out Massnahme m, out Befund b, out Dictionary<string, string> parameter)
        {
            m = string.IsNullOrEmpty(kennung) ? null : Massnahmen.Finde(kennung);
            b = BefundFinden(schluessel);
            parameter = new Dictionary<string, string>(StringComparer.Ordinal);
            if (m == null)
                return "Die Maßnahme „" + (kennung ?? "") + "“ steht nicht im Katalog dieser Fassung; sie wird nicht ausgeführt.";
            if (_bild == null || b == null)
                return "Der Befund zu dieser Karte liegt nicht mehr vor; bitte prüfen Sie den PC noch einmal und starten Sie die Maßnahme danach erneut.";
            if (b.Massnahmen == null || !b.Massnahmen.Contains(m.Kennung))
                return "Der Befund „" + b.Titel + "“ schlägt die Maßnahme „" + m.Titel + "“ nicht vor; sie wird nicht ausgeführt.";
            if (m.Art == Massnahme.ArtAnsicht)
                return "„" + m.Titel + "“ ist eine Ansicht und kein Eingriff; sie wird über „Ansehen“ geöffnet, nicht ausgeführt.";
            if (m.Art != Massnahme.ArtAusfuehren)
                return "„" + m.Titel + "“ ist ein Rat (Stufe " + m.Stufe + "), den dieses Programm nicht ausführt.";
            foreach (string name in m.Parameter ?? new string[0])
            {
                string wert = null;
                if (b.Parameter == null || !b.Parameter.TryGetValue(name, out wert) || string.IsNullOrEmpty(wert))
                    return "Dem Befund fehlt der Wert „" + name + "“, den „" + m.Titel + "“ braucht; bitte prüfen Sie den PC noch einmal.";
                parameter[name] = wert;
            }
            if (m.Kennung == KennungGeraetAktivieren)
            {
                string instanzId;
                parameter.TryGetValue("instanzId", out instanzId);
                if (Entscheidungen.AntwortAuf("geraet:" + instanzId + ":22") != Entscheidungen.Reparieren)
                    return "Das Gerät wird nur eingeschaltet, wenn Sie die Frage dazu mit „wieder einschalten“ beantwortet haben; diese Antwort fehlt.";
            }
            return null;
        }

        /// <summary>
        /// Befehl „massnahmeVorschau“ (UI → Host, Entwurf 9.1): die Antwort ist immer die
        /// gleichnamige Nachricht, mit ausfuehrbar = false und grund, wenn der Start scheitern
        /// würde (nie stumm: die Oberfläche zeigt dann den Grund statt „Jetzt ausführen“).
        /// Nur lesend, im Thread der Oberfläche; parameter sind die Werte des Befunds, die in
        /// den Plan gingen. Seit 8.3 (M4-Entwurf 4): verlust ist der Satz der Stufe 3 mit den
        /// Zahlen des Befunds (Massnahme.VerlustMit; „?“ für eine Zahl, die der Befund nicht
        /// liefert), null unter Stufe 3; läuft gerade etwas (Prüfung, Suchlauf, Werkzeug,
        /// geplante Wartung, Update), ist keine Maßnahme ausführbar, Stufe 3 erst recht nicht.
        /// </summary>
        internal void MassnahmeVorschau(string kennung, string schluessel)
        {
            Massnahme m; Befund b; Dictionary<string, string> parameter;
            string grund = MassnahmePruefen(kennung, schluessel, out m, out b, out parameter);
            if (grund == null)
            {
                string laeuft = LaufendesWas();
                if (laeuft != null) grund = "Gerade läuft " + laeuft + "; die Maßnahme startet erst, wenn der Lauf zu Ende ist.";
            }
            if (grund != null) AppLog.Info("Vorschau „" + (kennung ?? "") + "“ zu " + (schluessel ?? "") + ": nicht ausführbar (" + grund + ")");
            // Befund.Parameter ist null-sicher zu lesen (Entwurf 8.9); ohne Befund bleibt ein
            // leeres Wörterbuch, damit jeder Platzhalter zu „?“ wird statt roh stehen zu bleiben.
            string verlust = null;
            if (m != null && m.Stufe >= Massnahme.StufeUnumkehrbar)
                verlust = m.VerlustMit(b != null && b.Parameter != null ? b.Parameter : new Dictionary<string, string>());
            UiPost(new
            {
                type = "massnahmeVorschau",
                kennung = kennung ?? "",
                schluessel = schluessel ?? "",
                titel = m != null ? m.Titel : (kennung ?? ""),
                wasPassiert = m != null ? m.WasPassiert : "",
                wasBleibt = m != null ? m.WasBleibt : "",
                risiko = m != null ? m.Risiko : "",
                verlust = verlust,
                sicherung = m != null ? m.Sicherung : Massnahme.SicherungKeine,
                rueckweg = m != null ? m.Rueckweg : Massnahme.RueckwegKeiner,
                neustart = m != null && m.Neustart,
                dauer = m != null ? m.DauerSekunden : 0,
                stufe = m != null ? m.Stufe : 0,
                parameter = parameter,
                ausfuehrbar = grund == null,
                grund = grund,
            });
        }

        /// <summary>
        /// Befehl „massnahmeAusfuehren“ (UI → Host, Entwurf 9.2): erst dieselbe Prüfung wie die
        /// Vorschau (Absage als flowError, die Oberfläche steht schon auf dem Ablaufbildschirm),
        /// dann StartAbgelehnt wie beim Hauptweg, dann der Lauf im eigenen Thread (MassnahmeWorker).
        /// Stufe 3 (M4-Entwurf 4) verlangt bestaetigt = true, das Häkchen „Ich habe verstanden, was
        /// verloren geht“ aus der Vorschau; ohne das Feld (alte Oberfläche, fremde Nachricht) läuft
        /// nichts, und der Grund steht im app.log. Unter Stufe 3 wird das Feld nicht gelesen.
        /// </summary>
        internal void MassnahmeAusfuehren(string kennung, string schluessel, bool bestaetigt)
        {
            Massnahme m; Befund b; Dictionary<string, string> parameter;
            string grund = MassnahmePruefen(kennung, schluessel, out m, out b, out parameter);
            if (grund == null && m.Stufe >= Massnahme.StufeUnumkehrbar && !bestaetigt)
                grund = "Die Bestätigung fehlt; Maßnahmen der Stufe 3 laufen nur nach dem Häkchen in der Vorschau.";
            if (grund != null)
            {
                AppLog.Warn("Maßnahme „" + (kennung ?? "") + "“ zu " + (schluessel ?? "") + " nicht gestartet: " + grund);
                UiPost(new { type = "flowError", message = grund });
                return;
            }
            if (StartAbgelehnt("Maßnahme „" + m.Titel + "“")) return;
            _flowCancel = false;
            AppLog.Info("Maßnahme „" + m.Titel + "“ (" + m.Kennung + ", Stufe " + m.Stufe + (m.Stufe >= Massnahme.StufeUnumkehrbar ? ", bestätigt" : "") + ") gestartet, Befund " + b.Schluessel + ".");
            Massnahme fm = m; Befund fb = b; Dictionary<string, string> fp = parameter; bool fbest = bestaetigt;
            _flowThread = new Thread(delegate () { MassnahmeWorker(fm, fb, fp, fbest); }) { IsBackground = true, Name = "massnahme" };
            _flowThread.Start();
        }

        /// <summary>
        /// Plan mit genau einem Schritt (Kennung plus die geprüften Parameter) über den Helfer,
        /// dann Nachmessung (der Helfer ist verbunden, also erhöht), Regeln, Nachweis aus dem
        /// Kern über dem neuen Bild und das Ergebnis mit Nachweiskarte. Drei Schritte des
        /// Balkens: Maßnahme, Nachmessung, Ergebnis (Entwurf 9.1).
        /// </summary>
        void MassnahmeWorker(Massnahme m, Befund b, Dictionary<string, string> parameter, bool bestaetigt)
        {
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            string was = "die Maßnahme „" + m.Titel + "“";
            try
            {
                UiPost(new { type = "flowStart", mode = "massnahme", total = 3, titel = m.Titel });
                // Erst den ersten Blick abwarten: der tauscht _protokoll aus, und der Anfang dieses
                // Laufs soll in der Datei stehen, in der auch sein Ende steht.
                ErstbefundAbwarten();
                if (_protokoll == null) _protokoll = new Protokoll(Protokoll.NeueLaufId());
                _protokoll.Schreibe(Protokoll.Host, Protokoll.Anfang, m.Kennung, "Maßnahme „" + m.Titel + "“ gestartet",
                    new { befund = b.Schluessel, stufe = m.Stufe, bestaetigt, sicherung = m.Sicherung, parameter = ParameterText(parameter) });

                // Der Dialog kommt nur, wenn noch kein Helfer da ist; sonst wäre die Zeile eine Lüge.
                bool dialog = !HelferClient.Verbunden && !HelferClient.Abnahmeweg;
                FlowStep(1, dialog ? "Windows fragt nach Administratorrechten" : m.Titel);
                IAusfuehrer a = AusfuehrerHolen(was, m.Kennung);
                if (a == null) return;
                if (_flowCancel) { FlowCancelled(); return; }
                if (dialog) FlowStep(1, m.Titel);

                var plan = Plan.Neu(m.Titel);
                var schritt = new PlanSchritt { Kennung = m.Kennung };
                foreach (var kv in parameter) schritt.Parameter[kv.Key] = kv.Value;
                plan.Schritte.Add(schritt);
                PlanErgebnis erg = PlanAusfuehren(a, plan, m.Kennung);
                if (!MassnahmeErgebnisPruefen(erg, m.Kennung, was, MassnahmeFehlerSatz(m.Titel), "Maßnahme: " + m.Titel, uhr.Elapsed.TotalSeconds)) return;

                // Nach dem Eingriff neu messen, damit das Ergebnis den Zustand DANACH zeigt; im
                // laufenden Protokoll, damit Anfang und Ende in derselben Datei stehen.
                FlowStep(2, "Ihr PC wird erneut gelesen");
                Messen(false);
                FlowStep(3, "Das Ergebnis wird zusammengestellt");
                Bewerten();

                // Nachweis: Befund-Parameter plus die Werte des Helfers unter ihrem Namen (8.10);
                // seit 8.3 jeder Wert mit Punkt (dism.zustand, sfc.zustand, update.verborgen.vorher,
                // nachweis.bestanden, rueckweg.moeglich, …), dazu neustart (M4-Entwurf 10.3). Ein
                // Befund-Parameter gewinnt gegen einen gleichnamigen Wert.
                var p = new Dictionary<string, string>(parameter, StringComparer.Ordinal);
                WerteMitPunktUebernehmen(erg, p, parameter);
                WerteUebernehmen(erg, p, WertNeustart);
                Nachweis nw = NachweisBilden(m, m.Titel, erg, p);
                string sicherung = SicherungName(erg.Wert(WertSicherungDatei));
                bool rueckwegMoeglich = sicherung != null && erg.Wert(WertRueckwegMoeglich) != "0";
                MassnahmeAbschliessen("massnahme", m.Kennung, m.Titel, b.Schluessel, "Maßnahme: " + m.Titel,
                    erg, nw, uhr.Elapsed.TotalSeconds, sicherung, rueckwegMoeglich, null);
            }
            catch (Exception ex)
            {
                AppLog.Error("Maßnahme " + m.Kennung + " fehlgeschlagen", ex);
                FlowFehler(m.Kennung, MassnahmeFehlerSatz(m.Titel), ex.Message);
            }
        }

        /// <summary>
        /// Befehl „massnahmeRueckweg“ (UI → Host, Entwurf 9.2): Plan rueckweg mit dem Dateinamen
        /// der Sicherung. Der Host nimmt nur einen Namen nach dem Muster der Sicherungsdateien
        /// (keine Pfadzeichen) und nur, wenn die Datei noch unter sicherungen\massnahmen liegt;
        /// Ort und Herkunft (Besitzer, Rechte, Hardlinks) prüft der Helfer erneut. Der Titel
        /// „Rückgängig: &lt;Hin-Titel&gt;“ kommt aus der Kennung im Dateinamen.
        /// </summary>
        internal void MassnahmeRueckweg(string sicherung)
        {
            string name = SicherungName(sicherung);
            if (name == null)
            {
                AppLog.Warn("Rückweg nicht gestartet: ungültiger Dateiname „" + (sicherung ?? "") + "“.");
                UiPost(new { type = "flowError", message = "Der Name der Sicherungsdatei ist ungültig (erlaubt sind Buchstaben, Ziffern, Punkt, Unterstrich und Bindestrich mit der Endung .json); es wurde nichts zurückgespielt." });
                return;
            }
            string pfad = SicherungPfad(name);
            if (pfad == null)
            {
                AppLog.Warn("Rückweg nicht gestartet: die Sicherung „" + name + "“ liegt nicht mehr unter sicherungen\\massnahmen.");
                UiPost(new { type = "flowError", message = "Die Sicherung „" + name + "“ liegt nicht mehr im Sicherungsordner; es wurde nichts zurückgespielt. Die Maßnahme lässt sich nach einer neuen Prüfung erneut ausführen." });
                return;
            }
            // Dieselbe Regel wie beim Knopf im Verlauf (M4-Entwurf 4): eine Sicherung aus dem
            // Profil eines anderen Kontos oder eine unlesbare Datei geht gar nicht erst zum
            // Helfer (der lehnte sie mit Exit 2 ab, aber erst nach Bestätigung und UAC-Dialog).
            string profilGrund;
            if (!SicherungFuerEigenesProfil(pfad, out profilGrund))
            {
                AppLog.Warn("Rückweg nicht gestartet: Sicherung „" + name + "“: " + profilGrund);
                UiPost(new { type = "flowError", message = profilGrund });
                return;
            }
            Massnahme hin = HinMassnahme(name);
            string hinTitel = hin != null ? hin.Titel : "Sicherung " + name;
            if (StartAbgelehnt("Rückgängig: " + hinTitel)) return;
            _flowCancel = false;
            AppLog.Info("Rückweg gestartet: Sicherung " + name + (hin != null ? " (" + hin.Kennung + ")" : "") + ".");
            string fname = name; Massnahme fhin = hin;
            _flowThread = new Thread(delegate () { RueckwegWorker(fname, fhin); }) { IsBackground = true, Name = "rueckweg" };
            _flowThread.Start();
        }

        /// <summary>
        /// Plan rueckweg über den Helfer, Nachmessung, Regeln, Nachweis rueckweg aus dem Kern mit
        /// rueckweg.kennung, rueckweg.parameter.&lt;name&gt; und neustart aus den Werten (8.9),
        /// Ergebnis mit Nachweiskarte (hinKennung = rueckweg.kennung). Die Sicherungsdatei bleibt
        /// liegen; die Karte des Rückwegs trägt deshalb keinen zweiten Knopf.
        /// </summary>
        void RueckwegWorker(string name, Massnahme hin)
        {
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            string hinTitel = hin != null ? hin.Titel : "Sicherung " + name;
            string titel = "Rückgängig: " + hinTitel;
            string was = "das Rückgängigmachen von „" + hinTitel + "“";
            try
            {
                UiPost(new { type = "flowStart", mode = "rueckweg", total = 3, titel = titel });
                ErstbefundAbwarten();
                if (_protokoll == null) _protokoll = new Protokoll(Protokoll.NeueLaufId());
                _protokoll.Schreibe(Protokoll.Host, Protokoll.Anfang, KennungRueckweg, titel + " gestartet",
                    new { sicherung = name, hinKennung = hin != null ? hin.Kennung : null });

                bool dialog = !HelferClient.Verbunden && !HelferClient.Abnahmeweg;
                FlowStep(1, dialog ? "Windows fragt nach Administratorrechten" : titel);
                IAusfuehrer a = AusfuehrerHolen(was, KennungRueckweg);
                if (a == null) return;
                if (_flowCancel) { FlowCancelled(); return; }
                if (dialog) FlowStep(1, titel);

                PlanErgebnis erg = PlanAusfuehren(a, Plan.Neu(titel).Mit(KennungRueckweg, "sicherung", name), KennungRueckweg);
                if (!MassnahmeErgebnisPruefen(erg, KennungRueckweg, was, RueckwegFehlerSatz(hinTitel), titel, uhr.Elapsed.TotalSeconds)) return;

                FlowStep(2, "Ihr PC wird erneut gelesen");
                Messen(false);
                FlowStep(3, "Das Ergebnis wird zusammengestellt");
                Bewerten();

                // Die Hin-Maßnahme sagt der Helfer aus der Datei (rueckweg.kennung); der Name der
                // Datei war nur die Vermutung für den Titel bis hierher.
                string hinKennung = erg.Wert(WertRueckwegKennung);
                if (string.IsNullOrEmpty(hinKennung)) hinKennung = hin != null ? hin.Kennung : null;
                Massnahme hinNachher = Massnahmen.Finde(hinKennung) ?? hin;
                if (hinNachher != null) { hinTitel = hinNachher.Titel; titel = "Rückgängig: " + hinTitel; }

                // rueckweg.kennung, rueckweg.parameter.<name>, nachweis.bestanden und nachweis.satz
                // kommen wie alle Werte mit Punkt generisch mit (8.9, M4-Entwurf 10.3), neustart eigens.
                var p = new Dictionary<string, string>(StringComparer.Ordinal);
                p["sicherung"] = name;
                WerteMitPunktUebernehmen(erg, p, null);
                WerteUebernehmen(erg, p, WertNeustart);
                Massnahme rueckweg = Massnahmen.Finde(KennungRueckweg);
                Nachweis nw = rueckweg == null
                    ? Nachweis.Unlesbar("Der Katalog dieser Fassung kennt keinen Nachweis für den Rückweg; ob der frühere Zustand wieder gilt, bleibt offen.")
                    : NachweisBilden(rueckweg, titel, erg, p);
                MassnahmeAbschliessen("rueckweg", KennungRueckweg, titel, null, titel, erg, nw, uhr.Elapsed.TotalSeconds, null, false, hinKennung);
            }
            catch (Exception ex)
            {
                AppLog.Error("Rückweg " + name + " fehlgeschlagen", ex);
                FlowFehler(KennungRueckweg, RueckwegFehlerSatz(hinTitel), ex.Message);
            }
        }

        /// <summary>
        /// Befehl „massnahmeAnsicht“ (UI → Host, Entwurf 9.2): nur die feste Zuordnung
        /// update.einstellungen.oeffnen → ms-settings:windowsupdate, geöffnet über die Oberfläche
        /// des Nutzers (Shell.OeffneImNutzerkontext, nie erhöht, kein Process.Start im Host).
        /// speicher.aufraeumen öffnet die Oberfläche selbst. Jede andere Kennung: Absage als
        /// flowError, nie stumm.
        /// </summary>
        internal void MassnahmeAnsicht(string kennung)
        {
            if (kennung == KennungUpdateEinstellungen)
            {
                AppLog.Info("Ansicht „Windows Update“ geöffnet (ms-settings:windowsupdate, im Nutzerkontext).");
                Shell.OeffneImNutzerkontext("ms-settings:windowsupdate");
                return;
            }
            AppLog.Warn("Ansicht „" + (kennung ?? "") + "“ nicht geöffnet: keine feste Zuordnung im Host.");
            UiPost(new { type = "flowError", message = "Die Ansicht „" + (kennung ?? "") + "“ kennt dieses Programm nicht; es wurde nichts geöffnet." });
        }

        /// <summary>
        /// Nachweis über dem neuen Bild. Bei Exit 1 (Problem im Helfer) ist jeder Nachweis Nein mit
        /// dem Helfer-Satz (Entwurf 8.10): der Kern sieht „lief mit Fehler“ nicht am Bild, und
        /// Kennungen ohne messbaren Zustand lieferten sonst Ja. Wirft die Nachweisfunktion oder
        /// liefert sie nichts, ist der Nachweis nicht messbar (Unlesbar), nie bestanden.
        /// </summary>
        Nachweis NachweisBilden(Massnahme m, string titel, PlanErgebnis erg, Dictionary<string, string> p)
        {
            if (erg.Exit != PlanErgebnis.Ok || erg.Problem)
            {
                string satz = erg.Wert(WertNachweisSatz);
                if (string.IsNullOrEmpty(satz)) satz = string.IsNullOrEmpty(erg.Grund) ? "der Helfer meldete kein Ergebnis." : erg.Grund;
                return Nachweis.Nein(titel + " lief mit Fehler; " + satz);
            }
            try
            {
                Nachweis nw = m.Pruefen(_bild, p);
                return nw ?? Nachweis.Unlesbar("Der Nachweis zu „" + titel + "“ lieferte kein Ergebnis; ob die Maßnahme gewirkt hat, bleibt offen.");
            }
            catch (Exception ex)
            {
                AppLog.Warn("Nachweis " + m.Kennung + ": " + ex.Message);
                return Nachweis.Unlesbar("Der Nachweis zu „" + titel + "“ ließ sich nicht bilden (" + ex.Message + "); ob die Maßnahme gewirkt hat, bleibt offen.");
            }
        }

        /// <summary>
        /// Gemeinsames Ende von Maßnahme und Rückweg: Zeile nachweis im Protokoll, Verlaufseintrag
        /// (good bei bestanden, warn bei nicht bestanden oder nicht messbar, bad nur bei Exit != 0;
        /// Entwurf 9.3) mit der Sicherungsdatei, wenn der Rückweg über sie möglich ist, dann das
        /// Ergebnis mit der Nachweiskarte. schluessel ist der Befund der Karte (null beim
        /// Rückweg), hinKennung die Hin-Maßnahme (nur beim Rückweg).
        /// </summary>
        void MassnahmeAbschliessen(string mode, string kennung, string titel, string schluessel, string verlaufAktion,
                                   PlanErgebnis erg, Nachweis nw, double sekunden, string sicherung, bool rueckwegMoeglich, string hinKennung)
        {
            string satzArt = nw.Unbestimmt ? "Nachweis nicht messbar: " : (nw.Bestanden ? "Nachweis bestanden: " : "Nachweis nicht bestanden: ");
            string kind = erg.Exit != PlanErgebnis.Ok ? "bad" : (nw.Bestanden ? "good" : "warn");
            bool neustart = nw.Neustart || erg.Wert(WertNeustart) == "1";
            if (_protokoll != null)
                _protokoll.Schreibe(Protokoll.Host, Protokoll.Nachweis, kennung, satzArt + nw.Satz,
                    new { bestanden = nw.Bestanden, unbestimmt = nw.Unbestimmt, neustart, exit = erg.Exit, sekunden = (int)sekunden, sicherung, rueckwegMoeglich, hinKennung });
            // Der Verlauf kennt nur „Datei da oder nicht“ und macht daraus den Knopf „Rückgängig“
            // (Entwurf 8.10: nur mit Sicherung UND rueckweg.moeglich != "0"). Deshalb bekommt er den
            // Namen nur, wenn der Rückweg möglich ist; Protokollzeile und Nachweiskarte behalten die
            // Datei als Beleg, die Karte liest rueckwegMoeglich selbst. Sonst führte der Knopf bei
            // update.dienst.aktivieren über Bestätigung und UAC-Dialog immer in die Absage des Helfers.
            History.Add(verlaufAktion, kind, satzArt + nw.Satz, sekunden, rueckwegMoeglich ? sicherung : null);
            AppLog.Info(verlaufAktion + ": " + satzArt + nw.Satz + (sicherung != null ? " (Sicherung " + sicherung + ")" : ""));
            FlowLog((nw.Bestanden ? "✔  " : "●  ") + satzArt + nw.Satz, nw.Bestanden ? LogKind.Good : LogKind.Warn);
            if (neustart) FlowLog("●  Die Änderung wirkt vollständig erst nach einem Neustart.", LogKind.Warn);
            AbbruchNachPruefungMelden();
            PublishResult(mode, new
            {
                kennung = kennung,
                titel = titel,
                schluessel = schluessel,
                bestanden = nw.Bestanden,
                unbestimmt = nw.Unbestimmt,
                neustart = neustart,
                satz = nw.Satz,
                // fehler = der Helfer hat den Schritt nicht sauber beendet (Exit 1 oder Problem):
                // die Kopfzeile der Oberfläche sagt dann „mit Fehler beendet“ statt nur „nicht belegt“.
                fehler = erg.Exit != PlanErgebnis.Ok || erg.Problem,
                sicherung = sicherung,
                rueckwegMoeglich = rueckwegMoeglich,
                hinKennung = hinKennung,
            }, neustart);
        }

        /// <summary>
        /// Ergebnis des Plans einer Maßnahme oder eines Rückwegs übersetzen: wie PlanErgebnisPruefen
        /// beim Hauptweg, mit zwei Unterschieden, weil hier eine Änderung am PC und womöglich eine
        /// Sicherungsdatei im Spiel sind. Erstens zählt ein „Abbrechen“, das erst nach dem fertigen
        /// Schritt ankam (Exit 0 oder 1, nichts Abgebrochenes im Ergebnis), nicht als Abbruch: die
        /// Änderung ist da, also folgen Nachmessung, Nachweis und Karte, und AbbruchNachPruefungMelden
        /// nennt den späten Abbruch in der Konsole; bis dahin sagten Protokoll-Ende und Oberfläche
        /// „Ihrem PC ist nichts passiert“, obwohl Sicherung und Änderung schon da waren. Zweitens
        /// bekommt der Verlauf einen Eintrag, wenn der Lauf ohne Nachweis endet (Abbruch mitten im
        /// Schritt, Absage, Fehler), der Helfer aber schon eine Sicherung geschrieben hat: die
        /// Abbruch-Nachfrage verspricht „eine angelegte Sicherung bleibt liegen“, und ohne Eintrag
        /// führte kein Knopf mehr zu ihr. Den Dateinamen trägt der Eintrag wie in MassnahmeAbschliessen
        /// nur, wenn der Rückweg möglich ist. true nur, wenn die Werte ausgewertet werden dürfen.
        /// </summary>
        bool MassnahmeErgebnisPruefen(PlanErgebnis erg, string kennung, string was, string fehlerSatz, string verlaufAktion, double sekunden)
        {
            bool abgebrochen = erg.Abgebrochen || erg.Exit == PlanErgebnis.AbgebrochenExit;
            if (!abgebrochen && (erg.Exit == PlanErgebnis.Ok || erg.Exit == PlanErgebnis.MitProblem))
            {
                if (_flowCancel) AppLog.Info("Abbruch von " + was + " kam erst nach dem fertigen Schritt (Exit " + erg.Exit + "); Nachmessung und Nachweis laufen.");
                return true;
            }
            string sicherung = SicherungName(erg.Wert(WertSicherungDatei));
            if (sicherung != null)
            {
                bool rueckwegMoeglich = erg.Wert(WertRueckwegMoeglich) != "0";
                abgebrochen = abgebrochen || _flowCancel;
                string satz = (abgebrochen ? "Abgebrochen" : "Mit Fehler beendet") + ", bevor der Nachweis lief; die angelegte Sicherung bleibt liegen";
                History.Add(verlaufAktion, abgebrochen ? "warn" : "bad", satz, sekunden, rueckwegMoeglich ? sicherung : null);
                AppLog.Info(verlaufAktion + ": " + satz + " (Sicherung " + sicherung + (rueckwegMoeglich ? "" : ", kein Rückweg") + ").");
                FlowLog("●  Die angelegte Sicherung bleibt liegen" + (rueckwegMoeglich ? "; „Rückgängig“ steht im Verlauf." : "."), LogKind.Warn);
                if (abgebrochen)
                {
                    FlowCancelled("Abgebrochen; die angelegte Sicherung " + sicherung + " bleibt liegen.");
                    return false;
                }
            }
            return PlanErgebnisPruefen(erg, kennung, was, fehlerSatz);
        }

        /// <summary>Werte des Helfers unter demselben Namen in das Wörterbuch des Nachweises, nur die vorhandenen.</summary>
        static void WerteUebernehmen(PlanErgebnis erg, Dictionary<string, string> p, params string[] namen)
        {
            foreach (string n in namen)
            {
                string v = erg.Wert(n);
                if (!string.IsNullOrEmpty(v)) p[n] = v;
            }
        }

        /// <summary>
        /// Alle Werte des Helfers, deren Name einen Punkt trägt (dism.zustand, sfc.exit,
        /// update.verborgen.vorher, nachweis.bestanden, rueckweg.parameter.&lt;name&gt;, …), unverändert
        /// unter demselben Namen in das Wörterbuch des Nachweises (M4-Entwurf 4 und 10.3); der Kern
        /// liest sie dort. Ein Name, der schon als Befund-Parameter da ist (befundParameter, darf
        /// null sein), bleibt der des Befunds: der Helfer überschreibt nie, womit er gerufen wurde.
        /// Leere Werte werden nicht übernommen (ein leerer Wert ist für den Kern „fehlt“).
        /// </summary>
        static void WerteMitPunktUebernehmen(PlanErgebnis erg, Dictionary<string, string> p, Dictionary<string, string> befundParameter)
        {
            if (erg == null || erg.Werte == null) return;
            foreach (var kv in erg.Werte)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Key.IndexOf('.') < 0 || string.IsNullOrEmpty(kv.Value)) continue;
                if (befundParameter != null && befundParameter.ContainsKey(kv.Key)) continue;
                p[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// Liest eine Sicherungsdatei lesend (Kern.Json mit derselben Klasse wie der Helfer,
        /// Helfer.Sicherung; M4-Entwurf 10.3) und sagt, ob der Rückweg über sie für dieses Konto
        /// in Frage kommt: true, wenn parameter.profilSid fehlt (maschinenweite Sicherung) oder das
        /// eigene Profil nennt; false mit grund bei fremdem Profil, unlesbarer Datei, Datei ohne
        /// Kennung, leerem oder ungültigem profilSid oder unlesbarer eigener SID (was sich nicht
        /// prüfen lässt, bekommt keinen Knopf). Herkunft (Besitzer, Rechte, Hardlinks) prüft
        /// weiter nur der Helfer am Handle; hier geht es nur darum, keinen Knopf zu zeigen, der
        /// immer in seine Absage führt (Rueckweg.InhaltPruefen lehnt genau diese Fälle mit Exit 2
        /// ab). Wirft nie.
        /// </summary>
        static bool SicherungFuerEigenesProfil(string pfad, out string grund)
        {
            grund = null;
            Helfer.Sicherung s;
            try { s = Json.Lesen<Helfer.Sicherung>(File.ReadAllText(pfad, Encoding.UTF8)); }
            catch (Exception ex)
            {
                grund = "Die Sicherungsdatei ließ sich nicht lesen (" + ex.Message.TrimEnd('.') + "); ohne lesbare Sicherung gibt es kein „Rückgängig“.";
                return false;
            }
            // „null“, „[]“ oder ein Objekt ohne kennung lesen sich fehlerfrei, sind aber keine
            // Sicherung: der Helfer wüsste nicht, welchen Rückweg er nehmen soll.
            if (s == null || string.IsNullOrEmpty(s.Kennung))
            {
                grund = "Die Sicherungsdatei trägt keine Kennung; ohne lesbare Sicherung gibt es kein „Rückgängig“.";
                return false;
            }
            // Fehlt der Schlüssel ganz, ist die Sicherung maschinenweit (HKLM, Dienst, Aufgabe …).
            // Steht er da, muss er eine Profil-SID sein: ein leerer Wert ist kein fehlender.
            string profil;
            if (s.Parameter == null || !s.Parameter.TryGetValue(SicherungProfilSchluessel, out profil)) return true;
            if (string.IsNullOrEmpty(profil) || Helfer.LeistungMassnahmen.ProfilSidNorm(profil) == null)
            {
                grund = "Die Sicherung nennt kein gültiges Profil (" + SicherungProfilSchluessel + " muss eine Konto-SID sein); ohne das gibt es kein „Rückgängig“.";
                return false;
            }
            if (Helfer.LeistungMassnahmen.ProfilIstEigenes(profil)) return true;
            grund = Helfer.LeistungMassnahmen.ProfilFremdeSicherung;
            return false;
        }

        static string MassnahmeFehlerSatz(string titel)
        {
            return "Die Maßnahme „" + titel + "“ konnte nicht abgeschlossen werden; den Grund nennen die Zeilen darüber. Sie können es jederzeit erneut versuchen.";
        }

        static string RueckwegFehlerSatz(string hinTitel)
        {
            return "„" + hinTitel + "“ ließ sich nicht rückgängig machen; den Grund nennen die Zeilen darüber. Die Sicherung bleibt liegen, Sie können es jederzeit erneut versuchen.";
        }

        /// <summary>Parameter als „a=1; b=2“ für das Protokoll (Werte sind Kennungen des Befunds, keine Geheimnisse).</summary>
        static string ParameterText(Dictionary<string, string> parameter)
        {
            if (parameter == null || parameter.Count == 0) return "";
            return string.Join("; ", parameter.Select(kv => kv.Key + "=" + kv.Value));
        }

        /// <summary>
        /// Nur der Dateiname einer Sicherung nach dem Muster der Kennung rueckweg; null bei leer,
        /// Pfadzeichen oder fremdem Muster. Ein voller Pfad aus den Werten des Helfers
        /// (sicherung.datei) wird auf seinen Namen gekürzt: die Oberfläche und der Verlauf kennen
        /// nur Namen, der Ort ist immer sicherungen\massnahmen.
        /// </summary>
        static string SicherungName(string sicherung)
        {
            if (string.IsNullOrEmpty(sicherung)) return null;
            string name;
            try { name = Path.GetFileName(sicherung); }
            catch (Exception) { return null; }
            return !string.IsNullOrEmpty(name) && SicherungNameRx.IsMatch(name) ? name : null;
        }

        /// <summary>
        /// Voller Pfad einer Sicherungsdatei unter sicherungen\massnahmen, wenn sie dort liegt,
        /// sonst null. Zusammengesetzt aus Ablage.Maschinenweit() (Entwurf 8.10), nicht über
        /// Ablage.Unterordner oder Ablage.Sicherungen: die legten den Ordner an, und der gehört
        /// dem erhöhten Helfer (eigene DACL). Liegt eine Ebene hinter einer Abzweigung, gilt
        /// „nicht vorhanden“. Wirft nie.
        /// </summary>
        static string SicherungPfad(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                string pfad = Path.Combine(Ablage.Maschinenweit(), "sicherungen", "massnahmen", name);
                if (Ablage.IstAbzweigung(pfad)) return null;
                return File.Exists(pfad) ? pfad : null;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Sicherung „" + name + "“ nicht prüfbar: " + ex.Message);
                return null;
            }
        }

        /// <summary>Die Hin-Maßnahme aus der Kennung im Dateinamen der Sicherung; null, wenn der Name das Muster nicht trägt oder der Katalog die Kennung nicht kennt.</summary>
        static Massnahme HinMassnahme(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Match t = SicherungKennungRx.Match(name);
            if (!t.Success) return null;
            string kennung = t.Groups["k"].Value;
            return kennung == KennungRueckweg ? null : Massnahmen.Finde(kennung);
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
            _planLetzteBadZeile = null;
            var k = new Helfer.Ausfuehrungskontext();
            k.Zeile = delegate (string text, string art, int? prozent)
            {
                if (_flowCancel) FlowAbbruchNachreichen(a);
                if (prozent.HasValue)
                {
                    UiPost(new { type = "flowPercent", percent = prozent.Value });
                    if (string.IsNullOrEmpty(text)) return;   // reine Fortschrittszeile: keine Konsolenzeile
                }
                if (art == "bad" && !string.IsNullOrWhiteSpace(text)) _planLetzteBadZeile = text.Trim();
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

        /// <summary>
        /// Abbruch-Ende eines Laufs: Protokoll-Ende, kein Nachlauf, flowCancelled. endeSatz ersetzt
        /// den Satz der Ende-Zeile, wenn „nichts passiert“ nicht stimmt (Maßnahme mit schon
        /// geschriebener Sicherung, MassnahmeErgebnisPruefen).
        /// </summary>
        void FlowCancelled(string endeSatz = null)
        {
            AppLog.Info("Ablauf abgebrochen.");
            if (_protokoll != null) _protokoll.Schreibe(Protokoll.Host, Protokoll.Ende, "abgebrochen", endeSatz ?? "Abgebrochen. Ihrem PC ist nichts passiert.");
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
        /// Urteil über die Windows-Dateien aus den Werten des Helfers (M4-Entwurf 10.3): DISM nur
        /// aus dism.zustand (healthy → ok, repairable → „reparierbar“ als warn, nonrepairable →
        /// bad, fehler oder fehlend → unbekannt, mit dism.hresult im Satz), SFC aus sfc.zustand
        /// (ohneBefund → ok, repariert → ok mit Neustart-Rat, nichtRepariert → bad, fehler oder
        /// fehlend → unbekannt, mit sfc.exit im Satz). Die Textsuche in dism.ausgabe (Explain)
        /// entfällt; der Übergangswert bleibt nur in alten Protokollen. Kein Wert wird gedeutet,
        /// der nicht da ist: fehlt einer der beiden, gibt es kein „in Ordnung“, aber auch keinen
        /// Befund (unbekannt). Ein Neustart gilt, wenn SFC repariert hat oder der Helfer neustart
        /// = "1" meldet (sfc.exit 3010, „pending“ in CBS.log).
        /// </summary>
        void EvaluateFiles(PlanErgebnis erg)
        {
            _filesSummary = null;
            _filesAdvice = null;
            _filesNeustart = false;

            string dism = (erg.Wert(WertDismZustand) ?? "").Trim();
            string sfc = (erg.Wert(WertSfcZustand) ?? "").Trim();
            string hresult = erg.Wert(WertDismHresult);
            string sfcExit = erg.Wert(WertSfcExit);

            // Beide Urteile getrennt, dann gilt das schlechtere. Bis 12.09.2026 gewann bei
            // unbekannter SFC-Ausgabe (abgebrochen, unbekannte Meldung) die DISM-Deutung, und
            // ein Lauf ohne SFC-Ergebnis stand als "unbeschädigt" da.
            string dismZustand;
            switch (dism)
            {
                case "healthy": dismZustand = Zustand.Ok; break;
                case "repairable": dismZustand = Zustand.Warn; break;
                case "nonrepairable": dismZustand = Zustand.Bad; break;
                default: dismZustand = Zustand.Unknown; break;
            }
            bool repariert = sfc == "repariert";
            string sfcZustand;
            switch (sfc)
            {
                case "ohneBefund": case "repariert": sfcZustand = Zustand.Ok; break;
                case "nichtRepariert": sfcZustand = Zustand.Bad; break;
                default: sfcZustand = Zustand.Unknown; break;
            }
            // Unbekannt zählt hier wie ein eigener Rang zwischen ok und warn: ohne SFC-Ergebnis
            // gibt es kein "in Ordnung", aber auch keinen Befund.
            string zustand;
            if (sfcZustand == Zustand.Bad || dismZustand == Zustand.Bad) zustand = Zustand.Bad;
            else if (dismZustand == Zustand.Warn) zustand = Zustand.Warn;
            else if (sfcZustand == Zustand.Unknown || dismZustand == Zustand.Unknown) zustand = Zustand.Unknown;
            else zustand = Zustand.Ok;
            _filesState = zustand;
            _filesNeustart = repariert || erg.Wert(WertNeustart) == "1";

            if (zustand == Zustand.Bad)
            {
                if (dismZustand == Zustand.Bad)
                {
                    _filesSummary = "Die Grundbestandteile von Windows (Komponentenspeicher) sind beschädigt und lassen sich mit DISM nicht reparieren"
                                    + (sfcZustand == Zustand.Bad ? "; auch beschädigte Windows-Dateien blieben unrepariert." : ".");
                    _filesAdvice = "Bleibt der Befund nach einem zweiten Lauf mit Internetverbindung, hilft nur eine Windows-Reparaturinstallation (Inplace-Upgrade); eigene Dateien und Programme bleiben dabei erhalten.";
                }
                else
                {
                    _filesSummary = "Es wurden beschädigte Windows-Dateien gefunden, die nicht alle repariert werden konnten.";
                    _filesAdvice = "Lassen Sie die Reparatur laufen. Bleibt der Befund, hilft eine Windows-Reparaturinstallation.";
                }
                return;
            }
            if (zustand == Zustand.Warn)
            {
                _filesSummary = "An den Grundbestandteilen von Windows wurden Beschädigungen gefunden; sie sind reparierbar, solange Windows Update erreichbar ist.";
                _filesAdvice = "Lassen Sie die Reparatur laufen. Sie holt die fehlenden Bestandteile über das Internet neu.";
                return;
            }
            if (zustand == Zustand.Unknown)
            {
                string dismSatz = dism == "fehler"
                    ? "die Prüfung der Grundbestandteile (DISM) meldete einen Fehler" + (string.IsNullOrEmpty(hresult) ? "" : " (" + hresult + ")")
                    : "die Prüfung der Grundbestandteile (DISM) lieferte kein Ergebnis";
                string sfcSatz = sfc == "fehler"
                    ? "die Dateiprüfung (SFC) meldete einen Fehler" + (string.IsNullOrEmpty(sfcExit) ? "" : " (Exit " + sfcExit + ")")
                    : "die Dateiprüfung (SFC) lieferte kein Ergebnis";
                if (sfcZustand == Zustand.Unknown && dismZustand == Zustand.Unknown)
                    _filesSummary = "Der Zustand der Windows-Dateien ließ sich nicht bestimmen: " + dismSatz + ", und " + sfcSatz + ".";
                else if (sfcZustand == Zustand.Unknown)
                    _filesSummary = "Die Grundbestandteile sind geprüft, aber " + sfcSatz + ".";
                else
                    _filesSummary = "Die Dateien sind geprüft, aber " + dismSatz + ".";
                return;
            }
            _filesSummary = repariert
                ? "Beschädigte Windows-Dateien wurden gefunden und repariert."
                : "Alle geschützten Windows-Dateien und die Grundbestandteile von Windows sind in Ordnung.";
            if (_filesNeustart)
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

        /// <summary>
        /// Stellt das Ergebnis zusammen und schickt es an die Oberfläche. nachweis (nur bei mode
        /// massnahme oder rueckweg, Entwurf 9.1) ist die Nachweiskarte; der Verlaufseintrag dieser
        /// beiden Wege steht dann schon (MassnahmeAbschliessen: „Maßnahme: …“ mit Sicherungsdatei),
        /// ein zweiter „Prüfung“-Eintrag wäre eine Lüge. neustart ergänzt das Feld restart, wenn
        /// der Nachweis erst nach einem Neustart gilt.
        /// </summary>
        void PublishResult(string mode, object nachweis = null, bool neustart = false)
        {
            var zeilen = new List<object> { DateiZeile() };
            zeilen.AddRange(_ergebnisse.Select(Bereichszeile));

            var befunde = Alle.AlleBefunde(_ergebnisse).ToList();
            string overall = Zustand.Schlechtester(_ergebnisse.Select(x => x.Zustand).Concat(new[] { _filesGeprueft ? _filesState : Zustand.Unknown }));
            int problems = Alle.Probleme(_ergebnisse) + ((_filesState == Zustand.Warn || _filesState == Zustand.Bad) ? 1 : 0);
            int fragen = Alle.OffeneFragen(_ergebnisse).Count();
            bool fixable = _filesState == Zustand.Warn || _filesState == Zustand.Bad;

            // Braucht der PC einen Neustart? Nur aus Befunden, die es ausdrücklich sagen, aus
            // der Reparatur der Windows-Dateien oder aus dem Nachweis einer Maßnahme.
            bool restart = befunde.Any(b => b.IstProblem && b.Schluessel != null && b.Schluessel.StartsWith("update.neustart"))
                           || _filesNeustart || neustart;
            bool massnahme = mode == "massnahme" || mode == "rueckweg";

            if (mode != "answer")
            {
                if (!massnahme)
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
                // Nachweiskarte (Entwurf 9.1); null bei allen anderen Wegen, die Oberfläche zeigt sie nur bei mode massnahme|rueckweg.
                nachweis = nachweis,
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

        // ---------------------------------------------------------------- Techniker-Ansicht (Meilenstein 4)

        /// <summary>Zahl der jüngsten Protokolldateien, die die Techniker-Ansicht nennt (M4-Entwurf 4).</summary>
        const int TechnikerProtokollAnzahl = 10;

        /// <summary>
        /// Befehl „technikerAnsicht“ (UI → Host, M4-Entwurf 4): alle Befunde des letzten Ergebnisses,
        /// auch „in Ordnung“, mit Messwert, Schwelle, Quelle, Detail, Kennungen und Parametern; dazu
        /// die Fehlerliste des Systembilds, was nicht geprüft wurde, die zehn jüngsten Protokolle
        /// und der Laufzeitordner. Nur lesend, nichts wird geändert. Ohne Ergebnis (kein Bild) ist
        /// die Antwort techniker {leer: true}. Bild und Ergebnisse werden als Stand von jetzt
        /// gefasst; die Dateizugriffe (Protokolle, Sicherungen zählen) laufen im Hintergrund, die
        /// Antwort geht per UiPost. Nie stumm: ein Fehler beim Zusammenstellen kommt als leer mit fehler.
        /// </summary>
        internal void TechnikerAnsicht()
        {
            Systembild bild = _bild;
            List<BereichErgebnis> ergebnisse = _ergebnisse;
            if (bild == null)
            {
                UiPost(new { type = "techniker", leer = true });
                return;
            }
            Thread t = new Thread(delegate ()
            {
                try { UiPost(TechnikerDaten(bild, ergebnisse)); }
                catch (Exception ex)
                {
                    AppLog.Warn("Techniker-Ansicht nicht erstellt: " + ex.Message);
                    UiPost(new { type = "techniker", leer = true, fehler = "Die Techniker-Ansicht ließ sich nicht zusammenstellen (" + ex.Message.TrimEnd('.') + "); nach einer neuen Prüfung geht es erneut." });
                }
            }) { IsBackground = true, Name = "techniker" };
            t.Start();
        }

        /// <summary>Die Nachricht techniker (Feldnamen aus M4-Entwurf 4) aus Bild und Ergebnissen.</summary>
        object TechnikerDaten(Systembild bild, List<BereichErgebnis> ergebnisse)
        {
            var bereiche = new List<object>();
            foreach (var b in ergebnisse ?? new List<BereichErgebnis>())
            {
                if (b == null) continue;
                bereiche.Add(new
                {
                    bereich = b.Bereich,
                    titel = Bereich.Titel(b.Bereich),
                    zustand = b.Zustand,
                    fehlend = b.Fehlend.ToArray(),
                    befunde = b.Befunde.Where(f => f != null).Select(TechnikerBefund).ToArray(),
                });
            }
            // Der Dateibefund der Tiefenprüfung liegt nicht im Systembild (eigene Felder); er gehört
            // trotzdem zu „alle Befunde“, sobald geprüft wurde.
            if (_filesGeprueft)
            {
                bereiche.Add(new
                {
                    bereich = "files",
                    titel = "Windows-Dateien",
                    zustand = _filesState,
                    fehlend = new string[0],
                    befunde = new object[]
                    {
                        new
                        {
                            schluessel = "windows.dateien", zustand = _filesState, titel = "Windows-Dateien", satz = _filesSummary, rat = _filesAdvice,
                            messwert = (string)null, einheit = (string)null, schwelle = (string)null, quelle = "DISM-API (dism.zustand), sfc.exe (sfc.zustand)",
                            detail = new string[0], massnahmen = new string[0], parameter = new Dictionary<string, string>(), frage = (string)null, antwort = (string)null,
                        },
                    },
                });
            }

            Kern.Windows w = bild.Windows ?? new Kern.Windows();
            Kern.Hardware h = bild.Hardware ?? new Kern.Hardware();
            return new
            {
                type = "techniker",
                version = typeof(ShellForm).Assembly.GetName().Version.ToString(3),
                aufgezeichnet = ZeitText(bild.AufgezeichnetUtc),
                erhoeht = bild.Erhoeht,
                windows = new
                {
                    produkt = string.IsNullOrEmpty(w.Caption) ? w.Edition : w.Caption,
                    build = w.Build > 0 ? w.Build + "." + w.Ubr + (string.IsNullOrEmpty(w.DisplayVersion) ? "" : " (" + w.DisplayVersion + ")") : null,
                    sprache = SpracheText(bild.Sprache),
                },
                hardware = new
                {
                    system = ((h.Hersteller ?? "") + " " + (h.Modell ?? "")).Trim(),
                    cpu = h.Cpu,
                    ramGb = h.RamGesamtKB > 0 ? Math.Round(h.RamGesamtKB / 1048576.0, 1) : (double?)null,
                },
                bereiche = bereiche.ToArray(),
                fehlerliste = (bild.Fehlerliste ?? new List<Fehler>()).Where(f => f != null)
                    .Select(f => new { quelle = f.Quelle, art = f.Art, text = f.Text }).ToArray(),
                protokolle = TechnikerProtokolle().Select(p => new { name = p.Name, zeit = p.LastWriteTime.ToString("dd.MM.yyyy HH:mm"), groesse = p.Length }).ToArray(),
                laufzeitordner = Ablage.Maschinenweit(),
                sicherungen = SicherungenZaehlen(),
            };
        }

        /// <summary>Ein Befund für die Techniker-Ansicht: alle Felder, die der Kern trägt, plus die gespeicherte Antwort auf seine Frage.</summary>
        object TechnikerBefund(Befund f)
        {
            string antwort = null;
            if (f.Frage != null && !string.IsNullOrEmpty(f.Frage.Id))
            {
                try { antwort = Entscheidungen.AntwortAuf(f.Frage.Id); }
                catch (Exception ex) { AppLog.Warn("Antwort zu " + f.Frage.Id + " nicht lesbar: " + ex.Message); }
            }
            return new
            {
                schluessel = f.Schluessel,
                zustand = f.Zustand,
                titel = f.Titel,
                satz = f.Satz,
                rat = f.Rat,
                messwert = f.Messwert == null ? null : f.Messwert.Wert,
                einheit = f.Messwert == null ? null : f.Messwert.Einheit,
                schwelle = f.Messwert == null ? null : f.Messwert.Schwelle,
                quelle = f.Quelle,
                detail = (f.Detail ?? new List<string>()).ToArray(),
                massnahmen = (f.Massnahmen ?? new List<string>()).ToArray(),
                parameter = f.Parameter ?? new Dictionary<string, string>(),
                frage = f.Frage == null ? null : f.Frage.Text,
                antwort = antwort,
            };
        }

        /// <summary>ISO-Zeit des Bilds als Ortszeit „dd.MM.yyyy HH:mm:ss“; unlesbar → der Rohtext, leer → null.</summary>
        static string ZeitText(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            DateTime? t = Zeit.Lesen(iso);
            return t.HasValue ? t.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") : iso;
        }

        /// <summary>Systemsprache als „1031 (de-DE)“; 0 → null; unbekannte LCID → nur die Zahl.</summary>
        static string SpracheText(Kern.Sprache s)
        {
            if (s == null || s.Lcid == 0) return null;
            try { return s.Lcid + " (" + System.Globalization.CultureInfo.GetCultureInfo(s.Lcid).Name + ")"; }
            catch (Exception) { return s.Lcid.ToString(); }
        }

        /// <summary>
        /// Die jüngsten Dateien des Protokollordners (lauf-*.jsonl und dism-*.log liegen dort; Kern
        /// Protokoll.Ordner, null hinter einer Abzweigung), neueste zuerst, höchstens
        /// TechnikerProtokollAnzahl. Wirft nie; ohne Ordner eine leere Liste.
        /// </summary>
        static List<FileInfo> TechnikerProtokolle()
        {
            try
            {
                string ordner = Protokoll.Ordner();
                if (string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner)) return new List<FileInfo>();
                return new DirectoryInfo(ordner).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).Take(TechnikerProtokollAnzahl).ToList();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Protokolle nicht gelistet: " + ex.Message);
                return new List<FileInfo>();
            }
        }

        /// <summary>
        /// Zahl der Sicherungsdateien unter sicherungen\massnahmen, gebildet wie SicherungPfad (kein
        /// Anlegen, Abzweigung zählt als 0). Wirft nie.
        /// </summary>
        static int SicherungenZaehlen()
        {
            try
            {
                string ordner = Path.Combine(Ablage.Maschinenweit(), "sicherungen", "massnahmen");
                if (Ablage.IstAbzweigung(ordner) || !Directory.Exists(ordner)) return 0;
                return Directory.GetFiles(ordner, "*.json").Length;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Sicherungen nicht gezählt: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Befehl „technikerText“ (UI → Host, M4-Entwurf 4): der Bericht (BerichtText) plus die
        /// Abschnitte „ALLE BEFUNDE“ (auch in Ordnung, mit Schlüssel, Quelle, Messwert, Schwelle,
        /// Kennungen, Parametern und Einzelheiten), „SYSTEMBILD-FEHLER“ und „PROTOKOLLE“; die
        /// Oberfläche kopiert ihn in die Zwischenablage. Im Thread der Oberfläche, weil BerichtText
        /// den Konsolenpuffer liest, der nur dort geschrieben wird.
        /// </summary>
        internal string TechnikerText()
        {
            var sb = new StringBuilder(BerichtText());
            var ergebnisse = _ergebnisse ?? new List<BereichErgebnis>();
            sb.AppendLine();
            sb.AppendLine("ALLE BEFUNDE (auch in Ordnung)");
            if (ergebnisse.Count == 0) sb.AppendLine("  Es liegt kein Ergebnis vor (0 Bereiche); erst prüfen.");
            foreach (var b in ergebnisse)
            {
                if (b == null) continue;
                sb.AppendLine();
                sb.AppendLine("[" + b.Zustand + "] " + Bereich.Titel(b.Bereich) + " (" + b.Befunde.Count + " Befund(e))");
                foreach (var f in b.Befunde)
                {
                    if (f == null) continue;
                    sb.AppendLine("  " + f.Schluessel + "  [" + f.Zustand + "]  " + f.Titel + ": " + f.Satz);
                    sb.AppendLine("      Quelle: " + (f.Quelle ?? "?")
                                  + (f.Messwert != null ? "  Messwert: " + f.Messwert.Wert + " " + f.Messwert.Einheit + (f.Messwert.Schwelle != null ? " (Schwelle " + f.Messwert.Schwelle + ")" : "") : ""));
                    if (!string.IsNullOrEmpty(f.Rat)) sb.AppendLine("      Rat: " + f.Rat);
                    if (f.Massnahmen != null && f.Massnahmen.Count > 0)
                        sb.AppendLine("      Kennungen: " + string.Join(", ", f.Massnahmen) + (f.Parameter != null && f.Parameter.Count > 0 ? "  Parameter: " + ParameterText(f.Parameter) : ""));
                    if (f.Frage != null)
                    {
                        string antwort = null;
                        try { antwort = Entscheidungen.AntwortAuf(f.Frage.Id); } catch (Exception) { }
                        sb.AppendLine("      Frage: " + f.Frage.Text + "  Antwort: " + (antwort ?? "offen"));
                    }
                    foreach (string d in f.Detail ?? new List<string>()) sb.AppendLine("      " + d);
                }
                foreach (string fe in b.Fehlend) sb.AppendLine("  Nicht geprüft: " + fe);
            }
            if (_filesGeprueft)
            {
                sb.AppendLine();
                sb.AppendLine("[" + _filesState + "] Windows-Dateien (1 Befund)");
                sb.AppendLine("  windows.dateien  [" + _filesState + "]  Windows-Dateien: " + _filesSummary);
                sb.AppendLine("      Quelle: DISM-API (dism.zustand), sfc.exe (sfc.zustand)");
                if (!string.IsNullOrEmpty(_filesAdvice)) sb.AppendLine("      Rat: " + _filesAdvice);
            }

            sb.AppendLine();
            sb.AppendLine("SYSTEMBILD-FEHLER");
            var fehler = _bild != null && _bild.Fehlerliste != null ? _bild.Fehlerliste.Where(f => f != null).ToList() : new List<Fehler>();
            if (fehler.Count == 0) sb.AppendLine("  Keine (0 Einträge): jede Quelle hat geliefert.");
            foreach (var fe in fehler)
                sb.AppendLine("  " + fe.Quelle + " [" + fe.Art + "] " + fe.Text + (fe.Art == Fehler.Zugriff ? " (mit Administratorrechten ergänzbar)" : ""));

            sb.AppendLine();
            string protokollOrdner = null;
            try { protokollOrdner = Protokoll.Ordner(); } catch (Exception) { }
            sb.AppendLine("PROTOKOLLE (Ordner: " + (protokollOrdner ?? "keiner") + ", die " + TechnikerProtokollAnzahl + " jüngsten)");
            var protokolle = TechnikerProtokolle();
            if (protokolle.Count == 0) sb.AppendLine("  Keine Protokolldatei (0 Dateien).");
            foreach (var p in protokolle)
                sb.AppendLine("  " + p.LastWriteTime.ToString("dd.MM.yyyy HH:mm") + "  " + p.Length.ToString().PadLeft(9) + " Byte  " + p.Name);
            sb.AppendLine();
            sb.AppendLine("LAUFZEITORDNER");
            sb.AppendLine("  " + Ablage.Maschinenweit() + "  (Sicherungen unter sicherungen\\massnahmen: " + SicherungenZaehlen() + ")");
            return sb.ToString();
        }
    }
}
